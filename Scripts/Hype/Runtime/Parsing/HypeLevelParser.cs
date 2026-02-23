using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HypeReborn.Hype.Runtime.Parsing;

public static class HypeLevelParser
{
    private sealed class FixContext
    {
        public string LevelsRoot { get; init; } = string.Empty;
        public HypeRelocationTable? Rtb { get; init; }
        public HypeRelocationTable? Rtp { get; init; }
        public HypeRelocationTable? Rtt { get; init; }
        public HypeSnaImage? Sna { get; init; }
        public byte[]? Gpt { get; init; }
        public byte[]? Ptx { get; init; }
        public IReadOnlyList<HypeParseDiagnostic> Diagnostics { get; init; } = Array.Empty<HypeParseDiagnostic>();
    }

    public static IReadOnlyList<HypeParsedLevelRecord> ParseLevels(
        string gameRoot,
        IReadOnlyList<HypeLevelRecord> levels)
    {
        var levelsRoot = Path.Combine(gameRoot, "Gamedata", "World", "Levels");
        var fix = LoadFixContext(levelsRoot);

        var parsed = new List<HypeParsedLevelRecord>(levels.Count);
        foreach (var level in levels)
        {
            parsed.Add(ParseLevel(level, fix));
        }

        return parsed;
    }

