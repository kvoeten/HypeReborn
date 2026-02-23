using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using HypeReborn.Hype.Config;
using HypeReborn.Hype.Maps;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Textures;
using HypeEditor = HypeReborn.Hype.Editor;

namespace HypeReborn.Hype.EditorPlugin;

[Tool]
public partial class HypeBrowserDock : VBoxContainer
{
    private sealed class SelectedAsset
    {
        public string Kind { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string VirtualPath { get; init; } = string.Empty;
        public string AbsolutePath { get; init; } = string.Empty;
        public string AuxData { get; init; } = string.Empty;
    }

    private EditorInterface? _editorInterface;
    private LineEdit? _rootPathEdit;
    private LineEdit? _languageEdit;
    private Tree? _assetTree;
    private Label? _statusLabel;
    private EditorFileDialog? _directoryDialog;
    private Label? _previewTitle;
    private Label? _previewDetails;
    private TextureRect? _previewTexture;
    private TextEdit? _previewText;
    private Button? _openSelectedMapButton;

    private HypeAssetIndex? _currentIndex;
    private string? _selectedMapName;

    public void Initialize(EditorInterface editorInterface)
    {
        _editorInterface = editorInterface;
    }

    public override void _Ready()
    {
        BuildUi();
        RefreshFromProjectSettings();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var title = new Label { Text = "Hype External Asset Browser" };
        AddChild(title);

        var rootRow = new HBoxContainer();
        AddChild(rootRow);

        _rootPathEdit = new LineEdit
        {
            PlaceholderText = "Path to Hype Game folder (or parent folder containing Game/)"
        };
        rootRow.AddChild(_rootPathEdit);
        _rootPathEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var browseButton = new Button { Text = "Browse" };
        browseButton.Pressed += OnBrowsePressed;
        rootRow.AddChild(browseButton);

        var languageRow = new HBoxContainer();
        AddChild(languageRow);

        var languageLabel = new Label { Text = "Language" };
        languageRow.AddChild(languageLabel);

        _languageEdit = new LineEdit { PlaceholderText = "Dutch" };
        _languageEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        languageRow.AddChild(_languageEdit);

        var actionRow = new HBoxContainer();
        AddChild(actionRow);

        var refreshButton = new Button { Text = "Refresh" };
        refreshButton.Pressed += RefreshIndex;
        actionRow.AddChild(refreshButton);

        var generateButton = new Button { Text = "Generate Map Scenes" };
        generateButton.Pressed += GenerateAllMapScenes;
        actionRow.AddChild(generateButton);

        var rebuildButton = new Button { Text = "Rebuild Open Map" };
        rebuildButton.Pressed += RebuildOpenMap;
        actionRow.AddChild(rebuildButton);

        _statusLabel = new Label
        {
            Text = "Set a valid game root to begin.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        AddChild(_statusLabel);

        _assetTree = new Tree();
        _assetTree.Columns = 2;
        _assetTree.SetColumnTitle(0, "Asset");
        _assetTree.SetColumnTitle(1, "Kind");
        _assetTree.ColumnTitlesVisible = true;
        _assetTree.HideRoot = false;
        _assetTree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _assetTree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _assetTree.ItemSelected += OnTreeItemSelected;
        _assetTree.ItemActivated += OnTreeItemActivated;

        var split = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        AddChild(split);

        split.AddChild(_assetTree);

        var previewPane = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        split.AddChild(previewPane);

        _previewTitle = new Label
        {
            Text = "Preview"
        };
        previewPane.AddChild(_previewTitle);

        _previewDetails = new Label
        {
            Text = "Select an asset to inspect.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        previewPane.AddChild(_previewDetails);

        _previewTexture = new TextureRect
        {
            CustomMinimumSize = new Vector2(0f, 180f),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        previewPane.AddChild(_previewTexture);

        _openSelectedMapButton = new Button
        {
            Text = "Open Selected Map",
            Disabled = true
        };
        _openSelectedMapButton.Pressed += OpenSelectedMap;
        previewPane.AddChild(_openSelectedMapButton);

        _previewText = new TextEdit
        {
            Editable = false,
            Visible = false,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        previewPane.AddChild(_previewText);

        _directoryDialog = new EditorFileDialog
        {
            FileMode = EditorFileDialog.FileModeEnum.OpenDir,
            Access = EditorFileDialog.AccessEnum.Filesystem,
            Title = "Select Hype root"
        };
        _directoryDialog.DirSelected += OnDirectorySelected;
        AddChild(_directoryDialog);
    }

    private void RefreshFromProjectSettings()
    {
        if (_rootPathEdit == null || _languageEdit == null)
        {
            return;
        }

        _rootPathEdit.Text = HypeProjectSettings.GetExternalGameRoot();
        _languageEdit.Text = HypeProjectSettings.GetDefaultLanguage();
        RefreshIndex();
    }

    private void OnBrowsePressed()
    {
        _directoryDialog?.PopupCenteredRatio(0.8f);
    }

    private void OnDirectorySelected(string path)
    {
        if (_rootPathEdit != null)
        {
            _rootPathEdit.Text = path;
        }

        RefreshIndex();
    }

    private void RefreshIndex()
    {
        if (_rootPathEdit == null || _languageEdit == null || _statusLabel == null || _assetTree == null)
        {
            return;
        }

        var inputRoot = _rootPathEdit.Text.Trim();
        var language = string.IsNullOrWhiteSpace(_languageEdit.Text) ? "Dutch" : _languageEdit.Text.Trim();

        HypeProjectSettings.SetExternalGameRoot(inputRoot);
        HypeProjectSettings.SetDefaultLanguage(language);

        _assetTree.Clear();

        if (!HypeInstallProbe.TryResolveGameRoot(inputRoot, out var gameRoot))
        {
            _statusLabel.Text = "Invalid game root. Point to the installed Game folder, or its parent containing Game/.";
            _currentIndex = null;
            return;
        }

        var validation = HypeInstallProbe.ValidateGameRoot(gameRoot);
        if (validation.Count > 0)
        {
            _statusLabel.Text = "Game root is incomplete: " + string.Join(" | ", validation);
            _currentIndex = null;
            return;
        }

        _rootPathEdit.Text = gameRoot;

        try
        {
            _currentIndex = HypeAssetIndexer.Build(
                gameRoot,
                language,
                includeResolvedMapAssets: false,
                forceRefresh: false);
            PopulateTree(_currentIndex);

            var parsedOk = _currentIndex.ParsedLevels.Count(x => x.Succeeded);
            var parsedFail = _currentIndex.ParsedLevels.Count - parsedOk;
            var resolvedOk = _currentIndex.ParsedMapAssets.Count(x => x.Succeeded);
            var resolvedFail = _currentIndex.ParsedMapAssets.Count - resolvedOk;
            _statusLabel.Text = $"Parser levels: ok={parsedOk}, failed={parsedFail} | Resolved maps: ok={resolvedOk}, failed={resolvedFail} | Indexed {_currentIndex.Levels.Count} maps, {_currentIndex.Animations.Count} animation banks, {_currentIndex.Scripts.Count} script sources, {_currentIndex.TextureEntries.Count} texture entries.";
        }
        catch (Exception ex)
        {
            _currentIndex = null;
            _statusLabel.Text = "Indexing failed: " + ex.Message;
        }
    }

    private void PopulateTree(HypeAssetIndex index)
    {
        if (_assetTree == null)
        {
            return;
        }

        var rootEntry = HypeVirtualFileTreeBuilder.Build(index);
        var rootItem = _assetTree.CreateItem();
        FillTreeItem(rootItem, rootEntry);
        rootItem.Collapsed = false;
    }

    private void FillTreeItem(TreeItem item, HypeVirtualFileEntry entry)
    {
        item.SetText(0, entry.Name);
        item.SetText(1, entry.Kind.ToString());

        var metadata = BuildMetadata(entry);
        item.SetMetadata(0, metadata);

        foreach (var child in entry.Children)
        {
            var childItem = _assetTree!.CreateItem(item);
            FillTreeItem(childItem, child);
        }
    }

    private void OnTreeItemActivated()
    {
        if (_assetTree == null || _editorInterface == null)
        {
            return;
        }

        var item = _assetTree.GetSelected();
        if (item == null)
        {
            return;
        }

        var selected = ParseMetadata(item.GetMetadata(0).AsString(), item);

        if (!selected.Kind.Equals(HypeContentKind.Map.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_currentIndex != null &&
            TryGetParsedLevel(selected.Name, out var parsedLevel) &&
            parsedLevel != null &&
            !parsedLevel.Succeeded)
        {
            _statusLabel!.Text = $"Parser failed for map '{selected.Name}'. Resolve parser errors before opening this map.";
            return;
        }

        var scenePath = HypeEditor.HypeMapSceneGenerator.GenerateMapScenes(new[] { selected.Name }).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            OpenOrRefreshMapScene(scenePath, selected.Name);
        }
    }

    private void GenerateAllMapScenes()
    {
        if (_currentIndex == null || _statusLabel == null)
        {
            return;
        }

        var levels = _currentIndex.Levels.Select(x => x.LevelName).ToArray();
        var paths = HypeEditor.HypeMapSceneGenerator.GenerateMapScenes(levels);
        var refreshedOpen = TryRefreshOpenGeneratedMap(paths, out var refreshedPath);
        if (refreshedOpen)
        {
            _statusLabel.Text = $"Generated/updated {paths.Count} map scenes. Refreshed open map: {refreshedPath}.";
        }
        else if (paths.Count > 0)
        {
            _statusLabel.Text = $"Generated/updated {paths.Count} map scenes. Example: {paths[0]}";
        }
        else
        {
            _statusLabel.Text = "No map scenes were generated (no level names found).";
        }
    }

    private void RebuildOpenMap()
    {
        if (_editorInterface == null || _statusLabel == null)
        {
            return;
        }

        var root = _editorInterface.GetEditedSceneRoot();
        if (root is HypeMapRoot hypeMapRoot)
        {
            hypeMapRoot.RebuildResolvedView();
            _statusLabel.Text = "Rebuilt resolved view for currently open Hype map.";
            return;
        }

        _statusLabel.Text = "Open a Hype map scene first (root script: HypeMapRoot).";
    }

    private void OnTreeItemSelected()
    {
        if (_assetTree == null)
        {
            return;
        }

        var item = _assetTree.GetSelected();
        if (item == null)
        {
            return;
        }

        var selected = ParseMetadata(item.GetMetadata(0).AsString(), item);
        _selectedMapName = selected.Kind.Equals(HypeContentKind.Map.ToString(), StringComparison.OrdinalIgnoreCase)
            ? selected.Name
            : null;

        if (_openSelectedMapButton != null)
        {
            _openSelectedMapButton.Disabled = string.IsNullOrWhiteSpace(_selectedMapName);
        }

        ShowPreview(selected);
    }

    private void OpenSelectedMap()
    {
        if (_editorInterface == null || string.IsNullOrWhiteSpace(_selectedMapName))
        {
            return;
        }

        if (_currentIndex != null &&
            TryGetParsedLevel(_selectedMapName, out var parsedLevel) &&
            parsedLevel != null &&
            !parsedLevel.Succeeded)
        {
            _statusLabel!.Text = $"Parser failed for map '{_selectedMapName}'. Resolve parser errors before opening this map.";
            return;
        }

        var scenePath = HypeEditor.HypeMapSceneGenerator.GenerateMapScenes(new[] { _selectedMapName }).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            OpenOrRefreshMapScene(scenePath, _selectedMapName);
        }
    }

    private void OpenOrRefreshMapScene(string scenePath, string levelName)
    {
        if (_editorInterface == null)
        {
            return;
        }

        var root = _editorInterface.GetEditedSceneRoot();
        if (root is HypeMapRoot openMap && IsMapRootForLevel(openMap, levelName))
        {
            openMap.RebuildResolvedView();
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"Regenerated and refreshed open map '{NormalizeMapName(levelName)}' ({scenePath}).";
            }

            return;
        }

        _editorInterface.OpenSceneFromPath(scenePath);
        if (_statusLabel != null)
        {
            _statusLabel.Text = $"Opened map scene: {scenePath}";
        }
    }

    private void ShowPreview(SelectedAsset selected)
    {
        switch (selected.Kind)
        {
            case nameof(HypeContentKind.Map):
                ShowMapPreview(selected);
                break;
            case nameof(HypeContentKind.Script):
                ShowScriptPreview(selected);
                break;
            case nameof(HypeContentKind.Animation):
                ShowAnimationPreview(selected);
                break;
            case nameof(HypeContentKind.TextureContainer):
                ShowTextureContainerPreview(selected);
                break;
            case nameof(HypeContentKind.TextureEntry):
                ShowTextureEntryPreview(selected);
                break;
            case nameof(HypeContentKind.ParserLevel):
                ShowParserLevelPreview(selected);
                break;
            case nameof(HypeContentKind.ParserDiagnostic):
                ShowParserDiagnosticPreview(selected);
                break;
            default:
                SetPreview(
                    selected.Name,
                    $"Kind: {selected.Kind}\nVirtual path: {selected.VirtualPath}",
                    null,
                    null);
                break;
        }
    }

    private void ShowMapPreview(SelectedAsset selected)
    {
        var scenePath = ToScenePath(selected.Name);
        var details = $"Level directory: {selected.AbsolutePath}\nScene file: {scenePath}";
        if (!string.IsNullOrWhiteSpace(selected.AuxData))
        {
            details += $"\n\n{selected.AuxData}";
        }

        Texture2D? preview = null;
        if (_currentIndex != null)
        {
            preview = HypeVignettePreviewService.TryGetMapPreview(_currentIndex.GameRoot, selected.Name);
            if (preview == null)
            {
                details += "\nVignette preview: not found";
            }
        }

        SetPreview($"Map: {selected.Name}", details, preview, null);
    }

    private void ShowParserLevelPreview(SelectedAsset selected)
    {
        SetPreview(
            $"Parser Level: {selected.Name}",
            string.IsNullOrWhiteSpace(selected.AuxData) ? "No parser summary available." : selected.AuxData,
            null,
            null);
    }

    private void ShowParserDiagnosticPreview(SelectedAsset selected)
    {
        SetPreview(
            $"Parser Diagnostic: {selected.Name}",
            selected.AuxData,
            null,
            null);
    }

    private void ShowScriptPreview(SelectedAsset selected)
    {
        var details = $"Script path: {selected.AbsolutePath}\nScript type: {selected.AuxData}";
        var preview = ReadTextPreview(selected.AbsolutePath, 120);
        SetPreview($"Script: {selected.Name}", details, null, preview);
    }

    private void ShowAnimationPreview(SelectedAsset selected)
    {
        var details = $"Animation source: {selected.AbsolutePath}\nAnimation id: {selected.AuxData}";
        SetPreview($"Animation: {selected.Name}", details, null, null);
    }

    private void ShowTextureContainerPreview(SelectedAsset selected)
    {
        var details = $"Container path: {selected.AbsolutePath}";
        if (_currentIndex != null)
        {
            var count = _currentIndex.TextureEntries.Count(x =>
                x.ContainerSourceFile.Equals(selected.AbsolutePath, StringComparison.OrdinalIgnoreCase));
            details += $"\nEntries: {count}";
        }

        SetPreview($"Texture Container: {selected.Name}", details, null, null);
    }

    private void ShowTextureEntryPreview(SelectedAsset selected)
    {
        var texture = HypeVignettePreviewService.TryGetTextureByFullName(selected.AbsolutePath, selected.AuxData);
        var details = $"Container: {selected.AbsolutePath}\nEntry: {selected.AuxData}";
        if (texture == null)
        {
            details += "\nDecode preview failed.";
        }

        SetPreview($"Texture: {selected.Name}", details, texture, null);
    }

    private void SetPreview(string title, string details, Texture2D? texture, string? textContent)
    {
        if (_previewTitle == null || _previewDetails == null || _previewTexture == null || _previewText == null)
        {
            return;
        }

        _previewTitle.Text = title;
        _previewDetails.Text = details;
        _previewTexture.Texture = texture;

        var hasText = !string.IsNullOrWhiteSpace(textContent);
        _previewText.Visible = hasText;
        _previewText.Text = hasText ? textContent! : string.Empty;
    }

    private static string ReadTextPreview(string path, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "Script file not found.";
        }

        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();

            for (var i = 0; i < maxLines && !reader.EndOfStream; i++)
            {
                lines.Add(reader.ReadLine() ?? string.Empty);
            }

            if (!reader.EndOfStream)
            {
                lines.Add("... (truncated)");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Could not read script preview: {ex.Message}";
        }
    }

    private static string BuildMetadata(HypeVirtualFileEntry entry)
    {
        return string.Join("|", new[]
        {
            EncodeSegment(entry.Kind.ToString()),
            EncodeSegment(entry.Name),
            EncodeSegment(entry.VirtualPath),
            EncodeSegment(entry.AbsolutePath ?? string.Empty),
            EncodeSegment(entry.AuxData ?? string.Empty)
        });
    }

    private static SelectedAsset ParseMetadata(string metadata, TreeItem item)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return new SelectedAsset
            {
                Kind = item.GetText(1),
                Name = item.GetText(0)
            };
        }

