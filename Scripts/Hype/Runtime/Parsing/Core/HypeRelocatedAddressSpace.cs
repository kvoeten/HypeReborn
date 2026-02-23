using System;
using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime.Parsing;

internal sealed class HypeRelocatedAddressSpace
{
    private readonly Dictionary<string, HypeSegment> _segments = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, List<HypeSegment>> _blockSegments = new();
    private readonly List<HypeSegment> _blockSegmentList = new();

    public void AddSnaBlocks(HypeSnaImage? sna, HypeRelocationTable? rtb, string sourceTag)
    {
        if (sna == null)
        {
            return;
        }

        var blockIndex = 0;
        foreach (var block in sna.Blocks)
        {
            var pointerMap = BuildBlockPointerMap(rtb, block.Module, block.BlockId);
            var segmentId = BuildBlockSegmentId(sourceTag, block.Module, block.BlockId, blockIndex++);
            var segment = new HypeSegment(
                segmentId,
                block.Data,
                block.BaseInMemory,
                pointerMap,
                pointerFilePointerMap: null);

            _segments[segmentId] = segment;
            _blockSegmentList.Add(segment);

            var key = HypeRelocationTable.RelocationKey(block.Module, block.BlockId);
            if (!_blockSegments.TryGetValue(key, out var bucket))
            {
                bucket = new List<HypeSegment>();
                _blockSegments[key] = bucket;
            }

            bucket.Add(segment);
        }
    }

