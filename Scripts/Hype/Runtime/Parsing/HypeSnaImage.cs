using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using lzo.net;

namespace HypeReborn.Hype.Runtime.Parsing;

public sealed class HypeSnaBlock
{
    public required byte Module { get; init; }
    public required byte BlockId { get; init; }
    public required int BaseInMemory { get; init; }
    public required uint Size { get; init; }
    public required byte[] Data { get; init; }
}

public sealed class HypeSnaImage
{
    private readonly Dictionary<ushort, HypeSnaBlock> _lookup;

    public HypeSnaImage(string path, IReadOnlyList<HypeSnaBlock> blocks, bool sawCompressedBlocks)
    {
        Path = path;
        Blocks = blocks;
        SawCompressedBlocks = sawCompressedBlocks;

        _lookup = new Dictionary<ushort, HypeSnaBlock>();
        foreach (var block in blocks)
        {
            _lookup[HypeRelocationTable.RelocationKey(block.Module, block.BlockId)] = block;
        }
    }

    public string Path { get; }
    public IReadOnlyList<HypeSnaBlock> Blocks { get; }
    public bool SawCompressedBlocks { get; }

    public bool TryGetBlock(byte module, byte blockId, out HypeSnaBlock? block)
    {
        return _lookup.TryGetValue(HypeRelocationTable.RelocationKey(module, blockId), out block);
    }

    public static HypeSnaImage Load(string path, bool snaCompression)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SNA path is empty.", nameof(path));
        }

        var bytes = File.ReadAllBytes(path);
        return Parse(path, bytes, snaCompression);
    }

    public static async Task<HypeSnaImage> LoadAsync(string path, bool snaCompression, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SNA path is empty.", nameof(path));
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Parse(path, bytes, snaCompression);
    }

    private static HypeSnaImage Parse(string path, byte[] bytes, bool snaCompression)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        var blocks = new List<HypeSnaBlock>();
        var sawCompressedBlocks = false;

        while (stream.Position < stream.Length)
        {
            if (stream.Position + 6 > stream.Length)
            {
                break;
            }

            var module = reader.ReadByte();
            var blockId = reader.ReadByte();
            var baseInMemory = reader.ReadInt32();

            if (baseInMemory == -1)
            {
                continue;
            }

            if (stream.Position + 16 > stream.Length)
            {
                throw new InvalidDataException($"Invalid SNA block header in '{path}'.");
            }

            _ = reader.ReadUInt32(); // unk2
            _ = reader.ReadUInt32(); // unk3
            _ = reader.ReadUInt32(); // maxPosMinus9
            var size = reader.ReadUInt32();

            if (size > int.MaxValue)
            {
                throw new InvalidDataException($"SNA block too large in '{path}'.");
            }

            byte[] blockData;
            if (size == 0)
            {
                blockData = Array.Empty<byte>();
            }
            else if (snaCompression)
            {
                if (stream.Position + 20 > stream.Length)
                {
                    throw new InvalidDataException($"Invalid compressed SNA block in '{path}'.");
                }

                var isCompressed = reader.ReadUInt32();
                var compressedSize = reader.ReadUInt32();
                _ = reader.ReadUInt32(); // compressed checksum
                var decompressedSize = reader.ReadUInt32();
                _ = reader.ReadUInt32(); // decompressed checksum

                if (compressedSize > int.MaxValue || decompressedSize > int.MaxValue)
                {
                    throw new InvalidDataException($"Invalid compressed SNA size in '{path}'.");
                }

                if (stream.Position + compressedSize > stream.Length)
                {
                    throw new InvalidDataException($"SNA compressed payload exceeds file length in '{path}'.");
                }

                var payload = reader.ReadBytes((int)compressedSize);
                blockData = isCompressed != 0
                    ? DecompressLzo(payload, (int)decompressedSize, (int)size)
                    : ReadSizedPayload(payload, (int)size);
                sawCompressedBlocks |= isCompressed != 0;
            }
            else
            {
                if (stream.Position + size > stream.Length)
                {
                    throw new InvalidDataException($"SNA block exceeds file length in '{path}'.");
                }

                blockData = reader.ReadBytes((int)size);
            }

            if (blockData.Length < size)
            {
                throw new InvalidDataException($"SNA block data is truncated in '{path}'.");
            }

            if (blockData.Length > size)
            {
                Array.Resize(ref blockData, (int)size);
            }

            blocks.Add(new HypeSnaBlock
            {
                Module = module,
                BlockId = blockId,
                BaseInMemory = baseInMemory,
                Size = size,
                Data = blockData
            });
        }

        return new HypeSnaImage(path, blocks, sawCompressedBlocks);
    }

    private static byte[] DecompressLzo(byte[] compressed, int decompressedSize, int blockSize)
    {
        using var compressedStream = new MemoryStream(compressed, writable: false);
        using var lzo = new LzoStream(compressedStream, CompressionMode.Decompress, leaveOpen: false);
        lzo.SetLength(decompressedSize);
        var buffer = new byte[blockSize];
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

        Array.Resize(ref buffer, offset);
        return buffer;
    }

    private static byte[] ReadSizedPayload(byte[] payload, int blockSize)
    {
        if (payload.Length == blockSize)
        {
            return payload;
        }

        if (payload.Length < blockSize)
        {
            return payload;
        }

        var data = new byte[blockSize];
        Buffer.BlockCopy(payload, 0, data, 0, blockSize);
        return data;
    }
}
