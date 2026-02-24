using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Characters;
using HypeReborn.Hype.Runtime.Parsing;
using HypeReborn.Hype.Runtime.Rendering;

namespace HypeReborn.Hype.Player;

[Tool]
/// <summary>
/// Legacy Montreal actor visual controller.
/// Uses parsed actor channels/frames and maps motor state to frame ranges.
/// </summary>
public partial class HypeCharacterVisualDriver : Node3D, ICharacterVisualController
{
    [Export]
    public string SourceLevelName { get; set; } = string.Empty;

    [Export]
    public string SourceActorId { get; set; } = string.Empty;

    [Export]
    public string SourceActorKey { get; set; } = string.Empty;

    [Export]
    public bool AutoDetectLevelFromScene { get; set; } = true;

    [Export]
    public bool UseSaveActorSelection { get; set; } = true;

    [Export]
    public float ModelScale { get; set; } = 1f;

    [Export]
    public float AnimationSpeedMultiplier { get; set; } = 1f;

    [Export]
    public bool PauseWhenIdle { get; set; }

    [Export]
    public bool UseMovementDrivenAnimation { get; set; } = true;

    [Export]
    public Vector3 VisualOffset { get; set; } = Vector3.Zero;

    [Export]
    public bool AlignVisualToCapsuleBottom { get; set; } = true;

    [Export]
    public float VisualYawOffsetDegrees { get; set; } = -90f;

    [Export]
    public int IdleStartFrame { get; set; } = -1;

    [Export]
    public int IdleEndFrame { get; set; } = -1;

    [Export]
    public int WalkStartFrame { get; set; } = -1;

    [Export]
    public int WalkEndFrame { get; set; } = -1;

    [Export]
    public int RunStartFrame { get; set; } = -1;

    [Export]
    public int RunEndFrame { get; set; } = -1;

    [Export]
    public int JumpStartFrame { get; set; } = -1;

    [Export]
    public int JumpEndFrame { get; set; } = -1;

    [Export]
    public int FallStartFrame { get; set; } = -1;

    [Export]
    public int FallEndFrame { get; set; } = -1;

    [Export]
    public float WalkReferenceSpeed { get; set; } = 4.5f;

    [Export]
    public float RunReferenceSpeed { get; set; } = 7.5f;

    [Export]
    public float IdleSpeedScale { get; set; } = 0f;

    [Export]
    public float WalkSpeedScale { get; set; } = 1f;

    [Export]
    public float RunSpeedScale { get; set; } = 1.2f;

    [Export]
    public float AirSpeedScale { get; set; } = 1f;

    [Export]
    public float PlaybackSmoothingSharpness { get; set; } = 10f;

    private static readonly Dictionary<string, Mesh> CharacterObjectMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CharacterObjectMeshCacheLock = new();

    private Node3D? _visualRoot;
    private RuntimeRig? _runtime;
    private bool _loadAttempted;
    private readonly HypeCharacterAnimator _animator = new();

    public override void _Ready()
    {
        EnsureVisualLoaded();
    }

    /// <inheritdoc />
    public void SetActorSelection(string actorKey, bool persistToSave)
    {
        SourceActorKey = actorKey?.Trim() ?? string.Empty;
        SourceLevelName = string.Empty;
        SourceActorId = string.Empty;
        if (persistToSave && !string.IsNullOrWhiteSpace(SourceActorKey))
        {
            HypePlayerActorSaveState.SetSelectedActorKey(SourceActorKey);
        }

        RebuildVisual();
    }

    /// <summary>
    /// Legacy helper for callers that still resolve actor by level+id pair.
    /// Preferred entry point for new systems is <see cref="SetActorSelection(string,bool)"/>.
    /// </summary>
    public void SetActorSelection(string levelName, string actorId, bool persistToSave)
    {
        SourceActorKey = string.Empty;
        SourceLevelName = levelName?.Trim() ?? string.Empty;
        SourceActorId = actorId?.Trim() ?? string.Empty;
        if (persistToSave && !string.IsNullOrWhiteSpace(SourceLevelName) && !string.IsNullOrWhiteSpace(SourceActorId))
        {
            HypePlayerActorSaveState.SetSelectedActorKey(HypeActorCatalogService.BuildActorKey(SourceLevelName, SourceActorId));
        }

        RebuildVisual();
    }

    /// <inheritdoc />
    public void ApplyState(HypeCharacterMotorState state, float delta)
    {
        EnsureVisualLoaded();
        if (_runtime == null)
        {
            return;
        }

        _animator.SetMotion(ResolveMotionState(state.LocomotionState), state.HorizontalSpeed);
        AdvanceAnimation(delta);
    }

