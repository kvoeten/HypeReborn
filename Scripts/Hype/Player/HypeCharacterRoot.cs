using Godot;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Characters;
using System;
using System.Linq;

namespace HypeReborn.Hype.Player;

public partial class HypeCharacterRoot : CharacterBody3D
{
    [Export]
    public HypeCharacterDefinition? CharacterDefinition { get; set; }

    [Export]
    public bool ShowDebugOverlay { get; set; } = true;

    [Export]
    public bool AutoRotateToMovement { get; set; } = true;

    [Export]
    public float RotationSharpness { get; set; } = 16f;

    private readonly HypeCharacterMotor _motor = new();
    private readonly HypeCharacterCommandBus _commandBus = new();
    private HypePlayerCameraRig? _cameraRig;
    private HypeCharacterDebugOverlay? _debugOverlay;
    private HypeCharacterVisualDriver? _visualDriver;

    public override void _Ready()
    {
        CharacterDefinition ??= new HypeCharacterDefinition();
        EnsureCollisionShape(CharacterDefinition);
        EnsureCommandSource();
        EnsureCameraRig();
        EnsureVisualDriver();
        _visualDriver?.ConfigureSpeedReferences(CharacterDefinition.WalkSpeed, CharacterDefinition.SprintSpeed);
        EnsureDebugOverlay();
        EnsureDefaultPlayerActorSelection();
        ConfigureDebugActorPicker();
        _visualDriver?.RebuildVisual();
    }

    public override void _PhysicsProcess(double delta)
    {
        var command = _commandBus.Poll();
        _cameraRig?.ConsumeLookInput(command.LookInput);

        var moveBasis = _cameraRig?.GetMovementBasis() ?? GlobalBasis;
        _motor.Step(
            body: this,
            definition: CharacterDefinition!,
            movementBasis: moveBasis,
            command: command,
            delta: (float)delta);

        if (AutoRotateToMovement)
        {
            RotateTowardMovement((float)delta);
        }

        _visualDriver?.ApplyState(_motor.State, (float)delta);
        _debugOverlay?.UpdateState(_motor.State, GlobalPosition);
    }

    private void EnsureCollisionShape(HypeCharacterDefinition definition)
    {
        var shapeNode = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (shapeNode == null)
        {
            shapeNode = new CollisionShape3D { Name = "CollisionShape3D" };
            AddChild(shapeNode);
            shapeNode.Owner = Owner;
        }

        shapeNode.Shape = new CapsuleShape3D
        {
            Radius = definition.CapsuleRadius,
            Height = definition.CapsuleHeight
        };
    }

    private void EnsureCommandSource()
    {
        var source = FindFirstCommandSource(this);
        if (source == null)
        {
            var adapter = new HypeCharacterInputAdapter { Name = "InputAdapter" };
            AddChild(adapter);
            adapter.Owner = Owner;
            source = adapter;
        }

        _commandBus.SetSource(source);
    }

    private void EnsureCameraRig()
    {
        _cameraRig = GetNodeOrNull<HypePlayerCameraRig>("CameraRig");
        if (_cameraRig == null)
        {
            _cameraRig = new HypePlayerCameraRig { Name = "CameraRig" };
            AddChild(_cameraRig);
            _cameraRig.Owner = Owner;
        }

        _cameraRig.BindTarget(this);
    }

    private void EnsureDebugOverlay()
    {
        if (!ShowDebugOverlay)
        {
            return;
        }

        _debugOverlay = GetNodeOrNull<HypeCharacterDebugOverlay>("DebugOverlay");
        if (_debugOverlay == null)
        {
            _debugOverlay = new HypeCharacterDebugOverlay { Name = "DebugOverlay" };
            AddChild(_debugOverlay);
            _debugOverlay.Owner = Owner;
        }
    }

    private void EnsureDefaultPlayerActorSelection()
    {
        if (!HypeActorCatalogService.TryBuildCatalog(out var catalog, out var failureReason))
        {
            GD.PrintErr($"[HypeCharacterRoot] Actor catalog load failed: {failureReason}");
            return;
        }

        var selectedKey = HypePlayerActorSaveState.GetSelectedActorKey();
        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            var selectedActor = catalog.PlayableActors.FirstOrDefault(x =>
                x.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
            if (selectedActor != null && selectedActor.Category == HypeActorCategory.Hero)
            {
                return;
            }

            HypePlayerActorSaveState.ClearSelectedActorKey();
        }

        HypePlayerActorSaveState.EnsureDefaultSelection(catalog);
    }

