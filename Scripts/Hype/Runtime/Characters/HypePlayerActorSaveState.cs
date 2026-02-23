using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace HypeReborn.Hype.Runtime.Characters;

public static class HypePlayerActorSaveState
{
    private const string SavePath = "user://hype_player_actor_state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static bool _loaded;
    private static SaveStateData _state = new();

    public static string GetSelectedActorKey()
    {
        EnsureLoaded();
        return _state.SelectedActorKey ?? string.Empty;
    }

    public static bool HasSelectedActorKey()
    {
        return !string.IsNullOrWhiteSpace(GetSelectedActorKey());
    }

    public static void SetSelectedActorKey(string actorKey)
    {
        EnsureLoaded();
        _state.SelectedActorKey = actorKey?.Trim() ?? string.Empty;
        Save();
    }

    public static void ClearSelectedActorKey()
    {
        EnsureLoaded();
        _state.SelectedActorKey = string.Empty;
        Save();
    }

    public static bool EnsureDefaultSelection(HypeActorCatalog catalog)
    {
        if (HasSelectedActorKey())
        {
            return true;
        }

        var defaultKey = HypeActorCatalogService.ChooseDefaultHeroKey(catalog);
        if (string.IsNullOrWhiteSpace(defaultKey))
        {
            return false;
        }

        SetSelectedActorKey(defaultKey);
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(SavePath);
            if (!File.Exists(absolutePath))
            {
                _state = new SaveStateData();
                return;
            }

            var json = File.ReadAllText(absolutePath);
            _state = JsonSerializer.Deserialize<SaveStateData>(json) ?? new SaveStateData();
        }
        catch (Exception ex)
        {
            _state = new SaveStateData();
            GD.PrintErr($"[HypePlayerActorSaveState] Load failed: {ex.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            var absolutePath = ProjectSettings.GlobalizePath(SavePath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(absolutePath, json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HypePlayerActorSaveState] Save failed: {ex.Message}");
        }
    }

    private sealed class SaveStateData
    {
        public string SelectedActorKey { get; set; } = string.Empty;
    }
}
