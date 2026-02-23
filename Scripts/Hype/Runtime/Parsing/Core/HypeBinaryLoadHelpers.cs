using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HypeReborn.Hype.Runtime.Parsing;

internal static class HypeBinaryLoadHelpers
{
    public static string? GetLevelCoreFile(HypeLevelRecord level, string extension)
    {
        return GetNamedCoreFile(level, $"{level.LevelName}{extension}");
    }

    public static string? GetNamedCoreFile(HypeLevelRecord level, string fileName)
    {
        if (level.CoreFiles.TryGetValue(fileName, out var path) && !string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return null;
    }

    public static string? FindPath(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static HypeSnaImage? TryLoadSna(string? path, string tag, Action<string> onDiagnostic)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return HypeSnaImage.Load(path, snaCompression: true);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    public static HypeRelocationTable? TryLoadRtb(string? path, string tag, Action<string> onDiagnostic)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return HypeRelocationTable.Load(path, snaCompression: true);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    public static byte[]? TryLoadBytes(string? path, string tag, Action<string> onDiagnostic)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to read {tag}: {ex.Message}");
            return null;
        }
    }

    public static async Task<HypeSnaImage?> TryLoadSnaAsync(
        string? path,
        string tag,
        Action<string> onDiagnostic,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return await HypeSnaImage.LoadAsync(path, snaCompression: true, cancellationToken);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    public static async Task<HypeRelocationTable?> TryLoadRtbAsync(
        string? path,
        string tag,
        Action<string> onDiagnostic,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return await HypeRelocationTable.LoadAsync(path, snaCompression: true, cancellationToken);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    public static async Task<byte[]?> TryLoadBytesAsync(
        string? path,
        string tag,
        Action<string> onDiagnostic,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            onDiagnostic($"Failed to read {tag}: {ex.Message}");
            return null;
        }
    }
}
