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
    public bool ShowOrientationArrows { get; set; } = true;

    [Export]
    public bool AutoRotateToMovement { get; set; } = true;

    [Export]
    public float RotationSharpness { get; set; } = 16f;

    [Export]
    public float TurnRateDegreesPerSecond { get; set; } = 900f;

    [Export]
    public NodePath VisualControllerPath { get; set; } = new("VisualDriver");

    [Export]
    public HypeMovementModelKind MovementModel { get; set; } = HypeMovementModelKind.Classic;

    [Export]
    public bool AllowMovementModelToggle { get; set; } = true;

    [Export]
    public bool ControlledByPlayerController { get; set; }

    private readonly HypeCharacterMotor _motor = new();
    private readonly HypeCharacterCommandBus _commandBus = new();
    private HypePlayerCameraRig? _fallbackCameraRig;
    private HypeCharacterDebugOverlay? _debugOverlay;
    private HypeCharacterOrientationDebugArrows? _orientationArrows;
    private ICharacterVisualController? _visualController;

    public override void _Ready()
    {
        CharacterDefinition ??= new HypeCharacterDefinition();
        EnsureCollisionShape(CharacterDefinition);
        if (!ControlledByPlayerController)
        {
            EnsureCommandSource();
            EnsureFallbackCameraRig();
        }
        EnsureVisualController();
        _visualController?.ConfigureSpeedReferences(CharacterDefinition.WalkSpeed, CharacterDefinition.SprintSpeed);
        EnsureDebugOverlay();
        EnsureOrientationArrows();
        _motor.SetMovementModel(MovementModel);
        EnsureDefaultPlayerActorSelection();
        ConfigureDebugActorPicker();
        SyncFacingFromCurrentRotation();
        _visualController?.RebuildVisual();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (ControlledByPlayerController)
        {
            return;
        }

        var command = _commandBus.Poll();
        _fallbackCameraRig?.ConsumeLookInput(command.LookInput);
        var moveBasis = _fallbackCameraRig?.GetMovementBasis() ?? GlobalBasis;
        SimulateCharacter(command, moveBasis, (float)delta);
    }

    /// <summary>
    /// Executes one movement/animation step from an external player controller.
    /// </summary>
    public void StepFromPlayerController(in HypeCharacterCommand command, Basis movementBasis, float delta)
    {
        SimulateCharacter(command, movementBasis, delta);
    }

    private void SimulateCharacter(in HypeCharacterCommand command, Basis movementBasis, float delta)
    {
        HandleMovementModelToggle(command);
        _motor.Step(
            body: this,
            definition: CharacterDefinition!,
            movementBasis: movementBasis,
            command: command,
            delta: delta);

        if (AutoRotateToMovement)
        {
            RotateTowardMovement(delta);
        }
        else
        {
            SyncFacingFromCurrentRotation();
        }

        _visualController?.ApplyState(_motor.State, delta);
        _debugOverlay?.UpdateState(_motor.State, _motor.MovementModel, GlobalPosition);
        _orientationArrows?.UpdateDirections(_motor.State);
    }

    private void HandleMovementModelToggle(in HypeCharacterCommand command)
    {
        if (!AllowMovementModelToggle || !command.ToggleMovementModelPressed)
        {
            return;
        }

        var next = _motor.MovementModel == HypeMovementModelKind.Classic
            ? HypeMovementModelKind.Modern
            : HypeMovementModelKind.Classic;
        _motor.SetMovementModel(next);
        MovementModel = next;
        GD.Print($"[HypeCharacterRoot] Movement model switched to {next}.");
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

    private void EnsureFallbackCameraRig()
    {
        _fallbackCameraRig = GetNodeOrNull<HypePlayerCameraRig>("CameraRig");
        if (_fallbackCameraRig == null)
        {
            _fallbackCameraRig = new HypePlayerCameraRig { Name = "CameraRig" };
            AddChild(_fallbackCameraRig);
            _fallbackCameraRig.Owner = Owner;
        }

        _fallbackCameraRig.BindTarget(this);
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

    private void EnsureOrientationArrows()
    {
        if (!ShowOrientationArrows)
        {
            return;
        }

        _orientationArrows = GetNodeOrNull<HypeCharacterOrientationDebugArrows>("OrientationDebugArrows");
        if (_orientationArrows == null)
        {
            _orientationArrows = new HypeCharacterOrientationDebugArrows
            {
                Name = "OrientationDebugArrows"
            };
            AddChild(_orientationArrows);
            _orientationArrows.Owner = Owner;
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
        _visualController?.SetActorSelection(actorKey, persistToSave: false);
        _debugOverlay?.SetActorStatus($"Active player actor: {actorKey}");
    }

    /// <summary>
    /// Resolves the active visual controller.
    /// Resolution order: explicit path, existing controller in children, then legacy fallback creation.
    /// </summary>
    private void EnsureVisualController()
    {
        if (!VisualControllerPath.IsEmpty)
        {
            var nodeAtPath = GetNodeOrNull<Node>(VisualControllerPath);
            if (nodeAtPath is ICharacterVisualController explicitController)
            {
                _visualController = explicitController;
                return;
            }

            if (nodeAtPath != null)
            {
                GD.PrintErr($"[HypeCharacterRoot] Node at '{VisualControllerPath}' does not implement ICharacterVisualController.");
            }
        }

        var discovered = FindFirstVisualController(this);
        if (discovered != null)
        {
            _visualController = discovered;
            return;
        }

        var legacy = GetNodeOrNull<HypeCharacterVisualDriver>("VisualDriver");
        if (legacy == null)
        {
            legacy = new HypeCharacterVisualDriver { Name = "VisualDriver" };
            AddChild(legacy);
            legacy.Owner = Owner;
        }

        _visualController = legacy;
    }

    private void RotateTowardMovement(float delta)
    {
        var direction = _motor.State.VelocityWorldDirection;
        if (direction.LengthSquared() <= 0.0001f)
        {
            SyncFacingFromCurrentRotation();
            return;
        }

        // Character facing uses Godot forward (-Z), so yaw must be solved from (x, -z).
        var targetYaw = Mathf.Atan2(direction.X, -direction.Z);
        var maxStep = Mathf.DegToRad(Mathf.Max(30f, TurnRateDegreesPerSecond)) * delta;
        var blend = 1f - Mathf.Exp(-Mathf.Max(1f, RotationSharpness) * delta);
        var smoothedYaw = Mathf.LerpAngle(Rotation.Y, targetYaw, blend);
        var nextYaw = MoveTowardAngle(Rotation.Y, smoothedYaw, maxStep);
        Rotation = new Vector3(
            Rotation.X,
            nextYaw,
            Rotation.Z);
        SyncFacingFromCurrentRotation();
    }

    private void SyncFacingFromCurrentRotation()
    {
        var yaw = Rotation.Y;
        // Yaw=0 should face forward in Godot space (-Z).
        var facing = new Vector3(Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw)).Normalized();
        if (facing.LengthSquared() <= float.Epsilon)
        {
            facing = Vector3.Forward;
        }

        _motor.State.FacingDirection = facing;
        if (_motor.State.DesiredWorldDirection.LengthSquared() <= 0.001f)
        {
            _motor.State.AttackAimDirection = facing;
        }
    }

    public Vector3 ResolveAttackDirection()
    {
        var direction = _motor.State.AttackAimDirection;
        return direction.LengthSquared() > 0.001f ? direction.Normalized() : _motor.State.FacingDirection;
    }

    private static float MoveTowardAngle(float from, float to, float maxDelta)
    {
        var delta = Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(delta) <= maxDelta)
        {
            return to;
        }

        return from + Mathf.Sign(delta) * maxDelta;
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

    private static ICharacterVisualController? FindFirstVisualController(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is ICharacterVisualController visualController)
            {
                return visualController;
            }

            if (child is Node childNode)
            {
                var nested = FindFirstVisualController(childNode);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
