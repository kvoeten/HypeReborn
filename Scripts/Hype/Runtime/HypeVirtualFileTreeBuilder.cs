using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HypeReborn.Hype.Runtime;

public static class HypeVirtualFileTreeBuilder
{
    public static HypeVirtualFileEntry Build(HypeAssetIndex index)
    {
        var root = new HypeVirtualFileEntry
        {
            Name = "Hype",
            VirtualPath = "/",
            Kind = HypeContentKind.Root,
            AbsolutePath = index.GameRoot
        };

        var maps = new HypeVirtualFileEntry
        {
            Name = "Maps",
            VirtualPath = "/Maps",
            Kind = HypeContentKind.Category
        };
        root.Children.Add(maps);

        var parsedByLevel = index.ParsedLevels
            .ToDictionary(x => x.LevelName, StringComparer.OrdinalIgnoreCase);
        var parsedAssetsByLevel = index.ParsedMapAssets
            .ToDictionary(x => x.LevelName, StringComparer.OrdinalIgnoreCase);

        foreach (var level in index.Levels)
        {
            parsedByLevel.TryGetValue(level.LevelName, out var parsedLevel);
            parsedAssetsByLevel.TryGetValue(level.LevelName, out var parsedAssets);
            maps.Children.Add(new HypeVirtualFileEntry
            {
                Name = level.LevelName,
                VirtualPath = $"/Maps/{level.LevelName}",
                Kind = HypeContentKind.Map,
                AbsolutePath = level.LevelDirectoryPath,
                AuxData = BuildMapSummary(parsedLevel, parsedAssets)
            });
        }

        var parserRoot = new HypeVirtualFileEntry
        {
            Name = "Parser",
            VirtualPath = "/Parser",
            Kind = HypeContentKind.ParserCategory
        };
        root.Children.Add(parserRoot);

        AddParserLevels(parserRoot, index.ParsedLevels);
        AddParsedMapAssets(parserRoot, index.ParsedMapAssets);

        var animations = new HypeVirtualFileEntry
        {
            Name = "Animations",
            VirtualPath = "/Animations",
            Kind = HypeContentKind.Category
        };
        root.Children.Add(animations);

        AddAnimations(animations, index.Animations);

        var scripts = new HypeVirtualFileEntry
        {
            Name = "Scripts",
            VirtualPath = "/Scripts",
            Kind = HypeContentKind.Category
        };
        root.Children.Add(scripts);

        AddScripts(scripts, index.Scripts);

        var textures = new HypeVirtualFileEntry
        {
            Name = "TextureContainers",
            VirtualPath = "/TextureContainers",
            Kind = HypeContentKind.Category
        };
        root.Children.Add(textures);

        foreach (var container in index.TextureContainers.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var containerEntry = new HypeVirtualFileEntry
            {
                Name = container.Name,
                VirtualPath = $"/{container.VirtualPath}",
                Kind = HypeContentKind.TextureContainer,
                AbsolutePath = container.SourceFile,
                AuxData = container.Id
            };
            textures.Children.Add(containerEntry);

            AddTextureEntries(containerEntry, container, index.TextureEntries);
        }

        return root;
    }

    private static void AddAnimations(HypeVirtualFileEntry animationsRoot, IReadOnlyList<HypeAnimationRecord> animations)
    {
        foreach (var group in animations.GroupBy(x => x.SourceLevel).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var levelEntry = new HypeVirtualFileEntry
            {
                Name = group.Key,
                VirtualPath = $"{animationsRoot.VirtualPath}/{group.Key}",
                Kind = HypeContentKind.Category
            };
            animationsRoot.Children.Add(levelEntry);

            foreach (var animation in group.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                levelEntry.Children.Add(new HypeVirtualFileEntry
                {
                    Name = Path.GetFileName(animation.SourceFile),
                    VirtualPath = $"/{animation.VirtualPath}",
                    Kind = HypeContentKind.Animation,
                    AbsolutePath = animation.SourceFile,
                    AuxData = animation.Id
                });
            }
        }
    }

