using System;

namespace HypeReborn.Hype.Runtime;

public static class HypePathUtils
{
    public static string NormalizePathSeparators(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('/', '\\');
    }

    public static string? NormalizeTextureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizePathSeparators(value.Trim()).TrimStart('\\');
        normalized = StripGameDataPrefix(normalized);
        normalized = ChangeGfExtension(normalized);
        return normalized;
    }

    public static string StripGameDataPrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path.StartsWith("gamedata\\", StringComparison.OrdinalIgnoreCase))
        {
            return path["gamedata\\".Length..];
        }

        if (path.StartsWith("game\\gamedata\\", StringComparison.OrdinalIgnoreCase))
        {
            return path["game\\gamedata\\".Length..];
        }

        return path;
    }

    public static string ChangeGfExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(".gf", StringComparison.OrdinalIgnoreCase)
            ? path[..^3] + ".tga"
            : path;
    }
}
