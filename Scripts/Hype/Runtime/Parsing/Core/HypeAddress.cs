using System;

namespace HypeReborn.Hype.Runtime.Parsing;

internal readonly struct HypeAddress : IEquatable<HypeAddress>
{
    public HypeAddress(string segmentId, int offset)
    {
        SegmentId = segmentId;
        Offset = offset;
    }

    public string SegmentId { get; }
    public int Offset { get; }

    public HypeAddress Add(int delta)
    {
        return new HypeAddress(SegmentId, Offset + delta);
    }

    public bool Equals(HypeAddress other)
    {
        return Offset == other.Offset &&
               SegmentId.Equals(other.SegmentId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is HypeAddress other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SegmentId, Offset);
    }

    public override string ToString()
    {
        return $"{SegmentId}@0x{Offset:X8}";
    }
}
