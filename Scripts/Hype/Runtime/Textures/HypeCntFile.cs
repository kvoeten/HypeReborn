using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HypeReborn.Hype.Runtime.Binary;
using HypeReborn.Hype.Runtime;

namespace HypeReborn.Hype.Runtime.Textures;

public sealed class HypeCntFile : IDisposable
{
    public sealed class Entry
    {
        public required string Directory { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public required string TgaName { get; init; }
        public required int DataPointer { get; init; }
        public required int Size { get; init; }
        public required byte[] XorKey { get; init; }
        public required uint Checksum { get; init; }
    }

    private readonly string _path;
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, List<Entry>> _entriesByTgaName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Entry>> _entriesByFullName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Entry>> _entriesByTgaFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry?> _uniqueByTgaSuffixCache = new(StringComparer.OrdinalIgnoreCase);

    public string Path => _path;
    public IReadOnlyList<Entry> Entries => _entries;

    public HypeCntFile(string path)
    {
        _path = path;
        Parse();
    }

    public Entry? FindByTgaName(string tgaName)
    {
        var normalized = HypePathUtils.NormalizePathSeparators(tgaName).TrimStart('\\');
        return _entriesByTgaName.TryGetValue(normalized, out var matches) && matches.Count > 0
            ? matches[0]
            : null;
    }

    public Entry? FindByFullName(string fullName)
    {
        var normalized = HypePathUtils.NormalizePathSeparators(fullName).TrimStart('\\');
        return _entriesByFullName.TryGetValue(normalized, out var matches) && matches.Count > 0
            ? matches[0]
            : null;
    }

    public Entry? FindUniqueByTgaFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalized = System.IO.Path.GetFileName(HypePathUtils.NormalizePathSeparators(fileName));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return _entriesByTgaFileName.TryGetValue(normalized, out var matches) && matches.Count == 1
            ? matches[0]
            : null;
    }

    public Entry? FindUniqueByTgaSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var normalizedSuffix = HypePathUtils.NormalizePathSeparators(suffix).TrimStart('\\');
        if (_uniqueByTgaSuffixCache.TryGetValue(normalizedSuffix, out var cached))
        {
            return cached;
        }

        Entry? found = null;
        foreach (var entry in _entries)
        {
            if (!entry.TgaName.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found != null)
            {
                _uniqueByTgaSuffixCache[normalizedSuffix] = null;
                return null;
            }

            found = entry;
        }

        _uniqueByTgaSuffixCache[normalizedSuffix] = found;
        return found;
    }

    public byte[] ReadEntryBytes(Entry entry)
    {
        using var stream = File.OpenRead(_path);
        using var reader = new HypeBinaryReader(stream);

        reader.Seek(entry.DataPointer);
        var data = reader.ReadBytes(entry.Size);

        for (var i = 0; i < data.Length; i++)
        {
            if ((entry.Size % 4) + i < entry.Size)
            {
                data[i] = (byte)(data[i] ^ entry.XorKey[i % 4]);
            }
        }

        return data;
    }

    private void Parse()
    {
        using var stream = File.OpenRead(_path);
        using var reader = new HypeBinaryReader(stream);

        var directoryCount = reader.ReadInt32();
        var fileCount = reader.ReadInt32();
        var isXor = reader.ReadByte();
        var isChecksum = reader.ReadByte();
        var xorKey = reader.ReadByte();

        var directories = new string[directoryCount];
        var checksum = 0;

        for (var i = 0; i < directoryCount; i++)
        {
            var strLen = reader.ReadInt32();
            var data = reader.ReadBytes(strLen);
            Decode(data, isXor != 0, xorKey, ref checksum, isChecksum != 0);
            directories[i] = Encoding.ASCII.GetString(data);
        }

        _ = reader.ReadByte(); // Directory checksum byte

        for (var i = 0; i < fileCount; i++)
        {
            var dirIndex = reader.ReadInt32();
            var strLen = reader.ReadInt32();
            var fileNameBytes = reader.ReadBytes(strLen);
            Decode(fileNameBytes, isXor != 0, xorKey, ref checksum, false);
            var fileName = Encoding.ASCII.GetString(fileNameBytes);

            var fileXorKey = reader.ReadBytes(4);
            var fileChecksum = reader.ReadUInt32();
            var dataPointer = reader.ReadInt32();
            var fileSize = reader.ReadInt32();

            var directory = dirIndex >= 0 && dirIndex < directories.Length ? directories[dirIndex] : string.Empty;
            var fullName = string.IsNullOrWhiteSpace(directory)
                ? fileName
                : $"{directory}\\{fileName}";

            _entries.Add(new Entry
            {
                Directory = directory,
                Name = fileName,
                FullName = HypePathUtils.NormalizePathSeparators(fullName),
                TgaName = HypePathUtils.ChangeGfExtension(HypePathUtils.NormalizePathSeparators(fullName)),
                DataPointer = dataPointer,
                Size = fileSize,
                XorKey = fileXorKey,
                Checksum = fileChecksum
            });

            IndexEntry(_entries[^1]);
        }
    }

    private void IndexEntry(Entry entry)
    {
        static void Add(Dictionary<string, List<Entry>> map, string key, Entry value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<Entry>();
                map[key] = list;
            }

            list.Add(value);
        }

        Add(_entriesByTgaName, entry.TgaName, entry);
        Add(_entriesByFullName, entry.FullName, entry);

        var tgaFile = System.IO.Path.GetFileName(entry.TgaName);
        if (!string.IsNullOrWhiteSpace(tgaFile))
        {
            Add(_entriesByTgaFileName, tgaFile, entry);
        }
    }

    private static void Decode(byte[] data, bool useXor, byte xorKey, ref int checksum, bool updateChecksum)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (useXor)
            {
                data[i] = (byte)(data[i] ^ xorKey);
            }

            if (updateChecksum)
            {
                checksum = (checksum + data[i]) % 256;
            }
        }
    }

    public void Dispose()
    {
        // Nothing to dispose currently; kept for API symmetry.
    }
}
