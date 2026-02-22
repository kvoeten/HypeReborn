using System;
using System.IO;
using Godot;
using HypeReborn.Hype.Runtime;

namespace HypeReborn.Hype.Config;

public static class HypeProjectSettings
{
    public const string ExternalGameRootSetting = "hype/external_game_root";
    public const string DefaultLanguageSetting = "hype/default_language";

    public static void EnsureDefaults()
    {
        if (!ProjectSettings.HasSetting(ExternalGameRootSetting))
        {
            ProjectSettings.SetSetting(ExternalGameRootSetting, string.Empty);
            ProjectSettings.SetInitialValue(ExternalGameRootSetting, string.Empty);
        }

        if (!ProjectSettings.HasSetting(DefaultLanguageSetting))
        {
            ProjectSettings.SetSetting(DefaultLanguageSetting, "Dutch");
            ProjectSettings.SetInitialValue(DefaultLanguageSetting, "Dutch");
        }

        ProjectSettings.Save();
    }

    public static string GetExternalGameRoot()
    {
        EnsureDefaults();
        var root = Convert.ToString(ProjectSettings.GetSetting(ExternalGameRootSetting, string.Empty)) ?? string.Empty;
        return root.Trim();
    }

    public static void SetExternalGameRoot(string absolutePath)
    {
        EnsureDefaults();
        ProjectSettings.SetSetting(ExternalGameRootSetting, absolutePath.Trim());
        ProjectSettings.Save();
    }

    public static string GetDefaultLanguage()
    {
        EnsureDefaults();
        var language = Convert.ToString(ProjectSettings.GetSetting(DefaultLanguageSetting, "Dutch")) ?? "Dutch";
        return language.Trim();
    }

    public static void SetDefaultLanguage(string language)
    {
        EnsureDefaults();
        ProjectSettings.SetSetting(DefaultLanguageSetting, language.Trim());
        ProjectSettings.Save();
    }

    public static string? TryGetValidatedGameRoot()
    {
        var configured = GetExternalGameRoot();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return HypeInstallProbe.TryResolveGameRoot(configured, out var gameRoot)
            ? gameRoot
            : null;
    }

    public static string ToProjectRelativePath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return absolutePath;
        }

        try
        {
            return ProjectSettings.LocalizePath(absolutePath);
        }
        catch
        {
            return absolutePath;
        }
    }

    public static string ToCanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
