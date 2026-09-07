using System.Buffers.Binary;

namespace AudioBridge.Core;

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    /// <summary>Payload is Opus-encoded rather than raw PCM.</summary>
    Opus = 1 << 0,
    /// <summary>Sender detected silence; payload is empty and the receiver should render silence.</summary>
    Silence = 1 << 1,
}

/// <summary>
/// One datagram of audio. Header is 16 bytes, little-endian, followed by the payload.
///
///   0..3   magic 'A' 'B' 'R' 'G'
///   4      version (currently 1)
///   5      flags
///   6..7   stream id
///   8..11  sequence number (wraps; compare with <see cref="IsNewer"/>)
///   12..15 timestamp, in frames since the stream started
/// </summary>
public readonly record struct AudioPacket(
    StreamId Stream,
    uint Sequence,
    uint Timestamp,
    PacketFlags Flags,
    ReadOnlyMemory<byte> Payload)
{
    public const int HeaderSize = 16;
    public const byte CurrentVersion = 1;
    private const uint Magic = 0x47524241; // "ABRG" read little-endian

    /// <summary>Largest payload we will put in one datagram. Chosen to stay under a
    /// 1500-byte MTU once the 16-byte header and the IP/UDP headers are added, so a
    /// packet never fragments on a normal LAN.</summary>
    public const int MaxPayloadSize = 1400;

    public int TotalSize => HeaderSize + Payload.Length;

    /// <summary>Writes the packet into <paramref name="destination"/>; returns bytes written.</summary>
    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < TotalSize)
            throw new ArgumentException($"Need {TotalSize} bytes, got {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[4] = CurrentVersion;
        destination[5] = (byte)Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)Stream);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Timestamp);
        Payload.Span.CopyTo(destination[HeaderSize..]);
        return TotalSize;
    }

    public byte[] ToArray()
    {
        var buffer = new byte[TotalSize];
        WriteTo(buffer);
        return buffer;
    }

    /// <summary>Parses a received datagram. Returns false for anything that isn't a
    /// well-formed packet of a version we understand — the socket is open to the LAN,
    /// so garbage and stray traffic are expected and must never throw.</summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out AudioPacket packet)
    {
        packet = default;
        if (source.Length < HeaderSize) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic) return false;
        if (source[4] != CurrentVersion) return false;

        packet = new AudioPacket(
            (StreamId)BinaryPrimitives.ReadUInt16LittleEndian(source[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
            (PacketFlags)source[5],
            source[HeaderSize..].ToArray());
        return true;
    }

    /// <summary>True when <paramref name="sequence"/> comes after <paramref name="other"/>,
    /// correct across the 32-bit wrap.</summary>
    public static bool IsNewer(uint sequence, uint other) => unchecked((int)(sequence - other)) > 0;
}
