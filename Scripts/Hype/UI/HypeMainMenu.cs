using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using HypeReborn.Hype.Config;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.UI;

public partial class HypeMainMenu : Control
{
    private static readonly (string Label, string NormalTga, string HoverTga, ActionKind Kind)[] MainMenuButtons =
    {
        ("New Game", @"FixTex\menus\mainmenu\npartie1.tga", @"FixTex\menus\mainmenu\npartie1_h.tga", ActionKind.NewGame),
        ("Load Map", @"FixTex\menus\mainmenu\parties_ex1.tga", @"FixTex\menus\mainmenu\parties_ex1_h.tga", ActionKind.LoadSelectedMap),
        ("Options", @"FixTex\menus\mainmenu\config.tga", @"FixTex\menus\mainmenu\config_h.tga", ActionKind.Options),
        ("Quit", @"FixTex\menus\mainmenu\quitter.tga", @"FixTex\menus\mainmenu\quitter_h.tga", ActionKind.Quit)
    };

    private OptionButton? _mapPicker;
    private Label? _statusLabel;
    private string _gameRoot = string.Empty;

    public override void _Ready()
    {
        HypeProjectSettings.EnsureDefaults();
        _gameRoot = HypeProjectSettings.TryGetValidatedGameRoot() ?? string.Empty;

        BuildUi();
        PopulateMapPicker();
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var background = new TextureRect
        {
            Name = "Background",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        background.Texture = LoadFirstTexture(
            @"MENU\menu_princ.tga",
            @"MENU\Bkmenu.tga",
            @"MENU\Bkmenu_old.tga",
            @"MENU\Bkmenu_old2.tga");

        var overlay = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.18f)
        };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        var root = new MarginContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 56);
        root.AddThemeConstantOverride("margin_top", 42);
        root.AddThemeConstantOverride("margin_right", 56);
        root.AddThemeConstantOverride("margin_bottom", 42);
        AddChild(root);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(row);

        var menuColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.Fill
        };
        menuColumn.AddThemeConstantOverride("separation", 12);
        row.AddChild(menuColumn);

        var title = new Label
        {
            Text = "Hype Reborn",
            Modulate = new Color(1f, 0.96f, 0.86f, 1f)
        };
        title.AddThemeFontSizeOverride("font_size", 44);
        menuColumn.AddChild(title);

        var subtitle = new Label
        {
            Text = "The Time Quest",
            Modulate = new Color(0.95f, 0.9f, 0.76f, 1f)
        };
        subtitle.AddThemeFontSizeOverride("font_size", 22);
        menuColumn.AddChild(subtitle);

        foreach (var descriptor in MainMenuButtons)
        {
            menuColumn.AddChild(BuildMenuAction(descriptor));
        }

        _mapPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        };
        menuColumn.AddChild(_mapPicker);

        _statusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.95f, 0.9f, 0.76f, 1f)
        };
        menuColumn.AddChild(_statusLabel);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        var sideNote = new Label
        {
            Text = "Assets are loaded from your original Hype install.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.95f, 0.9f, 0.76f, 1f),
            CustomMinimumSize = new Vector2(260, 0)
        };
        row.AddChild(sideNote);
    }

    private Control BuildMenuAction((string Label, string NormalTga, string HoverTga, ActionKind Kind) descriptor)
    {
        var normal = LoadTexture(descriptor.NormalTga);
        var hover = LoadTexture(descriptor.HoverTga) ?? normal;

        if (normal != null)
        {
            var textureButton = new TextureButton
            {
                TextureNormal = normal,
                TextureHover = hover,
                TexturePressed = hover,
                IgnoreTextureSize = false
            };
            textureButton.Pressed += () => RunAction(descriptor.Kind);
            return textureButton;
        }

        var fallbackButton = new Button
        {
            Text = descriptor.Label,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        };
        fallbackButton.Pressed += () => RunAction(descriptor.Kind);
        return fallbackButton;
    }

    private void PopulateMapPicker()
    {
        if (_mapPicker == null)
        {
            return;
        }

        _mapPicker.Clear();
        foreach (var scenePath in DiscoverMapScenes())
        {
            var index = _mapPicker.ItemCount;
            _mapPicker.AddItem(Path.GetFileNameWithoutExtension(scenePath));
            _mapPicker.SetItemMetadata(index, scenePath);
        }

        if (_mapPicker.ItemCount > 0)
        {
            _mapPicker.Selected = 0;
        }

        SetStatus(_mapPicker.ItemCount == 0
            ? "No map scenes found in res://Maps/Hype. Generate maps in the Hype Browser first."
            : $"Detected {_mapPicker.ItemCount} map scenes.");
    }

    private void RunAction(ActionKind action)
    {
        switch (action)
        {
            case ActionKind.NewGame:
                OpenNamedMap("astrolabe");
                break;
            case ActionKind.LoadSelectedMap:
                OpenSelectedMap();
                break;
            case ActionKind.Options:
                ShowOptionsSummary();
                break;
            case ActionKind.Quit:
                GetTree().Quit();
                break;
        }
    }

    private void OpenNamedMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        var path = $"res://Maps/Hype/{mapName.ToLowerInvariant()}.tscn";
        OpenScene(path);
    }

    private void OpenSelectedMap()
    {
        if (_mapPicker == null || _mapPicker.ItemCount == 0 || _mapPicker.Selected < 0)
        {
            SetStatus("No map selected.");
            return;
        }

        var metadata = _mapPicker.GetItemMetadata(_mapPicker.Selected);
        var path = metadata.AsString();
        OpenScene(path);
    }

    private void OpenScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            SetStatus("Scene path is empty.");
            return;
        }

        var packed = ResourceLoader.Load<PackedScene>(scenePath);
        if (packed == null)
        {
            SetStatus($"Could not load scene: {scenePath}");
            return;
        }

        var error = GetTree().ChangeSceneToPacked(packed);
        if (error != Error.Ok)
        {
            SetStatus($"Scene switch failed ({error}): {scenePath}");
            return;
        }
    }

    private void ShowOptionsSummary()
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
        {
            SetStatus("External Hype root is not configured. Set hype/external_game_root in project settings.");
            return;
        }

        var language = HypeProjectSettings.GetDefaultLanguage();
        SetStatus($"External root: {_gameRoot}\nLanguage: {language}");
    }

    private static IReadOnlyList<string> DiscoverMapScenes()
    {
        var list = new List<string>();
        var dir = DirAccess.Open("res://Maps/Hype");
        if (dir == null)
        {
            return list;
        }

        foreach (var fileName in dir.GetFiles())
        {
            if (fileName.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            {
                list.Add($"res://Maps/Hype/{fileName}");
            }
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private Texture2D? LoadFirstTexture(params string[] tgaNames)
    {
        foreach (var tga in tgaNames)
        {
            var texture = LoadTexture(tga);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }

    private Texture2D? LoadTexture(string tgaName)
    {
        if (string.IsNullOrWhiteSpace(_gameRoot) || string.IsNullOrWhiteSpace(tgaName))
        {
            return null;
        }

        return HypeTextureLookupService.TryGetTextureByTgaName(_gameRoot, tgaName);
    }

    private void SetStatus(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = message;
        }
    }

    private enum ActionKind
    {
        NewGame,
        LoadSelectedMap,
        Options,
        Quit
    }
}
