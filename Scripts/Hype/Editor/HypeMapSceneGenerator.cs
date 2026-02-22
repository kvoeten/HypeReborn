using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using HypeReborn.Hype.Maps;

namespace HypeReborn.Hype.Editor;

[Tool]
public static class HypeMapSceneGenerator
{
    private const string MapsRoot = "res://Maps/Hype";
    private const string DefinitionsRoot = "res://Maps/Hype/Definitions";

    public static IReadOnlyList<string> GenerateMapScenes(IEnumerable<string> levelNames)
    {
        EnsureDirectory(MapsRoot);
        EnsureDirectory(DefinitionsRoot);

        var output = new List<string>();
        foreach (var rawLevelName in levelNames)
        {
            var levelName = SanitizeLevelName(rawLevelName);
            if (string.IsNullOrWhiteSpace(levelName))
            {
                continue;
            }

            var definition = EnsureDefinition(levelName);
            var scenePath = EnsureMapScene(levelName, definition);
            output.Add(scenePath);
        }

        return output;
    }

    private static HypeMapDefinition EnsureDefinition(string levelName)
    {
        var definitionPath = $"{DefinitionsRoot}/{levelName}.tres";
        var loaded = ResourceLoader.Load(definitionPath, string.Empty, ResourceLoader.CacheMode.Ignore);
        var definition = loaded switch
        {
            HypeMapDefinition typed => typed,
            Resource legacy => MigrateLegacyDefinition(legacy),
            _ => new HypeMapDefinition()
        };

        definition.LevelName = levelName;

        var saveErr = ResourceSaver.Save(definition, definitionPath);
        if (saveErr != Error.Ok)
        {
            throw new IOException($"Could not save definition '{definitionPath}': {saveErr}");
        }

        return definition;
    }

    private static HypeMapDefinition MigrateLegacyDefinition(Resource legacy)
    {
        var migrated = new HypeMapDefinition
        {
            LevelName = ReadString(legacy, nameof(HypeMapDefinition.LevelName)),
            ExternalGameRootOverride = ReadString(legacy, nameof(HypeMapDefinition.ExternalGameRootOverride)),
            LoadLanguageLayer = ReadBool(legacy, nameof(HypeMapDefinition.LoadLanguageLayer), true),
            LoadScripts = ReadBool(legacy, nameof(HypeMapDefinition.LoadScripts), true),
            LoadAnimationCatalog = ReadBool(legacy, nameof(HypeMapDefinition.LoadAnimationCatalog), true),
            DesignerNotes = ReadString(legacy, nameof(HypeMapDefinition.DesignerNotes))
        };
        return migrated;
    }

    private static string ReadString(Resource resource, string propertyName, string fallback = "")
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsString();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Resource resource, string propertyName, bool fallback)
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsBool();
        }
        catch
        {
            return fallback;
        }
    }

    private static string EnsureMapScene(string levelName, HypeMapDefinition definition)
    {
        var scenePath = $"{MapsRoot}/{levelName}.tscn";

        var root = new HypeMapRoot
        {
            Name = ToSceneRootName(levelName),
            MapDefinition = definition,
            AutoRefreshInEditor = true
        };

        var packed = new PackedScene();
        var packErr = packed.Pack(root);
        if (packErr != Error.Ok)
        {
            root.QueueFree();
            throw new IOException($"Could not pack scene for level '{levelName}': {packErr}");
        }

        var saveErr = ResourceSaver.Save(packed, scenePath);
        root.QueueFree();

        if (saveErr != Error.Ok)
        {
            throw new IOException($"Could not save scene '{scenePath}': {saveErr}");
        }

        return scenePath;
    }

    private static void EnsureDirectory(string resPath)
    {
        var absolutePath = ProjectSettings.GlobalizePath(resPath);
        Directory.CreateDirectory(absolutePath);
    }

    private static string SanitizeLevelName(string value)
    {
        var result = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        return result.ToLowerInvariant();
    }

    private static string ToSceneRootName(string levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName))
        {
            return "HypeMap";
        }

        return char.ToUpperInvariant(levelName[0]) + levelName.Substring(1) + "Map";
    }
}
