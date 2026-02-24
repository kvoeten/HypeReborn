using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HypeReborn.Hype.Config;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Characters;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.UI;

/// <summary>
/// Startup bootstrap scene that renders an original-game loading vignette while heavyweight
/// caches are prewarmed before entering the main menu.
///
/// IDA notes:
/// - Original exe exposes Vignette flow via "LoadVignette"/"LoadLevelVignette"
/// - References include "Gamedata\\Vignette\\random.vig" and "bar.bmp"
/// This loader reuses Vignette assets from CNT (welcome/menu backgrounds) as startup loading UI.
/// </summary>
public partial class HypeStartupLoader : Control
{
    [Export]
    public string NextScenePath { get; set; } = "res://Scenes/HypeMainMenu.tscn";

    [Export]
    public float MinimumDisplaySeconds { get; set; } = 0.35f;

    [Export]
    public bool PrewarmAssetIndex { get; set; } = true;

    [Export]
    public bool PrewarmActorCatalog { get; set; }

    [Export]
    public bool PrewarmMenuTextures { get; set; } = true;

    private TextureRect? _background;
    private Label? _statusLabel;

    public override void _Ready()
    {
        BuildUi();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        HypeProjectSettings.EnsureDefaults();
        var gameRoot = HypeProjectSettings.TryGetValidatedGameRoot();
        var language = HypeProjectSettings.GetDefaultLanguage();

        if (TryLoadStartupTexture(gameRoot, out var texture))
        {
            _background!.Texture = texture;
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var startedAt = Time.GetTicksMsec();
        Exception? backgroundError = null;

        var backgroundTask = Task.Run(() =>
        {
            try
            {
                PrewarmBackgroundData(gameRoot, language);
            }
            catch (Exception ex)
            {
                backgroundError = ex;
            }
        });

        if (PrewarmMenuTextures && !string.IsNullOrWhiteSpace(gameRoot))
        {
            await PrewarmMenuTexturesAsync(gameRoot);
        }

        await backgroundTask;

        if (PrewarmActorCatalog && !string.IsNullOrWhiteSpace(gameRoot))
        {
            SetStatus("Loading actor catalog...");
            _ = HypeActorCatalogService.BuildCatalog(gameRoot);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (backgroundError != null)
        {
            GD.PrintErr($"[HypeStartupLoader] Prewarm failed: {backgroundError.Message}");
            SetStatus("Startup prewarm failed. Continuing with live loading...");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var minDurationMs = (ulong)(Mathf.Max(0f, MinimumDisplaySeconds) * 1000f);
        while (Time.GetTicksMsec() - startedAt < minDurationMs)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var sceneError = GetTree().ChangeSceneToFile(NextScenePath);
        if (sceneError != Error.Ok)
        {
            SetStatus($"Failed to open menu scene ({sceneError}).");
            GD.PrintErr($"[HypeStartupLoader] Scene switch failed: {sceneError} -> {NextScenePath}");
        }
    }

    private void PrewarmBackgroundData(string? gameRoot, string language)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return;
        }

        if (PrewarmAssetIndex)
        {
            _ = HypeAssetResolver.BuildIndex(gameRoot, language);
        }
    }

    private async Task PrewarmMenuTexturesAsync(string gameRoot)
    {
        SetStatus("Loading original UI textures...");
        foreach (var tgaName in BuildMenuTextureCandidates())
        {
            _ = HypeTextureLookupService.TryGetTextureByTgaName(gameRoot, tgaName);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private bool TryLoadStartupTexture(string? gameRoot, out Texture2D? texture)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            SetStatus("No Hype install configured. Opening menu...");
            return false;
        }

        var language = HypeProjectSettings.GetDefaultLanguage();
        var candidates = new[]
        {
            $@"MultiLanguage\{language}\welcome.tga",
            @"MultiLanguage\English\welcome.tga",
            @"MultiLanguage\French\welcome.tga",
            @"MENU\Bkmenu.tga",
            @"MENU\menu_princ.tga"
        };

        foreach (var candidate in candidates)
        {
            texture = HypeTextureLookupService.TryGetTextureByTgaName(gameRoot, candidate);
            if (texture != null)
            {
                SetStatus("Loading game data...");
                return true;
            }
        }

        SetStatus("Loading game data...");
        return false;
    }

    private static IReadOnlyList<string> BuildMenuTextureCandidates()
    {
        return new[]
        {
            @"MENU\menu_princ.tga",
            @"MENU\Bkmenu.tga",
            @"FixTex\menus\mainmenu\npartie1.tga",
            @"FixTex\menus\mainmenu\npartie1_h.tga",
            @"FixTex\menus\mainmenu\parties_ex1.tga",
            @"FixTex\menus\mainmenu\parties_ex1_h.tga",
            @"FixTex\menus\mainmenu\config.tga",
            @"FixTex\menus\mainmenu\config_h.tga",
            @"FixTex\menus\mainmenu\quitter.tga",
            @"FixTex\menus\mainmenu\quitter_h.tga"
        };
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _background = new TextureRect
        {
            Name = "Background",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
        };
        _background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_background);

        var overlay = new ColorRect
        {
            Name = "Overlay",
            Color = new Color(0f, 0f, 0f, 0.24f)
        };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        _statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = "Loading...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(1f, 0.95f, 0.82f, 1f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _statusLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _statusLabel.OffsetBottom = -24f;
        _statusLabel.OffsetTop = -72f;
        _statusLabel.AddThemeFontSizeOverride("font_size", 28);
        AddChild(_statusLabel);
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = text;
        }
    }
}