    private void ConfigureDebugActorPicker()
    {
        if (_debugOverlay == null)
        {
            return;
        }

        _debugOverlay.ActorSelectionRequested -= OnDebugActorSelectionRequested;
        _debugOverlay.ActorCatalogRefreshRequested -= OnDebugActorCatalogRefreshRequested;
        _debugOverlay.ActorSelectionRequested += OnDebugActorSelectionRequested;
        _debugOverlay.ActorCatalogRefreshRequested += OnDebugActorCatalogRefreshRequested;

        RefreshDebugActorCatalog();
    }

    private void OnDebugActorCatalogRefreshRequested()
    {
        HypeActorCatalogService.InvalidateCache();
        RefreshDebugActorCatalog();
    }

    private void RefreshDebugActorCatalog()
    {
        if (_debugOverlay == null)
        {
            return;
        }

        if (!HypeActorCatalogService.TryBuildCatalog(out var catalog, out var failureReason))
        {
            _debugOverlay.SetActorStatus($"Actor catalog failed: {failureReason}");
            return;
        }

        var selectedKey = HypePlayerActorSaveState.GetSelectedActorKey();
        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            var selectedActor = catalog.PlayableActors.FirstOrDefault(x =>
                x.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
            if (selectedActor == null || selectedActor.Category != HypeActorCategory.Hero)
            {
                HypePlayerActorSaveState.ClearSelectedActorKey();
                HypePlayerActorSaveState.EnsureDefaultSelection(catalog);
                selectedKey = HypePlayerActorSaveState.GetSelectedActorKey();
            }
        }

        var pickerActors = HypeActorCatalogService.BuildPlayableModelRepresentatives(catalog);
        var pickerSelectedKey = HypeActorCatalogService.ResolvePlayableRepresentativeKey(catalog, selectedKey);
        _debugOverlay.SetActorOptions(pickerActors, pickerSelectedKey);
        var heroCount = catalog.Actors.Count(x => x.Category == HypeActorCategory.Hero);
        var npcCount = catalog.Actors.Count(x => x.Category == HypeActorCategory.Npc);
        var enemyCount = catalog.Actors.Count(x => x.Category == HypeActorCategory.Enemy);
        var levelActorCount = catalog.Actors.Count(x => x.Category == HypeActorCategory.LevelActor);
        _debugOverlay.SetActorStatus(
            $"Showing unique playable models ({pickerActors.Count} of {catalog.PlayableActors.Count} entries) | Hero: {heroCount} | NPC: {npcCount} | Enemy: {enemyCount} | LevelActor: {levelActorCount}");
    }

    private void OnDebugActorSelectionRequested(string actorKey)
    {
        if (!HypeActorCatalogService.TryParseActorKey(actorKey, out var levelName, out var actorId))
        {
            _debugOverlay?.SetActorStatus($"Invalid actor key: {actorKey}");
            return;
        }

        try
        {
            var index = HypeAssetResolver.BuildIndex();
            if (!HypeActorCatalogService.TryFindLevel(index, levelName, out _))
            {
                _debugOverlay?.SetActorStatus($"Actor level not found: {levelName}");
                return;
            }

            if (HypeActorCatalogService.TryBuildCatalog(out var catalog, out _) &&
                !catalog.Actors.Any(x =>
                    x.Key.Equals(actorKey, StringComparison.OrdinalIgnoreCase) &&
                    x.IsPlayerSelectable))
            {
                _debugOverlay?.SetActorStatus($"Actor is locked for player use: {actorKey}");
                return;
            }
        }
        catch (System.Exception ex)
        {
            _debugOverlay?.SetActorStatus($"Validation failed: {ex.Message}");
            return;
        }

        HypePlayerActorSaveState.SetSelectedActorKey(actorKey);
        _visualDriver?.SetActorSelection(levelName, actorId, persistToSave: false);
        _debugOverlay?.SetActorStatus($"Active player actor: {actorKey}");
    }

    private void EnsureVisualDriver()
    {
        _visualDriver = GetNodeOrNull<HypeCharacterVisualDriver>("VisualDriver");
        if (_visualDriver == null)
        {
            _visualDriver = new HypeCharacterVisualDriver { Name = "VisualDriver" };
            AddChild(_visualDriver);
            _visualDriver.Owner = Owner;
        }
    }

    private void RotateTowardMovement(float delta)
    {
        var direction = _motor.State.DesiredWorldDirection;
        if (direction.LengthSquared() <= 0.0001f)
        {
            return;
        }

        var targetYaw = Mathf.Atan2(direction.X, direction.Z);
        var blend = 1f - Mathf.Exp(-RotationSharpness * delta);
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(Rotation.Y, targetYaw, blend),
            Rotation.Z);
    }

    private static ICharacterCommandSource? FindFirstCommandSource(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is ICharacterCommandSource source)
            {
                return source;
            }
        }

        return null;
    }
}
