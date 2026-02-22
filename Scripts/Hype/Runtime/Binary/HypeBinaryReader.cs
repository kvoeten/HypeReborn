using System;
using System.IO;

namespace HypeReborn.Hype.Runtime.Binary;

public sealed class HypeBinaryReader : IDisposable
{
    private readonly BinaryReader _reader;

    public HypeBinaryReader(Stream stream)
    {
        _reader = new BinaryReader(stream);
    }

    public long Position => _reader.BaseStream.Position;
    public long Length => _reader.BaseStream.Length;

    public byte ReadByte() => _reader.ReadByte();
    public ushort ReadUInt16() => _reader.ReadUInt16();
    public uint ReadUInt32() => _reader.ReadUInt32();
    public int ReadInt32() => _reader.ReadInt32();

    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

    public void Seek(long position)
    {
        _reader.BaseStream.Seek(position, SeekOrigin.Begin);
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
