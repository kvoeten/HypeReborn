using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using lzo.net;

namespace HypeReborn.Hype.Runtime.Parsing;

public enum HypeRelocationType
{
    Rtb = 0,
    Rtp = 1,
    Rts = 2,
    Rtt = 3,
    Rtl = 4,
    Rtd = 5,
    Rtg = 6,
    Rtv = 7
}

public sealed class HypeRelocationPointerInfo
{
    public required uint OffsetInMemory { get; init; }
    public required byte Module { get; init; }
    public required byte BlockId { get; init; }
    public required byte Byte6 { get; init; }
    public required byte Byte7 { get; init; }
}

public sealed class HypeRelocationPointerBlock
{
    public required byte Module { get; init; }
    public required byte BlockId { get; init; }
    public required IReadOnlyList<HypeRelocationPointerInfo> Pointers { get; init; }
}

public sealed class HypeRelocationTable
{
    private readonly Dictionary<ushort, HypeRelocationPointerBlock> _lookup;

    public HypeRelocationTable(string path, IReadOnlyList<HypeRelocationPointerBlock> pointerBlocks, bool sawCompressedBlocks)
    {
        Path = path;
        PointerBlocks = pointerBlocks;
        SawCompressedBlocks = sawCompressedBlocks;

        _lookup = new Dictionary<ushort, HypeRelocationPointerBlock>();
        foreach (var block in pointerBlocks)
        {
            _lookup[RelocationKey(block.Module, block.BlockId)] = block;
        }
    }

    public string Path { get; }
    public IReadOnlyList<HypeRelocationPointerBlock> PointerBlocks { get; }
    public bool SawCompressedBlocks { get; }

    public bool TryGetBlock(byte module, byte blockId, out HypeRelocationPointerBlock? block)
    {
        return _lookup.TryGetValue(RelocationKey(module, blockId), out block);
    }

