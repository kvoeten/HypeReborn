using Godot;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.Maps;

[Tool]
[GlobalClass]
public partial class HypeMapRoot : Node3D
{
    private const string ResolvedRootNodeName = "__HypeResolved";

    [Export]
    public Resource? MapDefinition { get; set; }

    [Export]
    public bool AutoRefreshInEditor { get; set; } = true;

    [Export]
    public bool InvertResolvedNormals { get; set; }

    public override void _Ready()
    {
        if (Engine.IsEditorHint() && AutoRefreshInEditor)
        {
            RebuildResolvedView();
        }
    }

    public void RebuildResolvedView()
    {
        RemoveResolvedNode();

        var definition = HypeMapDefinitionSerializer.Resolve(MapDefinition);
        if (definition == null || string.IsNullOrWhiteSpace(definition.LevelName))
        {
            return;
        }

        var resolvedRoot = new Node3D { Name = ResolvedRootNodeName };
        AddChild(resolvedRoot);
        resolvedRoot.Owner = Owner;

        try
        {
            var resolved = HypeAssetResolver.ResolveLevel(definition);
            BuildResolvedPreview(resolvedRoot, resolved);
        }
        catch (System.Exception ex)
        {
            LogResolveException(definition.LevelName, ex);
            BuildErrorPreview(resolvedRoot, ex.Message);
        }
    }

    private void BuildResolvedPreview(Node3D root, HypeResolvedLevel resolved)
    {
        var banner = new Label3D
        {
            Name = "LevelBanner",
            Position = new Vector3(0f, 2f, 0f),
            Text = $"{resolved.Level.LevelName} | scripts={resolved.Scripts.Count} | animBanks={resolved.Animations.Count} | entities={resolved.Entities.Count}"
        };
        root.AddChild(banner);
        banner.Owner = Owner;

        var mapPreview = HypeVignettePreviewService.TryGetMapPreview(resolved.GameRoot, resolved.Level.LevelName);
        if (mapPreview != null)
        {
            var previewSprite = new Sprite3D
            {
                Name = "MapPreview",
                Texture = mapPreview,
                Position = new Vector3(0f, 4f, 0f),
                PixelSize = 0.01f
            };
            root.AddChild(previewSprite);
            previewSprite.Owner = Owner;
        }

        BuildResolvedEntities(root, resolved.Level.LevelName, resolved.GameRoot, resolved.Entities);

        var meta = new Node3D { Name = "ResolvedMetadata" };
        meta.SetMeta("level_name", resolved.Level.LevelName);
        meta.SetMeta("game_root", resolved.GameRoot);
        meta.SetMeta("language", resolved.Language);
        meta.SetMeta("script_count", resolved.Scripts.Count);
        meta.SetMeta("animation_count", resolved.Animations.Count);
        meta.SetMeta("entity_count", resolved.Entities.Count);
        root.AddChild(meta);
        meta.Owner = Owner;
    }

    private void BuildErrorPreview(Node3D root, string message)
    {
        var label = new Label3D
        {
            Name = "ResolveError",
            Position = new Vector3(0f, 2f, 0f),
            Text = $"Hype resolve error: {message}\n(see Output log)"
        };
        root.AddChild(label);
        label.Owner = Owner;
    }

    private static void LogResolveException(string levelName, System.Exception ex)
    {
        var prefix = string.IsNullOrWhiteSpace(levelName)
            ? "[HypeMapRoot]"
            : $"[HypeMapRoot:{levelName}]";

        GD.PushError($"{prefix} Resolve failed. See Output for details.");
        GD.PrintErr($"{prefix} Resolve failed.");
        GD.PrintErr(ex.ToString());
    }

    private void RemoveResolvedNode()
    {
        var existing = GetNodeOrNull<Node>(ResolvedRootNodeName);
        existing?.QueueFree();
    }

    private void BuildResolvedEntities(
        Node3D root,
        string levelName,
        string gameRoot,
        System.Collections.Generic.IReadOnlyList<HypeResolvedEntity> entities)
    {
        var geoRoot = CreateGroupRoot(root, "GeometryObjects");
        var lightRoot = CreateGroupRoot(root, "Lights");
        var fxRoot = CreateGroupRoot(root, "ParticleSources");
        var animRoot = CreateGroupRoot(root, "AnimationAnchors");
        var actorRoot = CreateGroupRoot(root, "Actors");

        foreach (var entity in entities)
        {
            switch (entity.Kind)
            {
                case HypeResolvedEntityKind.Geometry:
                    HypeMapGeometryNodeFactory.Create(geoRoot, Owner, gameRoot, entity, InvertResolvedNormals);
                    break;
                case HypeResolvedEntityKind.Light:
                    HypeMapPlaceholderFactory.CreateLight(lightRoot, Owner, entity);
                    break;
                case HypeResolvedEntityKind.ParticleSource:
                    HypeMapPlaceholderFactory.CreateParticleSource(fxRoot, Owner, entity);
                    break;
                case HypeResolvedEntityKind.AnimationAnchor:
                    HypeMapPlaceholderFactory.CreateAnimationAnchor(animRoot, Owner, entity);
                    break;
                case HypeResolvedEntityKind.Actor:
                    HypeMapActorNodeFactory.Create(actorRoot, Owner, levelName, entity);
                    break;
            }
        }
    }

    private Node3D CreateGroupRoot(Node3D parent, string name)
    {
        var root = new Node3D { Name = name };
        parent.AddChild(root);
        root.Owner = Owner;
        return root;
    }

}
