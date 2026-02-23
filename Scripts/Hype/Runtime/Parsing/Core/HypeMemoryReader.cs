using System;
using System.IO;
using System.Text;

namespace HypeReborn.Hype.Runtime.Parsing;

internal sealed class HypeMemoryReader
{
    private readonly HypeRelocatedAddressSpace _space;
    private HypeAddress _position;

    public HypeMemoryReader(HypeRelocatedAddressSpace space, HypeAddress start)
    {
        _space = space;
        _position = start;
    }

    public HypeAddress Position => _position;

    public byte ReadByte()
    {
        var bytes = ReadBytesInternal(1);
        return bytes[0];
    }

    public short ReadInt16()
    {
        var bytes = ReadBytesInternal(2);
        return BitConverter.ToInt16(bytes, 0);
    }

    public ushort ReadUInt16()
    {
        var bytes = ReadBytesInternal(2);
        return BitConverter.ToUInt16(bytes, 0);
    }

    public int ReadInt32()
    {
        var bytes = ReadBytesInternal(4);
        return BitConverter.ToInt32(bytes, 0);
    }

    public uint ReadUInt32()
    {
        var bytes = ReadBytesInternal(4);
        return BitConverter.ToUInt32(bytes, 0);
    }

    public float ReadSingle()
    {
        var bytes = ReadBytesInternal(4);
        return BitConverter.ToSingle(bytes, 0);
    }

    public string ReadFixedString(int length)
    {
        var bytes = ReadBytesInternal(length);
        var zeroIndex = Array.IndexOf(bytes, (byte)0);
        var count = zeroIndex >= 0 ? zeroIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, count);
    }

    public HypeAddress? ReadPointer()
    {
        var fieldAddress = _position;
        var raw = ReadUInt32();
        if (raw == 0 || raw == 0xFFFFFFFF)
        {
            return null;
        }

        if (_space.TryResolvePointer(fieldAddress, raw, out var resolved))
        {
            return resolved;
        }

        if (_space.TryResolveRawAddress(raw, out resolved))
        {
            return resolved;
        }

        return null;
    }

    private byte[] ReadBytesInternal(int length)
    {
        if (!_space.TryGetDataSlice(_position, length, out var span))
        {
            throw new InvalidDataException($"Read out of bounds at {_position} (+{length}).");
        }

        var bytes = span.ToArray();
        _position = _position.Add(length);
        return bytes;
    }
}
