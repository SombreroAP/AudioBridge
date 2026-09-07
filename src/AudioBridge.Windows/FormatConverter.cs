using AudioBridge.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioBridge.Windows;

/// <summary>
/// Bridges whatever format a device happens to run at to the fixed format we put on the wire.
///
/// This matters more than it looks. WASAPI hands back 32-bit float at whatever rate the user
/// set in Sound Control Panel — often 44.1 kHz on a headset, 48 kHz on an interface — while
/// both ends of the link must agree on one format or the audio plays back at the wrong pitch.
/// So every capture path funnels through here.
/// </summary>
internal sealed class FormatConverter
{
    private readonly BufferedWaveProvider _source;
    private readonly IWaveProvider _output;
    private readonly byte[] _readBuffer;
    private readonly int _bytesPerFrame;

    public FormatConverter(WaveFormat sourceFormat, AudioFormat target, int blockSizeBytes)
    {
        _source = new BufferedWaveProvider(sourceFormat)
        {
            // Without this the provider pads short reads with silence, which would inject a
            // gap into the stream every time capture briefly runs dry.
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(500),
        };

        ISampleProvider samples = _source.ToSampleProvider();

        if (sourceFormat.Channels != target.Channels)
        {
            samples = target.Channels == 1
                ? samples.ToMono()
                : samples.ToStereo();
        }

        if (sourceFormat.SampleRate != target.SampleRate)
            samples = new WdlResamplingSampleProvider(samples, target.SampleRate);

        _output = new SampleToWaveProvider16(samples);
        _bytesPerFrame = target.BytesPerFrame;
        _readBuffer = new byte[blockSizeBytes];
    }

    public void Write(byte[] buffer, int offset, int count) => _source.AddSamples(buffer, offset, count);

    /// <summary>Pulls whole blocks of converted audio, invoking <paramref name="onBlock"/> for
    /// each. Returns without calling back when there isn't a full block ready yet.</summary>
    public void DrainTo(Action<ReadOnlyMemory<byte>> onBlock)
    {
        while (true)
        {
            var read = _output.Read(_readBuffer, 0, _readBuffer.Length);
            if (read < _readBuffer.Length)
            {
                // A partial block means the source ran dry. Push the remainder back so the
                // next drain resumes mid-block rather than dropping those samples.
                var aligned = read / _bytesPerFrame * _bytesPerFrame;
                if (aligned > 0) onBlock(_readBuffer.AsMemory(0, aligned));
                return;
            }
            onBlock(_readBuffer.AsMemory(0, read));
        }
    }
}