    private static void AddScripts(HypeVirtualFileEntry scriptsRoot, IReadOnlyList<HypeScriptRecord> scripts)
    {
        foreach (var group in scripts.GroupBy(x => x.SourceLevel).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var levelEntry = new HypeVirtualFileEntry
            {
                Name = group.Key,
                VirtualPath = $"{scriptsRoot.VirtualPath}/{group.Key}",
                Kind = HypeContentKind.Category
            };
            scriptsRoot.Children.Add(levelEntry);

            foreach (var script in group.OrderBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase))
            {
                levelEntry.Children.Add(new HypeVirtualFileEntry
                {
                    Name = Path.GetFileName(script.SourceFile),
                    VirtualPath = $"/{script.VirtualPath}",
                    Kind = HypeContentKind.Script,
                    AbsolutePath = script.SourceFile,
                    AuxData = script.ScriptKind
                });
            }
        }
    }

    private static void AddTextureEntries(
        HypeVirtualFileEntry containerEntry,
        HypeTextureContainerRecord container,
        IReadOnlyList<HypeTextureEntryRecord> allEntries)
    {
        var entries = allEntries
            .Where(x => x.ContainerId.Equals(container.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.EntryFullName, StringComparer.OrdinalIgnoreCase);

        var folderCache = new Dictionary<string, HypeVirtualFileEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = containerEntry
        };

        foreach (var entry in entries)
        {
            var segments = SplitPath(entry.EntryFullName);
            if (segments.Length == 0)
            {
                continue;
            }

            var parent = containerEntry;
            var folderKey = string.Empty;

            for (var i = 0; i < segments.Length - 1; i++)
            {
                var folderName = segments[i];
                folderKey = folderKey.Length == 0 ? folderName : $"{folderKey}/{folderName}";

                if (!folderCache.TryGetValue(folderKey, out var folderNode))
                {
                    folderNode = new HypeVirtualFileEntry
                    {
                        Name = folderName,
                        VirtualPath = $"{containerEntry.VirtualPath}/{folderKey}",
                        Kind = HypeContentKind.TextureFolder,
                        AbsolutePath = container.SourceFile
                    };
                    parent.Children.Add(folderNode);
                    folderCache[folderKey] = folderNode;
                }

                parent = folderNode;
            }

            var fileName = segments[segments.Length - 1];
            parent.Children.Add(new HypeVirtualFileEntry
            {
                Name = fileName,
                VirtualPath = $"/{entry.VirtualPath}",
                Kind = HypeContentKind.TextureEntry,
                AbsolutePath = entry.ContainerSourceFile,
                AuxData = entry.EntryFullName
            });
        }
    }

    private static string[] SplitPath(string value)
    {
        return value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void AddParserLevels(
        HypeVirtualFileEntry parserRoot,
        IReadOnlyList<HypeParsedLevelRecord> parsedLevels)
    {
        foreach (var parsed in parsedLevels.OrderBy(x => x.LevelName, StringComparer.OrdinalIgnoreCase))
        {
            var levelNode = new HypeVirtualFileEntry
            {
                Name = parsed.LevelName,
                VirtualPath = $"{parserRoot.VirtualPath}/{parsed.LevelName}",
                Kind = HypeContentKind.ParserLevel,
                AuxData = BuildMapSummary(parsed, null)
            };
            parserRoot.Children.Add(levelNode);

            foreach (var diagnostic in parsed.Diagnostics)
            {
                var diagnosticNode = new HypeVirtualFileEntry
                {
                    Name = $"[{diagnostic.Severity}] {diagnostic.Phase}",
                    VirtualPath = $"{levelNode.VirtualPath}/{diagnostic.Phase}/{diagnostic.Severity}",
                    Kind = HypeContentKind.ParserDiagnostic,
                    AuxData = diagnostic.Message
                };
                levelNode.Children.Add(diagnosticNode);
            }
        }
    }

    private static void AddParsedMapAssets(
        HypeVirtualFileEntry parserRoot,
        IReadOnlyList<HypeParsedMapAssetRecord> parsedMapAssets)
    {
        var resolvedRoot = new HypeVirtualFileEntry
        {
            Name = "ResolvedMapAssets",
            VirtualPath = $"{parserRoot.VirtualPath}/ResolvedMapAssets",
            Kind = HypeContentKind.ParserCategory
        };
        parserRoot.Children.Add(resolvedRoot);

        foreach (var parsed in parsedMapAssets.OrderBy(x => x.LevelName, StringComparer.OrdinalIgnoreCase))
        {
            var levelNode = new HypeVirtualFileEntry
            {
                Name = parsed.LevelName,
                VirtualPath = $"{resolvedRoot.VirtualPath}/{parsed.LevelName}",
                Kind = HypeContentKind.ParserLevel,
                AuxData = BuildParsedMapAssetSummary(parsed)
            };
            resolvedRoot.Children.Add(levelNode);

            if (!parsed.Succeeded && !string.IsNullOrWhiteSpace(parsed.ErrorMessage))
            {
                levelNode.Children.Add(new HypeVirtualFileEntry
                {
                    Name = "[Error] ResolveMapAssets",
                    VirtualPath = $"{levelNode.VirtualPath}/error",
                    Kind = HypeContentKind.ParserDiagnostic,
                    AuxData = parsed.ErrorMessage
                });
            }

            foreach (var usage in parsed.TextureUsages)
            {
                levelNode.Children.Add(new HypeVirtualFileEntry
                {
                    Name = $"{usage.TextureTgaName} ({usage.SurfaceCount})",
                    VirtualPath = $"{levelNode.VirtualPath}/texture/{usage.TextureTgaName}",
                    Kind = HypeContentKind.ParserDiagnostic,
                    AuxData = $"Texture dependency: {usage.TextureTgaName}\nSurface references: {usage.SurfaceCount}"
                });
            }
        }
    }

    private static string BuildMapSummary(HypeParsedLevelRecord? parsed, HypeParsedMapAssetRecord? parsedAssets)
    {
        if (parsed == null)
        {
            return "Parser: no data";
        }

        var summary = new List<string>
        {
            $"Parser success: {parsed.Succeeded}",
            $"SNA blocks: fix={parsed.FixSnaBlockCount}, level={parsed.LevelSnaBlockCount}",
            $"Relocation blocks: fix={parsed.FixRelocationBlockCount}, level={parsed.LevelRelocationBlockCount}",
            $"Resolved pointers: sna={parsed.ResolvedSnaPointers}, gpt={parsed.ResolvedGptPointers}, ptx={parsed.ResolvedPtxPointers}",
            $"Unresolved pointers: sna={parsed.UnresolvedSnaPointers}, gpt={parsed.UnresolvedGptPointers}, ptx={parsed.UnresolvedPtxPointers}",
            $"Diagnostics: {parsed.Diagnostics.Count}"
        };

        if (parsedAssets != null)
        {
            summary.Add($"Resolved assets success: {parsedAssets.Succeeded}");
            summary.Add($"Resolved geometry={parsedAssets.GeometryEntityCount}, lights={parsedAssets.LightEntityCount}, particles={parsedAssets.ParticleSourceCount}, animAnchors={parsedAssets.AnimationAnchorCount}");
            summary.Add($"Resolved mesh surfaces: {parsedAssets.MeshSurfaceCount}");
            summary.Add($"Resolved texture dependencies: {parsedAssets.TextureUsages.Count}");
            if (!string.IsNullOrWhiteSpace(parsedAssets.ErrorMessage))
            {
                summary.Add($"Resolved assets error: {parsedAssets.ErrorMessage}");
            }
        }

        return string.Join("\n", summary);
    }

    private static string BuildParsedMapAssetSummary(HypeParsedMapAssetRecord parsed)
    {
        var summary = new List<string>
        {
            $"Resolved assets success: {parsed.Succeeded}",
            $"Geometry entities: {parsed.GeometryEntityCount}",
            $"Lights: {parsed.LightEntityCount}",
            $"Particle sources: {parsed.ParticleSourceCount}",
            $"Animation anchors: {parsed.AnimationAnchorCount}",
            $"Mesh surfaces: {parsed.MeshSurfaceCount}",
            $"Texture dependencies: {parsed.TextureUsages.Count}"
        };

        if (!string.IsNullOrWhiteSpace(parsed.ErrorMessage))
        {
            summary.Add($"Error: {parsed.ErrorMessage}");
        }

        return string.Join("\n", summary);
    }
}