    public void AddPointerFile(string segmentId, byte[]? bytes, HypeRelocationTable? table)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        var pointerFileMap = BuildPointerFileMap(bytes, table);
        _segments[segmentId] = new HypeSegment(
            segmentId,
            bytes,
            baseInMemory: null,
            blockPointerMap: null,
            pointerFilePointerMap: pointerFileMap);
    }

    public bool TryGetSegmentStart(string segmentId, out HypeAddress address)
    {
        if (_segments.ContainsKey(segmentId))
        {
            address = new HypeAddress(segmentId, 0);
            return true;
        }

        address = default;
        return false;
    }

    public HypeMemoryReader CreateReader(HypeAddress address)
    {
        return new HypeMemoryReader(this, address);
    }

    public bool TryResolvePointer(HypeAddress pointerFieldAddress, uint rawValue, out HypeAddress resolved)
    {
        resolved = default;
        if (!_segments.TryGetValue(pointerFieldAddress.SegmentId, out var sourceSegment))
        {
            return false;
        }

        if (sourceSegment.BlockPointerMap != null && sourceSegment.BaseInMemory.HasValue)
        {
            var memoryAddress = (uint)(sourceSegment.BaseInMemory.Value + pointerFieldAddress.Offset);
            if (sourceSegment.BlockPointerMap.TryGetValue(memoryAddress, out var pointerInfo))
            {
                return TryResolvePointerTarget(pointerInfo, rawValue, out resolved);
            }
        }

        if (sourceSegment.PointerFilePointerMap != null &&
            sourceSegment.PointerFilePointerMap.TryGetValue(pointerFieldAddress.Offset, out var pointerFromFile))
        {
            return TryResolvePointerTarget(pointerFromFile, rawValue, out resolved);
        }

        return false;
    }

    public bool TryResolveRawAddress(uint rawValue, out HypeAddress resolved)
    {
        foreach (var segment in _blockSegmentList)
        {
            if (!segment.BaseInMemory.HasValue)
            {
                continue;
            }

            var baseAddress = segment.BaseInMemory.Value;
            var relative = (long)rawValue - baseAddress;
            if (relative < 0 || relative >= segment.Data.Length)
            {
                continue;
            }

            resolved = new HypeAddress(segment.Id, (int)relative);
            return true;
        }

        resolved = default;
        return false;
    }

    public bool TryGetDataSlice(HypeAddress address, int length, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        if (!_segments.TryGetValue(address.SegmentId, out var segment))
        {
            return false;
        }

        if (address.Offset < 0 || address.Offset + length > segment.Data.Length)
        {
            return false;
        }

        bytes = new ReadOnlySpan<byte>(segment.Data, address.Offset, length);
        return true;
    }

    private bool TryResolvePointerTarget(
        HypeRelocationPointerInfo pointerInfo,
        uint rawValue,
        out HypeAddress resolved)
    {
        resolved = default;
        var key = HypeRelocationTable.RelocationKey(pointerInfo.Module, pointerInfo.BlockId);
        if (!_blockSegments.TryGetValue(key, out var candidates) || candidates.Count == 0)
        {
            return false;
        }

        HypeSegment? bestCandidate = null;
        long bestRelative = -1;
        var bestLength = -1;

        foreach (var candidate in candidates)
        {
            if (!candidate.BaseInMemory.HasValue)
            {
                continue;
            }

            var relative = (long)rawValue - candidate.BaseInMemory.Value;
            if (relative < 0 || relative >= candidate.Data.Length)
            {
                continue;
            }

            if (candidate.Data.Length > bestLength)
            {
                bestCandidate = candidate;
                bestRelative = relative;
                bestLength = candidate.Data.Length;
            }
        }

        if (bestCandidate == null || bestRelative < 0)
        {
            return false;
        }

        resolved = new HypeAddress(bestCandidate.Id, (int)bestRelative);
        return true;
    }

    private static Dictionary<uint, HypeRelocationPointerInfo>? BuildBlockPointerMap(
        HypeRelocationTable? table,
        byte module,
        byte blockId)
    {
        if (table == null || !table.TryGetBlock(module, blockId, out var block) || block == null || block.Pointers.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<uint, HypeRelocationPointerInfo>();
        foreach (var pointer in block.Pointers)
        {
            if (!map.ContainsKey(pointer.OffsetInMemory))
            {
                map[pointer.OffsetInMemory] = pointer;
            }
        }

        return map;
    }

    private static Dictionary<int, HypeRelocationPointerInfo>? BuildPointerFileMap(
        byte[] data,
        HypeRelocationTable? table)
    {
        if (table == null || data.Length < 4)
        {
            return null;
        }

        var lookupByOffsetInMemory = new Dictionary<uint, HypeRelocationPointerInfo>();
        foreach (var block in table.PointerBlocks)
        {
            foreach (var pointer in block.Pointers)
            {
                if (!lookupByOffsetInMemory.ContainsKey(pointer.OffsetInMemory))
                {
                    lookupByOffsetInMemory[pointer.OffsetInMemory] = pointer;
                }
            }
        }

        if (lookupByOffsetInMemory.Count == 0)
        {
            return null;
        }

        var map = new Dictionary<int, HypeRelocationPointerInfo>();
        for (var offset = 0; offset <= data.Length - 4; offset += 4)
        {
            var value = BitConverter.ToUInt32(data, offset);
            if (lookupByOffsetInMemory.TryGetValue(value, out var pointerInfo))
            {
                map[offset] = pointerInfo;
            }
        }

        return map.Count > 0 ? map : null;
    }

    private static string BuildBlockSegmentId(string sourceTag, byte module, byte blockId, int index)
    {
        return $"{sourceTag}:block:{module:X2}:{blockId:X2}:{index:X4}";
    }

    private sealed class HypeSegment
    {
        public HypeSegment(
            string id,
            byte[] data,
            int? baseInMemory,
            Dictionary<uint, HypeRelocationPointerInfo>? blockPointerMap,
            Dictionary<int, HypeRelocationPointerInfo>? pointerFilePointerMap)
        {
            Id = id;
            Data = data;
            BaseInMemory = baseInMemory;
            BlockPointerMap = blockPointerMap;
            PointerFilePointerMap = pointerFilePointerMap;
        }

        public string Id { get; }
        public byte[] Data { get; }
        public int? BaseInMemory { get; }
        public Dictionary<uint, HypeRelocationPointerInfo>? BlockPointerMap { get; }
        public Dictionary<int, HypeRelocationPointerInfo>? PointerFilePointerMap { get; }
    }
}