    /// <inheritdoc />
    public void RebuildVisual()
    {
        if (_visualRoot != null)
        {
            RemoveChild(_visualRoot);
            _visualRoot.QueueFree();
            _visualRoot = null;
        }

        _runtime = null;
        _animator.Reset();
        _loadAttempted = false;
        EnsureVisualLoaded();
    }

    private void EnsureVisualLoaded()
    {
        if (_runtime != null || _loadAttempted)
        {
            return;
        }

        _loadAttempted = true;
        if (!TryResolveActorRequest(out var request, out var reason))
        {
            GD.PrintErr($"[HypeCharacterVisual] {reason}");
            return;
        }

        var parsed = string.IsNullOrWhiteSpace(request.ActorId)
            ? HypeMontrealCharacterParser.TryParseMainActor(request.Level, out var actor, out var diagnostics)
            : HypeMontrealCharacterParser.TryParseActor(request.Level, request.ActorId, out actor, out diagnostics);
        if (!parsed || actor == null)
        {
            foreach (var line in diagnostics)
            {
                GD.PrintErr($"[HypeCharacterVisual:{request.Level.LevelName}] {line}");
            }
            return;
        }

        BuildVisual(request.GameRoot, actor);
    }

    private bool TryResolveActorRequest(out HypeActorResolveRequest request, out string failureReason)
    {
        var settings = new HypeActorResolverSettings
        {
            SourceLevelName = SourceLevelName,
            SourceActorId = SourceActorId,
            SourceActorKey = SourceActorKey,
            AutoDetectLevelFromScene = AutoDetectLevelFromScene,
            UseSaveActorSelection = UseSaveActorSelection
        };

        var resolved = HypeActorResolver.TryResolveActorRequest(
            settings,
            ResolveScenePath(),
            out var resolvedRequest,
            out failureReason);
        request = resolved ? resolvedRequest : default;
        return resolved;
    }

    private string? ResolveScenePath()
    {
        var scenePath = GetTree()?.EditedSceneRoot?.SceneFilePath;
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            return scenePath;
        }

