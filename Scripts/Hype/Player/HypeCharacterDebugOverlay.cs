using System;
using System.Collections.Generic;
using Godot;
using HypeReborn.Hype.Runtime.Characters;

namespace HypeReborn.Hype.Player;

public partial class HypeCharacterDebugOverlay : CanvasLayer
{
    private Label? _stateLabel;
    private Label? _actorStatusLabel;
    private OptionButton? _actorPicker;
    private bool _suppressActorSelection;

    public event Action<string>? ActorSelectionRequested;
    public event Action? ActorCatalogRefreshRequested;

    public override void _Ready()
    {
        var root = new VBoxContainer
        {
            Name = "DebugRoot",
            Position = new Vector2(12f, 12f)
        };
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        _stateLabel = new Label
        {
            Name = "StateLabel",
            Text = "Player debug",
            Modulate = new Color(1f, 0.96f, 0.72f, 1f)
        };
        _stateLabel.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(_stateLabel);

        var actorPanel = new PanelContainer
        {
            Name = "ActorPanel",
            CustomMinimumSize = new Vector2(620f, 0f)
        };
        root.AddChild(actorPanel);

        var actorLayout = new VBoxContainer();
        actorLayout.AddThemeConstantOverride("separation", 6);
        actorPanel.AddChild(actorLayout);

        var title = new Label
        {
            Text = "Debug Actor Picker"
        };
        title.AddThemeFontSizeOverride("font_size", 13);
        actorLayout.AddChild(title);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        actorLayout.AddChild(row);

        _actorPicker = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _actorPicker.ItemSelected += OnActorPickerSelected;
        row.AddChild(_actorPicker);

        var refreshButton = new Button
        {
            Text = "Refresh"
        };
        refreshButton.Pressed += () => ActorCatalogRefreshRequested?.Invoke();
        row.AddChild(refreshButton);

        _actorStatusLabel = new Label
        {
            Text = "Actor catalog not loaded yet.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.95f, 0.92f, 0.78f, 1f)
        };
        actorLayout.AddChild(_actorStatusLabel);
    }

    public void UpdateState(HypeCharacterMotorState state, HypeMovementModelKind movementModel, Vector3 position)
    {
        if (_stateLabel == null)
        {
            return;
        }

        _stateLabel.Text =
            $"Movement: {movementModel}\n" +
            $"State: {state.MovementState}\n" +
            $"Grounded: {state.Grounded}\n" +
            $"Speed: {state.HorizontalSpeed:0.00}\n" +
            $"Vertical: {state.VerticalSpeed:0.00}\n" +
            $"Pos: {position.X:0.00}, {position.Y:0.00}, {position.Z:0.00}";
    }

    public void SetActorOptions(IReadOnlyList<HypeActorRecord> actors, string selectedActorKey)
    {
        if (_actorPicker == null)
        {
            return;
        }

        _suppressActorSelection = true;
        _actorPicker.Clear();

        var selectedIndex = -1;
        var firstSelectableIndex = -1;
        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            _actorPicker.AddItem(actor.DisplayName);
            _actorPicker.SetItemMetadata(i, actor.Key);
            _actorPicker.SetItemDisabled(i, !actor.IsPlayerSelectable);
            if (actor.IsPlayerSelectable && firstSelectableIndex < 0)
            {
                firstSelectableIndex = i;
            }

            if (!string.IsNullOrWhiteSpace(selectedActorKey) &&
                actor.Key.Equals(selectedActorKey, StringComparison.OrdinalIgnoreCase) &&
                actor.IsPlayerSelectable)
            {
                selectedIndex = i;
            }
        }

        if (_actorPicker.ItemCount > 0)
        {
            _actorPicker.Selected = selectedIndex >= 0
                ? selectedIndex
                : (firstSelectableIndex >= 0 ? firstSelectableIndex : 0);
        }

        _suppressActorSelection = false;
    }

    public void SetActorStatus(string status)
    {
        if (_actorStatusLabel != null)
        {
            _actorStatusLabel.Text = status;
        }
    }

    private void OnActorPickerSelected(long selectedIndex)
    {
        if (_suppressActorSelection || _actorPicker == null)
        {
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= _actorPicker.ItemCount)
        {
            return;
        }

        if (_actorPicker.IsItemDisabled((int)selectedIndex))
        {
            return;
        }

        var key = _actorPicker.GetItemMetadata((int)selectedIndex).AsString();
        if (!string.IsNullOrWhiteSpace(key))
        {
            ActorSelectionRequested?.Invoke(key);
        }
    }
}
