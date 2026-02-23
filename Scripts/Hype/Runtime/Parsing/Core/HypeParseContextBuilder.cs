using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HypeReborn.Hype.Runtime.Parsing;

internal static class HypeParseContextBuilder
{
    public static bool TryBuild(
        HypeLevelRecord level,
        out HypeParseContext context,
        List<string> diagnostics)
    {
        context = default;
        var levelsRoot = Directory.GetParent(level.LevelDirectoryPath)?.FullName;
        if (string.IsNullOrWhiteSpace(levelsRoot))
        {
            diagnostics.Add("Could not resolve levels root from level directory.");
            return false;
        }

        var fixSnaPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.sna");
        var fixRtbPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtb");
        var fixRtpPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtp");
        var fixRttPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtt");
        var fixGptPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.gpt");
        var fixPtxPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.ptx");

        var lvlSnaPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".sna");
        var lvlRtbPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtb");
        var lvlRtpPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtp");
        var lvlRttPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtt");
        var lvlGptPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".gpt");
        var lvlPtxPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".ptx");
        var fixLvlRtbPath = HypeBinaryLoadHelpers.GetNamedCoreFile(level, "fixlvl.rtb");

        var fixSna = HypeBinaryLoadHelpers.TryLoadSna(fixSnaPath, "fix.sna", diagnostics.Add);
        var lvlSna = HypeBinaryLoadHelpers.TryLoadSna(lvlSnaPath, $"{level.LevelName}.sna", diagnostics.Add);
        var fixRtb = HypeBinaryLoadHelpers.TryLoadRtb(fixRtbPath, "fix.rtb", diagnostics.Add);
        var fixLvlRtb = HypeBinaryLoadHelpers.TryLoadRtb(fixLvlRtbPath, "fixlvl.rtb", diagnostics.Add);
        var lvlRtb = HypeBinaryLoadHelpers.TryLoadRtb(lvlRtbPath, $"{level.LevelName}.rtb", diagnostics.Add);
        var fixRtp = HypeBinaryLoadHelpers.TryLoadRtb(fixRtpPath, "fix.rtp", diagnostics.Add);
        var lvlRtp = HypeBinaryLoadHelpers.TryLoadRtb(lvlRtpPath, $"{level.LevelName}.rtp", diagnostics.Add);
        var fixRtt = HypeBinaryLoadHelpers.TryLoadRtb(fixRttPath, "fix.rtt", diagnostics.Add);
        var lvlRtt = HypeBinaryLoadHelpers.TryLoadRtb(lvlRttPath, $"{level.LevelName}.rtt", diagnostics.Add);
        var fixGpt = HypeBinaryLoadHelpers.TryLoadBytes(fixGptPath, "fix.gpt", diagnostics.Add);
        var lvlGpt = HypeBinaryLoadHelpers.TryLoadBytes(lvlGptPath, $"{level.LevelName}.gpt", diagnostics.Add);
        var fixPtx = HypeBinaryLoadHelpers.TryLoadBytes(fixPtxPath, "fix.ptx", diagnostics.Add);
        var lvlPtx = HypeBinaryLoadHelpers.TryLoadBytes(lvlPtxPath, $"{level.LevelName}.ptx", diagnostics.Add);

        if (lvlSna == null || lvlRtb == null || lvlGpt == null)
        {
            diagnostics.Add("Level is missing required parse files (SNA/RTB/GPT).");
            return false;
        }

        var mergedFixRtb = HypeRelocationTable.Merge(fixRtb, fixLvlRtb);
        var space = new HypeRelocatedAddressSpace();
        space.AddSnaBlocks(fixSna, mergedFixRtb, "fix");
        space.AddSnaBlocks(lvlSna, lvlRtb, "lvl");
        space.AddPointerFile("fix_gpt", fixGpt, fixRtp);
        space.AddPointerFile("lvl_gpt", lvlGpt, lvlRtp);
        space.AddPointerFile("fix_ptx", fixPtx, fixRtt);
        space.AddPointerFile("lvl_ptx", lvlPtx, lvlRtt);

        if (!space.TryGetSegmentStart("lvl_gpt", out var levelGptAddress))
        {
            diagnostics.Add("Could not map level GPT segment.");
            return false;
        }

        context = new HypeParseContext
        {
            Space = space,
            LevelGptAddress = levelGptAddress
        };

        return true;
    }

    public static async Task<(bool Succeeded, HypeParseContext Context)> TryBuildAsync(
        HypeLevelRecord level,
        List<string> diagnostics,
        CancellationToken cancellationToken = default)
    {
        var context = default(HypeParseContext);
        var levelsRoot = Directory.GetParent(level.LevelDirectoryPath)?.FullName;
        if (string.IsNullOrWhiteSpace(levelsRoot))
        {
            diagnostics.Add("Could not resolve levels root from level directory.");
            return (false, context);
        }

        var fixSnaPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.sna");
        var fixRtbPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtb");
        var fixRtpPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtp");
        var fixRttPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.rtt");
        var fixGptPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.gpt");
        var fixPtxPath = HypeBinaryLoadHelpers.FindPath(levelsRoot, "fix.ptx");

        var lvlSnaPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".sna");
        var lvlRtbPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtb");
        var lvlRtpPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtp");
        var lvlRttPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".rtt");
        var lvlGptPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".gpt");
        var lvlPtxPath = HypeBinaryLoadHelpers.GetLevelCoreFile(level, ".ptx");
        var fixLvlRtbPath = HypeBinaryLoadHelpers.GetNamedCoreFile(level, "fixlvl.rtb");

        var fixSna = await HypeBinaryLoadHelpers.TryLoadSnaAsync(fixSnaPath, "fix.sna", diagnostics.Add, cancellationToken);
        var lvlSna = await HypeBinaryLoadHelpers.TryLoadSnaAsync(lvlSnaPath, $"{level.LevelName}.sna", diagnostics.Add, cancellationToken);
        var fixRtb = await HypeBinaryLoadHelpers.TryLoadRtbAsync(fixRtbPath, "fix.rtb", diagnostics.Add, cancellationToken);
        var fixLvlRtb = await HypeBinaryLoadHelpers.TryLoadRtbAsync(fixLvlRtbPath, "fixlvl.rtb", diagnostics.Add, cancellationToken);
        var lvlRtb = await HypeBinaryLoadHelpers.TryLoadRtbAsync(lvlRtbPath, $"{level.LevelName}.rtb", diagnostics.Add, cancellationToken);
        var fixRtp = await HypeBinaryLoadHelpers.TryLoadRtbAsync(fixRtpPath, "fix.rtp", diagnostics.Add, cancellationToken);
        var lvlRtp = await HypeBinaryLoadHelpers.TryLoadRtbAsync(lvlRtpPath, $"{level.LevelName}.rtp", diagnostics.Add, cancellationToken);
        var fixRtt = await HypeBinaryLoadHelpers.TryLoadRtbAsync(fixRttPath, "fix.rtt", diagnostics.Add, cancellationToken);
        var lvlRtt = await HypeBinaryLoadHelpers.TryLoadRtbAsync(lvlRttPath, $"{level.LevelName}.rtt", diagnostics.Add, cancellationToken);
        var fixGpt = await HypeBinaryLoadHelpers.TryLoadBytesAsync(fixGptPath, "fix.gpt", diagnostics.Add, cancellationToken);
        var lvlGpt = await HypeBinaryLoadHelpers.TryLoadBytesAsync(lvlGptPath, $"{level.LevelName}.gpt", diagnostics.Add, cancellationToken);
        var fixPtx = await HypeBinaryLoadHelpers.TryLoadBytesAsync(fixPtxPath, "fix.ptx", diagnostics.Add, cancellationToken);
        var lvlPtx = await HypeBinaryLoadHelpers.TryLoadBytesAsync(lvlPtxPath, $"{level.LevelName}.ptx", diagnostics.Add, cancellationToken);

        if (lvlSna == null || lvlRtb == null || lvlGpt == null)
        {
            diagnostics.Add("Level is missing required parse files (SNA/RTB/GPT).");
            return (false, context);
        }

        var mergedFixRtb = HypeRelocationTable.Merge(fixRtb, fixLvlRtb);
        var space = new HypeRelocatedAddressSpace();
        space.AddSnaBlocks(fixSna, mergedFixRtb, "fix");
        space.AddSnaBlocks(lvlSna, lvlRtb, "lvl");
        space.AddPointerFile("fix_gpt", fixGpt, fixRtp);
        space.AddPointerFile("lvl_gpt", lvlGpt, lvlRtp);
        space.AddPointerFile("fix_ptx", fixPtx, fixRtt);
        space.AddPointerFile("lvl_ptx", lvlPtx, lvlRtt);

        if (!space.TryGetSegmentStart("lvl_gpt", out var levelGptAddress))
        {
            diagnostics.Add("Could not map level GPT segment.");
            return (false, context);
        }

        context = new HypeParseContext
        {
            Space = space,
            LevelGptAddress = levelGptAddress
        };

        return (true, context);
    }

}