        return GetTree()?.CurrentScene?.SceneFilePath;
    }

    private void BuildVisual(string gameRoot, HypeCharacterActorAsset actor)
    {
        _visualRoot = new Node3D
        {
            Name = "VisualRoot",
            Scale = new Vector3(ModelScale, ModelScale, ModelScale)
        };
        AddChild(_visualRoot);
        _visualRoot.Owner = Owner;

        var rigRoot = new Node3D
        {
            Name = "RigRoot",
            Rotation = new Vector3(0f, Mathf.DegToRad(VisualYawOffsetDegrees), 0f)
        };
        _visualRoot.AddChild(rigRoot);
        rigRoot.Owner = Owner;

        var objectVisualByIndex = actor.Objects.ToDictionary(x => x.ObjectIndex, x => x);
        var channels = new ChannelRuntime[actor.ChannelCount];
        for (var channelIndex = 0; channelIndex < actor.ChannelCount; channelIndex++)
        {
            var channelNode = new Node3D { Name = $"Channel_{channelIndex:D2}" };
            rigRoot.AddChild(channelNode);
            channelNode.Owner = Owner;

            var usedObjectIndices = new HashSet<int>();
            foreach (var frame in actor.Frames)
            {
                if (channelIndex < frame.ChannelSamples.Length)
                {
                    var objectIndex = frame.ChannelSamples[channelIndex].ObjectIndex;
                    if (objectIndex >= 0)
                    {
                        usedObjectIndices.Add(objectIndex);
                    }
                }
            }

            var objectNodes = new Dictionary<int, Node3D>();
            foreach (var objectIndex in usedObjectIndices.OrderBy(x => x))
            {
                var objectNode = new Node3D
                {
                    Name = $"Obj_{objectIndex:D3}",
                    Visible = false
                };
                channelNode.AddChild(objectNode);
                objectNode.Owner = Owner;

                if (objectVisualByIndex.TryGetValue(objectIndex, out var visual))
                {
                    objectNode.Scale = visual.ScaleMultiplier;
                    if (visual.Mesh != null)
                    {
                        var mesh = GetOrBuildCharacterObjectMesh(gameRoot, actor, objectIndex, visual.Mesh);
                        if (mesh != null)
                        {
                            var meshNode = new MeshInstance3D
                            {
                                Name = "Mesh",
                                Mesh = mesh
                            };
                            objectNode.AddChild(meshNode);
                            meshNode.Owner = Owner;
                        }
                    }
                }

                objectNodes[objectIndex] = objectNode;
            }

            channels[channelIndex] = new ChannelRuntime
            {
                ChannelNode = channelNode,
                ObjectNodes = objectNodes,
                ActiveObjectIndex = int.MinValue
            };
        }

        _runtime = new RuntimeRig
        {
            Actor = actor,
            RigRoot = rigRoot,
            Channels = channels,
            CurrentFrame = -1
        };

        _animator.Reset();
        ApplyFrame(0, force: true);
        ApplyVisualAlignment();
    }

    private void AdvanceAnimation(float delta)
    {
        if (_runtime == null || _runtime.Actor.Frames.Count == 0)
        {
            return;
        }

        _animator.Advance(
            _runtime.Actor,
            _runtime.CurrentFrame,
            delta,
            BuildAnimationSettings(),
            ApplyFrame);
    }

    private void ApplyFrame(int frameIndex, bool force)
    {
        if (_runtime == null || _runtime.Actor.Frames.Count == 0)
        {
            return;
        }

        frameIndex = Math.Clamp(frameIndex, 0, _runtime.Actor.Frames.Count - 1);
        var frame = _runtime.Actor.Frames[frameIndex];

        for (var channelIndex = 0; channelIndex < _runtime.Channels.Length; channelIndex++)
        {
            var channel = _runtime.Channels[channelIndex];
            var targetParent = _runtime.RigRoot;
            if (channelIndex < frame.ParentChannelIndices.Length)
            {
                var parentIndex = frame.ParentChannelIndices[channelIndex];
                if (parentIndex >= 0 && parentIndex < _runtime.Channels.Length)
                {
                    targetParent = _runtime.Channels[parentIndex].ChannelNode;
                }
            }

            if (channel.ChannelNode.GetParent() != targetParent)
            {
                channel.ChannelNode.Reparent(targetParent, keepGlobalTransform: false);
            }
        }

        for (var channelIndex = 0; channelIndex < _runtime.Channels.Length; channelIndex++)
        {
            var channel = _runtime.Channels[channelIndex];
            if (channelIndex >= frame.ChannelSamples.Length)
            {
                continue;
            }

            var sample = frame.ChannelSamples[channelIndex];
            channel.ChannelNode.Transform = sample.LocalTransform;

            if (force || channel.ActiveObjectIndex != sample.ObjectIndex)
            {
                if (channel.ActiveObjectIndex >= 0 &&
                    channel.ObjectNodes.TryGetValue(channel.ActiveObjectIndex, out var previousNode))
                {
                    previousNode.Visible = false;
                }

                channel.ActiveObjectIndex = sample.ObjectIndex;
                if (sample.ObjectIndex >= 0 && channel.ObjectNodes.TryGetValue(sample.ObjectIndex, out var activeNode))
                {
                    activeNode.Visible = true;
                }
            }
        }

        _runtime.CurrentFrame = frameIndex;
    }

    /// <inheritdoc />
    public void ConfigureSpeedReferences(float walkSpeed, float runSpeed)
    {
        if (walkSpeed > 0f)
        {
            WalkReferenceSpeed = walkSpeed;
        }

        if (runSpeed > 0f)
        {
            RunReferenceSpeed = runSpeed;
        }
    }

    private HypeCharacterAnimationSettings BuildAnimationSettings()
    {
        return new HypeCharacterAnimationSettings
        {
            PauseWhenIdle = PauseWhenIdle,
            UseMovementDrivenAnimation = UseMovementDrivenAnimation,
            AnimationSpeedMultiplier = AnimationSpeedMultiplier,
            PlaybackSmoothingSharpness = PlaybackSmoothingSharpness,
            IdleStartFrame = IdleStartFrame,
            IdleEndFrame = IdleEndFrame,
            WalkStartFrame = WalkStartFrame,
            WalkEndFrame = WalkEndFrame,
            RunStartFrame = RunStartFrame,
            RunEndFrame = RunEndFrame,
            JumpStartFrame = JumpStartFrame,
            JumpEndFrame = JumpEndFrame,
            FallStartFrame = FallStartFrame,
            FallEndFrame = FallEndFrame,
            WalkReferenceSpeed = WalkReferenceSpeed,
            RunReferenceSpeed = RunReferenceSpeed,
            IdleSpeedScale = IdleSpeedScale,
            WalkSpeedScale = WalkSpeedScale,
            RunSpeedScale = RunSpeedScale,
            AirSpeedScale = AirSpeedScale
        };
    }

    private static HypeMotionAnimationState ResolveMotionState(HypeLocomotionState locomotionState)
    {
        return locomotionState switch
        {
            HypeLocomotionState.Walk => HypeMotionAnimationState.Walk,
            HypeLocomotionState.Run => HypeMotionAnimationState.Run,
            HypeLocomotionState.Jump => HypeMotionAnimationState.Jump,
            HypeLocomotionState.Fall => HypeMotionAnimationState.Fall,
            _ => HypeMotionAnimationState.Idle
        };
    }

    private void ApplyVisualAlignment()
    {
        if (_visualRoot == null)
        {
            return;
        }

        var position = VisualOffset;
        if (AlignVisualToCapsuleBottom && TryComputeVisualMinYInOwnerSpace(out var visualMinY))
        {
            var capsuleBottomY = ResolveCapsuleBottomY();
            position.Y += capsuleBottomY - visualMinY;
        }

        _visualRoot.Position = position;
    }

    private bool TryComputeVisualMinYInOwnerSpace(out float minY)
    {
        minY = 0f;
        if (_visualRoot == null)
        {
            return false;
        }

        var found = false;
        var localFromWorld = GlobalTransform.AffineInverse();
        foreach (var meshInstance in EnumerateMeshInstances(_visualRoot))
        {
            if (meshInstance.Mesh == null)
            {
                continue;
            }
            if (!meshInstance.IsVisibleInTree())
            {
                continue;
            }

            var aabb = meshInstance.Mesh.GetAabb();
            foreach (var corner in EnumerateAabbCorners(aabb))
            {
                var world = meshInstance.GlobalTransform * corner;
                var local = localFromWorld * world;
                if (!found || local.Y < minY)
                {
                    minY = local.Y;
                    found = true;
                }
            }
        }

        return found;
    }

    private static IEnumerable<MeshInstance3D> EnumerateMeshInstances(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D meshInstance)
            {
                yield return meshInstance;
            }

            if (child is Node childNode)
            {
                foreach (var nested in EnumerateMeshInstances(childNode))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<Vector3> EnumerateAabbCorners(Aabb aabb)
    {
        var min = aabb.Position;
        var max = aabb.Position + aabb.Size;

        yield return new Vector3(min.X, min.Y, min.Z);
        yield return new Vector3(max.X, min.Y, min.Z);
        yield return new Vector3(min.X, max.Y, min.Z);
        yield return new Vector3(max.X, max.Y, min.Z);
        yield return new Vector3(min.X, min.Y, max.Z);
        yield return new Vector3(max.X, min.Y, max.Z);
        yield return new Vector3(min.X, max.Y, max.Z);
        yield return new Vector3(max.X, max.Y, max.Z);
    }

    private float ResolveCapsuleBottomY()
    {
        var shapeNode = GetParent()?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (shapeNode?.Shape is CapsuleShape3D capsule)
        {
            return -((capsule.Height * 0.5f) + capsule.Radius);
        }

        return 0f;
    }

    private static Mesh? GetOrBuildCharacterObjectMesh(
        string gameRoot,
        HypeCharacterActorAsset actor,
        int objectIndex,
        HypeResolvedMesh meshData)
    {
        var cacheKey = BuildCharacterObjectMeshCacheKey(gameRoot, actor, objectIndex);
        lock (CharacterObjectMeshCacheLock)
        {
            if (CharacterObjectMeshCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var built = HypeMeshBuilder.BuildArrayMesh(
            gameRoot,
            meshData,
            flipWinding: false,
            invertNormals: false,
            preferAuthoredNormals: true);
        if (built == null)
        {
            return null;
        }

        lock (CharacterObjectMeshCacheLock)
        {
            CharacterObjectMeshCache[cacheKey] = built;
        }

        return built;
    }

    private static string BuildCharacterObjectMeshCacheKey(string gameRoot, HypeCharacterActorAsset actor, int objectIndex)
    {
        return $"{gameRoot}::{actor.LevelName}::{actor.ActorId}::obj:{objectIndex}";
    }

    private sealed class RuntimeRig
    {
        public required HypeCharacterActorAsset Actor { get; init; }
        public required Node3D RigRoot { get; init; }
        public required ChannelRuntime[] Channels { get; init; }
        public required int CurrentFrame { get; set; }
    }

    private sealed class ChannelRuntime
    {
        public required Node3D ChannelNode { get; init; }
        public required Dictionary<int, Node3D> ObjectNodes { get; init; }
        public required int ActiveObjectIndex { get; set; }
    }
}
