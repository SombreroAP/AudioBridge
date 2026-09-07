namespace AudioBridge.Core;

/// <summary>
/// Absorbs network jitter and reordering between the receiving socket and the audio
/// render callback.
///
/// The render thread must never block, so the buffer holds back a small number of
/// packets (<see cref="TargetDepth"/>) before it starts handing any out. That delay is
/// the price of not glitching, and it's the single knob that trades latency against
/// robustness — the UI exposes it as the "stability" slider.
///
/// Not thread-safe on its own; callers hold a lock, since the socket thread pushes and
/// the audio thread pops.
/// </summary>
public sealed class JitterBuffer
{
    private readonly Dictionary<uint, AudioPacket> _packets = new();
    private uint _nextSequence;
    private bool _started;

    public JitterBuffer(int targetDepth = 3, int capacity = 64)
    {
        if (targetDepth < 1) throw new ArgumentOutOfRangeException(nameof(targetDepth));
        if (capacity < targetDepth) throw new ArgumentOutOfRangeException(nameof(capacity));
        TargetDepth = targetDepth;
        Capacity = capacity;
    }

    /// <summary>How many packets to accumulate before playback starts.</summary>
    public int TargetDepth { get; }

    /// <summary>Hard ceiling; past this the buffer discards its oldest gap rather than grow.</summary>
    public int Capacity { get; }

    public int Count => _packets.Count;

    /// <summary>Packets dropped because they arrived after their slot had already played.</summary>
    public int LatePacketCount { get; private set; }

    /// <summary>Slots that never arrived and were concealed with silence.</summary>
    public int ConcealedPacketCount { get; private set; }

    /// <summary>True once enough packets have accumulated for playback to begin.</summary>
    public bool IsPrimed => _started;

    /// <summary>Adds a received packet. Returns false if it was a duplicate or arrived too late.</summary>
    public bool Push(in AudioPacket packet)
    {
        if (_started && !AudioPacket.IsNewer(packet.Sequence, unchecked(_nextSequence - 1)))
        {
            LatePacketCount++;
            return false;
        }

        if (!_packets.TryAdd(packet.Sequence, packet))
            return false; // duplicate

        if (!_started && _packets.Count >= TargetDepth)
        {
            _nextSequence = OldestSequence();
            _started = true;
        }

        // Never let a persistent gap grow the buffer without bound.
        while (_packets.Count > Capacity)
            AdvancePastGap();

        return true;
    }

    /// <summary>
    /// Hands out the next packet in sequence. Returns false on underrun — the caller
    /// should render silence and try again on the next callback.
    /// A packet flagged <see cref="PacketFlags.Silence"/> with an empty payload means the
    /// slot was lost and concealed.
    /// </summary>
    public bool TryDequeue(out AudioPacket packet)
    {
        packet = default;
        if (!_started) return false;

        if (_packets.Remove(_nextSequence, out packet))
        {
            _nextSequence = unchecked(_nextSequence + 1);
            return true;
        }

        // The slot is missing. Only conceal it once we're holding enough later packets to
        // be confident it's genuinely lost rather than merely in flight.
        if (_packets.Count >= TargetDepth)
        {
            packet = new AudioPacket(default, _nextSequence, 0, PacketFlags.Silence, ReadOnlyMemory<byte>.Empty);
            _nextSequence = unchecked(_nextSequence + 1);
            ConcealedPacketCount++;
            return true;
        }

        return false;
    }

    /// <summary>Drops everything and re-primes. Used when a stream stops or the sender restarts.</summary>
    public void Reset()
    {
        _packets.Clear();
        _started = false;
        _nextSequence = 0;
    }

    private void AdvancePastGap()
    {
        var oldest = OldestSequence();
        ConcealedPacketCount += (int)unchecked(oldest - _nextSequence);
        _packets.Remove(oldest);
        _nextSequence = unchecked(oldest + 1);
    }

    private uint OldestSequence()
    {
        uint oldest = 0;
        var first = true;
        foreach (var sequence in _packets.Keys)
        {
            if (first || AudioPacket.IsNewer(oldest, sequence))
            {
                oldest = sequence;
                first = false;
            }
        }
        return oldest;
    }
}