    private static HypeParsedLevelRecord ParseLevel(HypeLevelRecord level, FixContext fix)
    {
        var diagnostics = new List<HypeParseDiagnostic>(fix.Diagnostics);

        var lvlSnaPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".sna");
        var lvlRtbPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtb");
        var lvlRtpPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtp");
        var lvlRttPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtt");
        var lvlGptPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".gpt");
        var lvlPtxPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".ptx");
        var fixLvlRtbPath = HypeBinaryLoadHelpers.GetNamedCoreFile(level, "fixlvl.rtb");

        if (string.IsNullOrWhiteSpace(lvlSnaPath))
        {
            diagnostics.Add(Error("Level", $"Missing level SNA file for '{level.LevelName}'."));
        }

        if (string.IsNullOrWhiteSpace(lvlRtbPath))
        {
            diagnostics.Add(Error("Level", $"Missing level RTB file for '{level.LevelName}'."));
        }

        if (string.IsNullOrWhiteSpace(lvlRtpPath))
        {
            diagnostics.Add(Warning("Level", $"Missing level RTP pointer file for '{level.LevelName}'. GPT relocation checks disabled."));
        }

        if (string.IsNullOrWhiteSpace(lvlRttPath))
        {
            diagnostics.Add(Warning("Level", $"Missing level RTT pointer file for '{level.LevelName}'. PTX relocation checks disabled."));
        }

        HypeSnaImage? levelSna = null;
        HypeRelocationTable? levelRtb = null;
        HypeRelocationTable? levelRtp = null;
        HypeRelocationTable? levelRtt = null;
        byte[]? levelGpt = null;
        byte[]? levelPtx = null;

        levelSna = TryLoadSna(lvlSnaPath, "LevelSna", diagnostics);
        levelRtb = TryLoadRelocation(lvlRtbPath, "LevelRtb", diagnostics);
        var fixLvlRtb = TryLoadRelocation(fixLvlRtbPath, "FixLvlRtb", diagnostics);
        levelRtp = TryLoadRelocation(lvlRtpPath, "LevelRtp", diagnostics);
        levelRtt = TryLoadRelocation(lvlRttPath, "LevelRtt", diagnostics);
        levelGpt = TryLoadBytes(lvlGptPath, "LevelGpt", diagnostics);
        levelPtx = TryLoadBytes(lvlPtxPath, "LevelPtx", diagnostics);
        var mergedFixRtb = HypeRelocationTable.Merge(fix.Rtb, fixLvlRtb);

        var combinedBlocks = BuildBlockCatalog(fix.Sna, levelSna);

        var resolvedSnaPointers = 0;
        var unresolvedSnaPointers = 0;
        var resolvedGptPointers = 0;
        var unresolvedGptPointers = 0;
        var resolvedPtxPointers = 0;
        var unresolvedPtxPointers = 0;

        ResolveBlockPointers(
            mergedFixRtb,
            fix.Sna,
            combinedBlocks,
            "FixRtb",
            diagnostics,
            ref resolvedSnaPointers,
            ref unresolvedSnaPointers);

        ResolveBlockPointers(
            levelRtb,
            levelSna,
            combinedBlocks,
            "LevelRtb",
            diagnostics,
            ref resolvedSnaPointers,
            ref unresolvedSnaPointers);

        ResolvePointerFile(
            fix.Gpt,
            fix.Rtp,
            combinedBlocks,
            "FixGpt",
            diagnostics,
            ref resolvedGptPointers,
            ref unresolvedGptPointers);

        ResolvePointerFile(
            levelGpt,
            levelRtp,
            combinedBlocks,
            "LevelGpt",
            diagnostics,
            ref resolvedGptPointers,
            ref unresolvedGptPointers);

        ResolvePointerFile(
            fix.Ptx,
            fix.Rtt,
            combinedBlocks,
            "FixPtx",
            diagnostics,
            ref resolvedPtxPointers,
            ref unresolvedPtxPointers);

        ResolvePointerFile(
            levelPtx,
            levelRtt,
            combinedBlocks,
            "LevelPtx",
            diagnostics,
            ref resolvedPtxPointers,
            ref unresolvedPtxPointers);

        var succeeded = diagnostics.All(x => x.Severity != HypeParseSeverity.Error);

        return new HypeParsedLevelRecord
        {
            LevelName = level.LevelName,
            Succeeded = succeeded,
            FixSnaBlockCount = fix.Sna?.Blocks.Count ?? 0,
            LevelSnaBlockCount = levelSna?.Blocks.Count ?? 0,
            FixRelocationBlockCount = mergedFixRtb?.PointerBlocks.Count ?? 0,
            LevelRelocationBlockCount = levelRtb?.PointerBlocks.Count ?? 0,
            ResolvedSnaPointers = resolvedSnaPointers,
            UnresolvedSnaPointers = unresolvedSnaPointers,
            ResolvedGptPointers = resolvedGptPointers,
            UnresolvedGptPointers = unresolvedGptPointers,
            ResolvedPtxPointers = resolvedPtxPointers,
            UnresolvedPtxPointers = unresolvedPtxPointers,
            Diagnostics = diagnostics
        };
    }

    private static FixContext LoadFixContext(string levelsRoot)
    {
        var diagnostics = new List<HypeParseDiagnostic>();

        var fixSnaPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.sna");
        var fixRtbPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtb");
        var fixRtpPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtp");
        var fixRttPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtt");
        var fixGptPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.gpt");
        var fixPtxPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.ptx");

        if (string.IsNullOrWhiteSpace(fixSnaPath))
        {
            diagnostics.Add(Error("Fix", "Missing fix.sna in Gamedata/World/Levels."));
        }

        if (string.IsNullOrWhiteSpace(fixRtbPath))
        {
            diagnostics.Add(Error("Fix", "Missing fix.rtb in Gamedata/World/Levels."));
        }

        var fixSna = TryLoadSna(fixSnaPath, "FixSna", diagnostics);
        var fixRtb = TryLoadRelocation(fixRtbPath, "FixRtb", diagnostics);
        var fixRtp = TryLoadRelocation(fixRtpPath, "FixRtp", diagnostics);
        var fixRtt = TryLoadRelocation(fixRttPath, "FixRtt", diagnostics);
        var fixGpt = TryLoadBytes(fixGptPath, "FixGpt", diagnostics);
        var fixPtx = TryLoadBytes(fixPtxPath, "FixPtx", diagnostics);

        return new FixContext
        {
            LevelsRoot = levelsRoot,
            Rtb = fixRtb,
            Rtp = fixRtp,
            Rtt = fixRtt,
            Sna = fixSna,
            Gpt = fixGpt,
            Ptx = fixPtx,
            Diagnostics = diagnostics
        };
    }

    private static Dictionary<ushort, HypeSnaBlock> BuildBlockCatalog(HypeSnaImage? fixSna, HypeSnaImage? levelSna)
    {
        var result = new Dictionary<ushort, HypeSnaBlock>();

        if (fixSna != null)
        {
            foreach (var block in fixSna.Blocks)
            {
                result[HypeRelocationTable.RelocationKey(block.Module, block.BlockId)] = block;
            }
        }

        if (levelSna != null)
        {
            foreach (var block in levelSna.Blocks)
            {
                result[HypeRelocationTable.RelocationKey(block.Module, block.BlockId)] = block;
            }
        }

        return result;
    }

    private static void ResolveBlockPointers(
        HypeRelocationTable? table,
        HypeSnaImage? sourceSna,
        IReadOnlyDictionary<ushort, HypeSnaBlock> combinedBlocks,
        string phase,
        List<HypeParseDiagnostic> diagnostics,
        ref int resolved,
        ref int unresolved)
    {
        if (table == null || sourceSna == null)
        {
            return;
        }

        foreach (var pointerBlock in table.PointerBlocks)
        {
            if (!sourceSna.TryGetBlock(pointerBlock.Module, pointerBlock.BlockId, out var sourceBlock) || sourceBlock == null)
            {
                unresolved += pointerBlock.Pointers.Count;
                diagnostics.Add(Warning(phase, $"Source block ({pointerBlock.Module},{pointerBlock.BlockId}) was not found in SNA."));
                continue;
            }

            foreach (var pointer in pointerBlock.Pointers)
            {
                var relativeAddress = (long)pointer.OffsetInMemory - sourceBlock.BaseInMemory;
                if (relativeAddress < 0 || relativeAddress + 4 > sourceBlock.Data.Length)
                {
                    unresolved++;
                    continue;
                }

                var ptrValue = BitConverter.ToUInt32(sourceBlock.Data, (int)relativeAddress);
                var targetKey = HypeRelocationTable.RelocationKey(pointer.Module, pointer.BlockId);
                if (!combinedBlocks.TryGetValue(targetKey, out var targetBlock))
                {
                    unresolved++;
                    continue;
                }

                var targetRelative = (long)ptrValue - targetBlock.BaseInMemory;
                if (targetRelative < 0 || targetRelative >= targetBlock.Data.Length)
                {
                    unresolved++;
                    continue;
                }

                resolved++;
            }
        }
    }

    private static void ResolvePointerFile(
        byte[]? pointerFileBytes,
        HypeRelocationTable? pointerTable,
        IReadOnlyDictionary<ushort, HypeSnaBlock> combinedBlocks,
        string phase,
        List<HypeParseDiagnostic> diagnostics,
        ref int resolved,
        ref int unresolved)
    {
        if (pointerFileBytes == null || pointerTable == null)
        {
            return;
        }

        if (pointerFileBytes.Length < 4)
        {
            diagnostics.Add(Warning(phase, "Pointer file is too small."));
            return;
        }

        var offsetLookup = new Dictionary<uint, HypeRelocationPointerInfo>();
        foreach (var block in pointerTable.PointerBlocks)
        {
            foreach (var pointer in block.Pointers)
            {
                if (!offsetLookup.ContainsKey(pointer.OffsetInMemory))
                {
                    offsetLookup[pointer.OffsetInMemory] = pointer;
                }
            }
        }

        var matched = 0;
        for (var offset = 0; offset <= pointerFileBytes.Length - 4; offset += 4)
        {
            var value = BitConverter.ToUInt32(pointerFileBytes, offset);
            if (!offsetLookup.TryGetValue(value, out var pointer))
            {
                continue;
            }

            matched++;

            var targetKey = HypeRelocationTable.RelocationKey(pointer.Module, pointer.BlockId);
            if (!combinedBlocks.TryGetValue(targetKey, out var targetBlock))
            {
                unresolved++;
                continue;
            }

            var targetRelative = (long)value - targetBlock.BaseInMemory;
            if (targetRelative < 0 || targetRelative >= targetBlock.Data.Length)
            {
                unresolved++;
                continue;
            }

            resolved++;
        }

        if (matched == 0)
        {
            diagnostics.Add(Warning(phase, "No relocation entries matched pointer file values."));
        }
    }

    private static HypeRelocationTable? TryLoadRelocation(
        string? path,
        string phase,
        List<HypeParseDiagnostic> diagnostics)
    {
        return HypeBinaryLoadHelpers.TryLoadRtb(
            path,
            string.IsNullOrWhiteSpace(path) ? phase : Path.GetFileName(path),
            message => diagnostics.Add(Error(phase, message)));
    }

    private static HypeSnaImage? TryLoadSna(
        string? path,
        string phase,
        List<HypeParseDiagnostic> diagnostics)
    {
        return HypeBinaryLoadHelpers.TryLoadSna(
            path,
            string.IsNullOrWhiteSpace(path) ? phase : Path.GetFileName(path),
            message => diagnostics.Add(Error(phase, message)));
    }

    private static byte[]? TryLoadBytes(
        string? path,
        string phase,
        List<HypeParseDiagnostic> diagnostics)
    {
        return HypeBinaryLoadHelpers.TryLoadBytes(
            path,
            string.IsNullOrWhiteSpace(path) ? phase : Path.GetFileName(path),
            message => diagnostics.Add(Error(phase, message)));
    }

    private static HypeParseDiagnostic Error(string phase, string message)
    {
        return new HypeParseDiagnostic
        {
            Severity = HypeParseSeverity.Error,
            Phase = phase,
            Message = message
        };
    }

    private static HypeParseDiagnostic Warning(string phase, string message)
    {
        return new HypeParseDiagnostic
        {
            Severity = HypeParseSeverity.Warning,
            Phase = phase,
            Message = message
        };
    }
}