    public static HypeRelocationTable Load(string path, bool snaCompression)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Relocation table path is empty.", nameof(path));
        }

        var bytes = File.ReadAllBytes(path);
        return Parse(path, bytes, snaCompression);
    }

    public static async Task<HypeRelocationTable> LoadAsync(
        string path,
        bool snaCompression,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Relocation table path is empty.", nameof(path));
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Parse(path, bytes, snaCompression);
    }

    private static HypeRelocationTable Parse(string path, byte[] bytes, bool snaCompression)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (!TryReadLayout(reader, path, snaCompression, hasExtraHeaderDword: false, out var parsed))
        {
            stream.Position = 0;
            if (!TryReadLayout(reader, path, snaCompression, hasExtraHeaderDword: true, out parsed))
            {
                throw new InvalidDataException($"Could not parse relocation table '{path}'.");
            }
        }

        return parsed!;
    }

    public static HypeRelocationTable? Merge(params HypeRelocationTable?[] tables)
    {
        if (tables == null || tables.Length == 0)
        {
            return null;
        }

        var orderedKeys = new List<ushort>();
        var blockMeta = new Dictionary<ushort, (byte Module, byte BlockId)>();
        var mergedPointers = new Dictionary<ushort, List<HypeRelocationPointerInfo>>();
        var mergedPathParts = new List<string>();
        var sawCompressedBlocks = false;

        foreach (var table in tables)
        {
            if (table == null)
            {
                continue;
            }

            mergedPathParts.Add(table.Path);
            sawCompressedBlocks |= table.SawCompressedBlocks;

            foreach (var block in table.PointerBlocks)
            {
                var key = RelocationKey(block.Module, block.BlockId);
                if (!mergedPointers.TryGetValue(key, out var pointers))
                {
                    pointers = new List<HypeRelocationPointerInfo>();
                    mergedPointers[key] = pointers;
                    blockMeta[key] = (block.Module, block.BlockId);
                    orderedKeys.Add(key);
                }

                foreach (var pointer in block.Pointers)
                {
                    pointers.Add(new HypeRelocationPointerInfo
                    {
                        OffsetInMemory = pointer.OffsetInMemory,
                        Module = pointer.Module,
                        BlockId = pointer.BlockId,
                        Byte6 = pointer.Byte6,
                        Byte7 = pointer.Byte7
                    });
                }
            }
        }

        if (orderedKeys.Count == 0)
        {
            return null;
        }

        var mergedBlocks = new List<HypeRelocationPointerBlock>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            var meta = blockMeta[key];
            mergedBlocks.Add(new HypeRelocationPointerBlock
            {
                Module = meta.Module,
                BlockId = meta.BlockId,
                Pointers = mergedPointers[key]
            });
        }

        var mergedPath = mergedPathParts.Count == 0 ? "merged" : string.Join("+", mergedPathParts);
        return new HypeRelocationTable(mergedPath, mergedBlocks, sawCompressedBlocks);
    }

    private static bool TryReadLayout(
        BinaryReader reader,
        string path,
        bool snaCompression,
        bool hasExtraHeaderDword,
        out HypeRelocationTable? table)
    {
        table = null;
        try
        {
            var stream = reader.BaseStream;
            stream.Position = 0;
            if (stream.Length < 1)
            {
                return false;
            }

            var count = reader.ReadByte();
            if (hasExtraHeaderDword)
            {
                if (stream.Position + 4 > stream.Length)
                {
                    return false;
                }

                _ = reader.ReadUInt32();
            }

            var sawCompressed = false;
            var blocks = new List<HypeRelocationPointerBlock>(count);

            for (var i = 0; i < count; i++)
            {
                if (stream.Position + 6 > stream.Length)
                {
                    return false;
                }

                var module = reader.ReadByte();
                var blockId = reader.ReadByte();
                var pointerCount = reader.ReadUInt32();
                var pointers = new List<HypeRelocationPointerInfo>((int)Math.Min(pointerCount, int.MaxValue));

                if (pointerCount > 0)
                {
                    if (snaCompression)
                    {
                        if (stream.Position + 20 > stream.Length)
                        {
                            return false;
                        }

                        var isCompressed = reader.ReadUInt32();
                        var compressedSize = reader.ReadUInt32();
                        _ = reader.ReadUInt32(); // compressed checksum
                        var decompressedSize = reader.ReadUInt32();
                        _ = reader.ReadUInt32(); // decompressed checksum

                        if (compressedSize > int.MaxValue || decompressedSize > int.MaxValue)
                        {
                            return false;
                        }

                        if (stream.Position + compressedSize > stream.Length)
                        {
                            return false;
                        }

                        var compressedData = reader.ReadBytes((int)compressedSize);
                        var blockData = isCompressed != 0
                            ? DecompressLzo(compressedData, (int)decompressedSize)
                            : compressedData;
                        sawCompressed |= isCompressed != 0;

                        if (!TryReadPointerInfos(blockData, pointerCount, pointers))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        var bytesRequired = checked(pointerCount * 8u);
                        if (stream.Position + bytesRequired > stream.Length)
                        {
                            return false;
                        }

                        for (var p = 0u; p < pointerCount; p++)
                        {
                            pointers.Add(new HypeRelocationPointerInfo
                            {
                                OffsetInMemory = reader.ReadUInt32(),
                                Module = reader.ReadByte(),
                                BlockId = reader.ReadByte(),
                                Byte6 = reader.ReadByte(),
                                Byte7 = reader.ReadByte()
                            });
                        }
                    }
                }

                blocks.Add(new HypeRelocationPointerBlock
                {
                    Module = module,
                    BlockId = blockId,
                    Pointers = pointers
                });
            }

            table = new HypeRelocationTable(path, blocks, sawCompressed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPointerInfos(
        byte[] data,
        uint expectedCount,
        List<HypeRelocationPointerInfo> destination)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);

        for (var i = 0u; i < expectedCount; i++)
        {
            if (stream.Position + 8 > stream.Length)
            {
                return false;
            }

            destination.Add(new HypeRelocationPointerInfo
            {
                OffsetInMemory = reader.ReadUInt32(),
                Module = reader.ReadByte(),
                BlockId = reader.ReadByte(),
                Byte6 = reader.ReadByte(),
                Byte7 = reader.ReadByte()
            });
        }

        return true;
    }

    private static byte[] DecompressLzo(byte[] compressedData, int decompressedSize)
    {
        using var compressedStream = new MemoryStream(compressedData, writable: false);
        using var lzo = new LzoStream(compressedStream, CompressionMode.Decompress, leaveOpen: false);
        var buffer = new byte[decompressedSize];
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = lzo.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        if (offset == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, offset);
        return buffer;
    }

    public static ushort RelocationKey(byte module, byte blockId)
    {
        return (ushort)((module << 8) | blockId);
    }
}
