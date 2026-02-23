using System.Globalization;
using System.IO;
using HypeReborn.Hype.Runtime.Parsing;

namespace HypeReborn.ParsingCore.Tests;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        Run(nameof(MemoryReader_PrimitiveReads_Work), MemoryReader_PrimitiveReads_Work);
        Run(nameof(MemoryReader_OutOfBounds_Throws), MemoryReader_OutOfBounds_Throws);
        Run(nameof(MemoryReader_PointerSentinels_ReturnNull), MemoryReader_PointerSentinels_ReturnNull);
        Run(nameof(RelocatedAddressSpace_RawAddressResolution_Works), RelocatedAddressSpace_RawAddressResolution_Works);
        Run(nameof(RelocatedAddressSpace_RelocatedPointerResolution_Works), RelocatedAddressSpace_RelocatedPointerResolution_Works);

        Console.WriteLine($"PASS {_passed} tests");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void MemoryReader_PrimitiveReads_Work()
    {
        var space = new HypeRelocatedAddressSpace();
        var bytes = new byte[]
        {
            0x7F,             // byte
            0x34, 0x12,       // int16 = 0x1234
            0x78, 0x56,       // uint16 = 0x5678
            0xEF, 0xCD, 0xAB, 0x90 // int32 = unchecked((int)0x90ABCDEF)
        };

        space.AddPointerFile("test", bytes, null);
        Ensure(space.TryGetSegmentStart("test", out var start), "segment start missing");

        var reader = space.CreateReader(start);
        Ensure(reader.ReadByte() == 0x7F, "byte mismatch");
        Ensure(reader.ReadInt16() == 0x1234, "int16 mismatch");
        Ensure(reader.ReadUInt16() == 0x5678, "uint16 mismatch");
        Ensure(reader.ReadInt32() == unchecked((int)0x90ABCDEF), "int32 mismatch");
    }

    private static void MemoryReader_OutOfBounds_Throws()
    {
        var space = new HypeRelocatedAddressSpace();
        space.AddPointerFile("bounds", new byte[] { 0x01, 0x02 }, null);
        Ensure(space.TryGetSegmentStart("bounds", out var start), "segment start missing");

        var reader = space.CreateReader(start);
        _ = reader.ReadByte();
        _ = reader.ReadByte();

        Throws<InvalidDataException>(() => reader.ReadByte());
    }

    private static void MemoryReader_PointerSentinels_ReturnNull()
    {
        var bytes = new byte[]
        {
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0xFF, 0xFF
        };

        var space = new HypeRelocatedAddressSpace();
        space.AddPointerFile("ptrs", bytes, null);
        Ensure(space.TryGetSegmentStart("ptrs", out var start), "segment start missing");

        var reader = space.CreateReader(start);
        Ensure(reader.ReadPointer() == null, "zero pointer should be null");
        Ensure(reader.ReadPointer() == null, "0xFFFFFFFF pointer should be null");
    }

    private static void RelocatedAddressSpace_RawAddressResolution_Works()
    {
        var targetData = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var sna = new HypeSnaImage(
            "test",
            new[]
            {
                new HypeSnaBlock
                {
                    Module = 0x01,
                    BlockId = 0x02,
                    BaseInMemory = 0x1000,
                    Size = (uint)targetData.Length,
                    Data = targetData
                }
            },
            sawCompressedBlocks: false);

        var space = new HypeRelocatedAddressSpace();
        space.AddSnaBlocks(sna, null, "raw");

        Ensure(space.TryResolveRawAddress(0x1002, out var resolved), "raw resolve failed");
        Ensure(resolved.Offset == 2, "raw offset mismatch");
        Ensure(space.TryGetDataSlice(resolved, 1, out var span), "slice missing");
        Ensure(span[0] == 0x30, "slice value mismatch");
        Ensure(!space.TryGetDataSlice(resolved, 99, out _), "expected out-of-bounds slice failure");
    }

    private static void RelocatedAddressSpace_RelocatedPointerResolution_Works()
    {
        var sourceData = new byte[] { 0x02, 0x10, 0x00, 0x00 };
        var targetData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        var sna = new HypeSnaImage(
            "test",
            new[]
            {
                new HypeSnaBlock
                {
                    Module = 0x01,
                    BlockId = 0x01,
                    BaseInMemory = 0x2000,
                    Size = (uint)sourceData.Length,
                    Data = sourceData
                },
                new HypeSnaBlock
                {
                    Module = 0x01,
                    BlockId = 0x02,
                    BaseInMemory = 0x1000,
                    Size = (uint)targetData.Length,
                    Data = targetData
                }
            },
            sawCompressedBlocks: false);

        var relocation = new HypeRelocationTable(
            "test.rtb",
            new[]
            {
                new HypeRelocationPointerBlock
                {
                    Module = 0x01,
                    BlockId = 0x01,
                    Pointers = new[]
                    {
                        new HypeRelocationPointerInfo
                        {
                            OffsetInMemory = 0x2000,
                            Module = 0x01,
                            BlockId = 0x02,
                            Byte6 = 0,
                            Byte7 = 0
                        }
                    }
                }
            },
            sawCompressedBlocks: false);

        var space = new HypeRelocatedAddressSpace();
        space.AddSnaBlocks(sna, relocation, "reloc");

        Ensure(space.TryResolveRawAddress(0x2000, out var sourceAddress), "source block resolve failed");
        var reader = space.CreateReader(sourceAddress);
        var pointer = reader.ReadPointer();
        Ensure(pointer != null, "relocated pointer was not resolved");
        var resolvedPointer = pointer!.Value;
        Ensure(resolvedPointer.Offset == 2, "resolved pointer offset mismatch");
        Ensure(space.TryGetDataSlice(resolvedPointer, 1, out var targetByte), "target slice missing");
        Ensure(targetByte[0] == 0xCC, string.Create(
            CultureInfo.InvariantCulture,
            $"target value mismatch: expected 0xCC got 0x{targetByte[0]:X2}"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected exception: {typeof(TException).Name}");
    }
}
