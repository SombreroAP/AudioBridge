using System.Buffers.Binary;
using System.Text;

namespace AudioBridge.Core;

/// <summary>What each PC shouts on the LAN so the other can find it without any
/// configuration. Deliberately tiny and hand-rolled rather than JSON: it goes out once a
/// second forever, and a fixed layout can't be tripped up by a malformed payload.
///
///   0..3   magic 'A' 'B' 'D' 'S'
///   4      version
///   5      role
///   6..7   audio port
///   8..23  instance id (GUID)
///   24     name length in bytes
///   25..   name, UTF-8
/// </summary>
public readonly record struct DiscoveryBeacon(Guid InstanceId, string MachineName, ushort AudioPort, PeerRole Role)
{
    private const uint Magic = 0x53444241; // "ABDS" little-endian
    public const byte CurrentVersion = 1;
    private const int FixedHeaderSize = 25;
    private const int MaxNameBytes = 120;

    public int Write(Span<byte> destination)
    {
        var nameBytes = Encoding.UTF8.GetBytes(MachineName);
        if (nameBytes.Length > MaxNameBytes)
            nameBytes = nameBytes[..MaxNameBytes];

        var total = FixedHeaderSize + nameBytes.Length;
        if (destination.Length < total)
            throw new ArgumentException($"Need {total} bytes, got {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[4] = CurrentVersion;
        destination[5] = (byte)Role;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], AudioPort);
        InstanceId.TryWriteBytes(destination[8..24]);
        destination[24] = (byte)nameBytes.Length;
        nameBytes.CopyTo(destination[FixedHeaderSize..]);
        return total;
    }

    public byte[] ToArray()
    {
        var buffer = new byte[FixedHeaderSize + Encoding.UTF8.GetByteCount(MachineName)];
        var written = Write(buffer);
        return written == buffer.Length ? buffer : buffer[..written];
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out DiscoveryBeacon beacon)
    {
        beacon = default;
        if (source.Length < FixedHeaderSize) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic) return false;
        if (source[4] != CurrentVersion) return false;

        int nameLength = source[24];
        if (source.Length < FixedHeaderSize + nameLength) return false;

        beacon = new DiscoveryBeacon(
            new Guid(source[8..24]),
            Encoding.UTF8.GetString(source.Slice(FixedHeaderSize, nameLength)),
            BinaryPrimitives.ReadUInt16LittleEndian(source[6..]),
            (PeerRole)source[5]);
        return true;
    }
}

/// <summary>Which end of the setup a PC is acting as. Sent in the beacon so the UI can
/// label peers correctly before the user has configured anything.</summary>
public enum PeerRole : byte
{
    Unconfigured = 0,
    /// <summary>Runs the games. Sends game audio, receives the microphone.</summary>
    GamingPc = 1,
    /// <summary>Has the headphones and the good microphone. Receives game audio, sends the mic.</summary>
    StreamingPc = 2,
}
