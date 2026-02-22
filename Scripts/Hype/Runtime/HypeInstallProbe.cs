using System;
using System.Collections.Generic;
using System.IO;

namespace HypeReborn.Hype.Runtime;

public static class HypeInstallProbe
{
    private static readonly string[] RequiredDirectories = { "Gamedata", "LangData" };
    private static readonly string[] RequiredFiles = { "gamedsc.bin", "Textures.cnt", "Vignette.cnt" };

    public static bool TryResolveGameRoot(string anyPath, out string gameRoot)
    {
        gameRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(anyPath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(anyPath.Trim());
        if (IsValidGameRoot(candidate))
        {
            gameRoot = TrimPath(candidate);
            return true;
        }

        var nestedGamePath = Path.Combine(candidate, "Game");
        if (IsValidGameRoot(nestedGamePath))
        {
            gameRoot = TrimPath(nestedGamePath);
            return true;
        }

        return false;
    }

    public static IReadOnlyList<string> ValidateGameRoot(string gameRoot)
    {
        var errors = new List<string>();
        if (!Directory.Exists(gameRoot))
        {
            errors.Add("Game root directory does not exist.");
            return errors;
        }

        foreach (var requiredDir in RequiredDirectories)
        {
            var dirPath = Path.Combine(gameRoot, requiredDir);
            if (!Directory.Exists(dirPath))
            {
                errors.Add($"Missing required directory: {requiredDir}");
            }
        }

        var gameDataPath = Path.Combine(gameRoot, "Gamedata");
        foreach (var requiredFile in RequiredFiles)
        {
            var filePath = Path.Combine(gameDataPath, requiredFile);
            if (!File.Exists(filePath))
            {
                errors.Add($"Missing required file: Gamedata/{requiredFile}");
            }
        }

        var levelsRoot = Path.Combine(gameRoot, "Gamedata", "World", "Levels");
        if (!Directory.Exists(levelsRoot))
        {
            errors.Add("Missing levels directory: Gamedata/World/Levels");
        }

        return errors;
    }

    private static bool IsValidGameRoot(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        foreach (var requiredDir in RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(path, requiredDir)))
            {
                return false;
            }
        }

        return File.Exists(Path.Combine(path, "Gamedata", "gamedsc.bin"));
    }

    private static string TrimPath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}