        var segments = metadata.Split('|');
        return new SelectedAsset
        {
            Kind = GetDecodedSegment(segments, 0, item.GetText(1)),
            Name = GetDecodedSegment(segments, 1, item.GetText(0)),
            VirtualPath = GetDecodedSegment(segments, 2, string.Empty),
            AbsolutePath = GetDecodedSegment(segments, 3, string.Empty),
            AuxData = GetDecodedSegment(segments, 4, string.Empty)
        };
    }

    private static string ToScenePath(string levelName)
    {
        var sceneName = NormalizeMapName(levelName);
        return $"res://Scenes/Hype/{sceneName}.tscn";
    }

    private bool TryRefreshOpenGeneratedMap(IReadOnlyList<string> generatedPaths, out string refreshedPath)
    {
        refreshedPath = string.Empty;
        if (_editorInterface == null || generatedPaths.Count == 0)
        {
            return false;
        }

        var root = _editorInterface.GetEditedSceneRoot();
        if (root is not HypeMapRoot openMap)
        {
            return false;
        }

        var openScenePath = NormalizeResPath(root.SceneFilePath);
        if (string.IsNullOrWhiteSpace(openScenePath))
        {
            return false;
        }

        foreach (var path in generatedPaths)
        {
            if (!openScenePath.Equals(NormalizeResPath(path), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            openMap.RebuildResolvedView();
            refreshedPath = path;
            return true;
        }

        return false;
    }

    private static string NormalizeResPath(string path)
    {
        return (path ?? string.Empty).Trim().Replace('\\', '/');
    }

    private static bool IsMapRootForLevel(HypeMapRoot mapRoot, string levelName)
    {
        var currentLevel = NormalizeMapName(ReadMapLevelName(mapRoot));
        var targetLevel = NormalizeMapName(levelName);
        if (string.IsNullOrWhiteSpace(currentLevel) || string.IsNullOrWhiteSpace(targetLevel))
        {
            return false;
        }

        return currentLevel.Equals(targetLevel, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadMapLevelName(HypeMapRoot mapRoot)
    {
        var resource = mapRoot.MapDefinition;
        if (resource == null)
        {
            return string.Empty;
        }

        if (resource is HypeMapDefinition definition)
        {
            return definition.LevelName;
        }

        try
        {
            var value = resource.Get(nameof(HypeMapDefinition.LevelName));
            return value.VariantType == Variant.Type.Nil ? string.Empty : value.AsString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeMapName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        return normalized.ToLowerInvariant();
    }

    private static string GetDecodedSegment(string[] segments, int index, string fallback)
    {
        if (index < 0 || index >= segments.Length)
        {
            return fallback;
        }

        try
        {
            return Uri.UnescapeDataString(segments[index]);
        }
        catch
        {
            return fallback;
        }
    }

    private static string EncodeSegment(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty);
    }

    private bool TryGetParsedLevel(string levelName, out HypeParsedLevelRecord? parsedLevel)
    {
        parsedLevel = _currentIndex?.ParsedLevels
            .FirstOrDefault(x => x.LevelName.Equals(levelName, StringComparison.OrdinalIgnoreCase));
        return parsedLevel != null;
    }
}
