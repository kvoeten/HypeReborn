using Godot;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.Maps;

[Tool]
[GlobalClass]
public partial class HypeMapRoot : Node3D
{
    private const string ResolvedRootNodeName = "__HypeResolved";
    private const uint TextureFlagColorKeyMask = 0x902u;
    private const uint TextureFlagTransparentMask = 0xAu;
    private const uint VisualMaterialTransparentFlag = 1u << 3;

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

        var definition = ResolveDefinition(MapDefinition);
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

        BuildResolvedEntities(root, resolved.GameRoot, resolved.Entities);

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
        string gameRoot,
        System.Collections.Generic.IReadOnlyList<HypeResolvedEntity> entities)
    {
        var geoRoot = CreateGroupRoot(root, "GeometryObjects");
        var lightRoot = CreateGroupRoot(root, "Lights");
        var fxRoot = CreateGroupRoot(root, "ParticleSources");
        var animRoot = CreateGroupRoot(root, "AnimationAnchors");

        foreach (var entity in entities)
        {
            switch (entity.Kind)
            {
                case HypeResolvedEntityKind.Geometry:
                    CreateGeometryNode(geoRoot, gameRoot, entity);
                    break;
                case HypeResolvedEntityKind.Light:
                    CreateLightPlaceholder(lightRoot, entity);
                    break;
                case HypeResolvedEntityKind.ParticleSource:
                    CreateParticlePlaceholder(fxRoot, entity);
                    break;
                case HypeResolvedEntityKind.AnimationAnchor:
                    CreateAnimationAnchor(animRoot, entity);
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

    private void CreateGeometryNode(Node3D parent, string gameRoot, HypeResolvedEntity entity)
    {
        var node = new HypeResolvedObject3D
        {
            Name = entity.Name,
            SourceCategory = "Geometry",
            SourceId = entity.Id,
            AutoBuildPlaceholder = false,
            Transform = entity.Transform
        };
        parent.AddChild(node);
        node.Owner = Owner;

        if (entity.Mesh == null || entity.Mesh.Surfaces.Count == 0)
        {
            var preview = new MeshInstance3D
            {
                Name = "Preview",
                Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) }
            };
            node.AddChild(preview);
            preview.Owner = Owner;
            return;
        }

        var mesh = BuildArrayMesh(gameRoot, entity.Mesh, entity.FlipWinding);
        if (mesh == null)
        {
            var preview = new MeshInstance3D
            {
                Name = "Preview",
                Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) }
            };
            node.AddChild(preview);
            preview.Owner = Owner;
            return;
        }

        var meshNode = new MeshInstance3D
        {
            Name = "ResolvedMesh",
            Mesh = mesh
        };
        node.AddChild(meshNode);
        meshNode.Owner = Owner;
    }

    private ArrayMesh? BuildArrayMesh(string gameRoot, HypeResolvedMesh meshData, bool flipWinding)
    {
        if (meshData.Surfaces.Count == 0)
        {
            return null;
        }

        var mesh = new ArrayMesh();
        for (var i = 0; i < meshData.Surfaces.Count; i++)
        {
            var surface = meshData.Surfaces[i];
            if (surface.Vertices.Length == 0 || surface.Indices.Length == 0)
            {
                continue;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = surface.Vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = surface.Uvs;
            arrays[(int)Mesh.ArrayType.Normal] = BuildSurfaceNormals(surface, flipWinding);
            arrays[(int)Mesh.ArrayType.Index] = flipWinding
                ? FlipTriangleWinding(surface.Indices)
                : surface.Indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var surfaceIndex = mesh.GetSurfaceCount() - 1;

            var material = BuildSurfaceMaterial(gameRoot, surface);
            if (material != null)
            {
                mesh.SurfaceSetMaterial(surfaceIndex, material);
            }
        }

        return mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    private static int[] FlipTriangleWinding(int[] source)
    {
        if (source.Length < 3)
        {
            return source;
        }

        var output = new int[source.Length];
        System.Array.Copy(source, output, source.Length);

        for (var i = 0; i + 2 < output.Length; i += 3)
        {
            (output[i + 1], output[i + 2]) = (output[i + 2], output[i + 1]);
        }

        return output;
    }

    private static Vector3[] InvertNormals(Vector3[] source)
    {
        if (source.Length == 0)
        {
            return source;
        }

        var output = new Vector3[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            output[i] = -source[i];
        }

        return output;
    }

    private Vector3[] BuildSurfaceNormals(HypeResolvedMeshSurface surface, bool flipWinding)
    {
        var normals = BuildSmoothNormals(surface.Vertices, surface.Indices);
        if (normals.Length == 0 && surface.Normals != null && surface.Normals.Length == surface.Vertices.Length)
        {
            normals = NormalizeNormals(surface.Normals);
        }

        if (flipWinding)
        {
            normals = InvertNormals(normals);
        }

        if (InvertResolvedNormals)
        {
            normals = InvertNormals(normals);
        }

        return normals;
    }

    private static Vector3[] NormalizeNormals(Vector3[] source)
    {
        var output = new Vector3[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var normal = source[i];
            output[i] = normal.LengthSquared() > float.Epsilon ? normal.Normalized() : Vector3.Up;
        }

        return output;
    }

    private static Vector3[] BuildSmoothNormals(Vector3[] vertices, int[] indices)
    {
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i + 0];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
            {
                continue;
            }

            var face = (vertices[i1] - vertices[i0]).Cross(vertices[i2] - vertices[i0]);
            if (face.LengthSquared() <= float.Epsilon)
            {
                continue;
            }

            normals[i0] += face;
            normals[i1] += face;
            normals[i2] += face;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > float.Epsilon ? normals[i].Normalized() : Vector3.Up;
        }

        return normals;
    }

    private StandardMaterial3D? BuildSurfaceMaterial(string gameRoot, HypeResolvedMeshSurface surface)
    {
        var useColorKey = (surface.TextureFlags & TextureFlagColorKeyMask) != 0;
        var wantsTransparency =
            useColorKey ||
            (surface.TextureFlags & TextureFlagTransparentMask) != 0 ||
            (surface.VisualMaterialFlags & VisualMaterialTransparentFlag) != 0;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 1f),
            Roughness = 1f,
            CullMode = surface.DoubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly
        };

        if (!string.IsNullOrWhiteSpace(surface.TextureTgaName))
        {
            var textureResult = HypeTextureLookupService.TryGetTextureByTgaNameDetailed(
                gameRoot,
                surface.TextureTgaName,
                surface.TextureFlags,
                surface.TextureAlphaMask);
            var texture = textureResult.Texture;
            if (texture != null)
            {
                material.AlbedoTexture = texture;
                var enableTransparency = useColorKey || (wantsTransparency && textureResult.HasAnyTransparency);
                if (enableTransparency)
                {
                    if (useColorKey || !textureResult.HasPartialTransparency)
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                        material.AlphaScissorThreshold = useColorKey ? 0.5f : 0.01f;
                    }
                    else
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                        material.AlphaScissorThreshold = 0f;
                    }
                    material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
                }
            }
        }

        return material;
    }

    private void CreateLightPlaceholder(Node3D parent, HypeResolvedEntity entity)
    {
        var light = new OmniLight3D
        {
            Name = entity.Name,
            Transform = entity.Transform,
            LightColor = new Color(1f, 0.95f, 0.8f),
            LightEnergy = 1.3f,
            OmniRange = 8f
        };
        parent.AddChild(light);
        light.Owner = Owner;
    }

    private void CreateParticlePlaceholder(Node3D parent, HypeResolvedEntity entity)
    {
        var particles = new GpuParticles3D
        {
            Name = entity.Name,
            Transform = entity.Transform,
            Amount = 32,
            Lifetime = 1.2f,
            OneShot = false,
            Emitting = true,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.2f, 0.2f) },
            ProcessMaterial = new ParticleProcessMaterial
            {
                Gravity = new Vector3(0f, -0.1f, 0f),
                InitialVelocityMin = 0.2f,
                InitialVelocityMax = 0.6f
            }
        };
        parent.AddChild(particles);
        particles.Owner = Owner;
    }

    private void CreateAnimationAnchor(Node3D parent, HypeResolvedEntity entity)
    {
        var anchor = new Node3D
        {
            Name = entity.Name,
            Transform = entity.Transform
        };
        parent.AddChild(anchor);
        anchor.Owner = Owner;

        var label = new Label3D
        {
            Name = "Label",
            Position = new Vector3(0f, 0.6f, 0f),
            Text = entity.Name
        };
        anchor.AddChild(label);
        label.Owner = Owner;
    }

    private static HypeMapDefinition? ResolveDefinition(Resource? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is HypeMapDefinition typed)
        {
            return typed;
        }

        // Legacy scenes can reference a generic Resource; remap known properties.
        return new HypeMapDefinition
        {
            LevelName = ReadString(resource, nameof(HypeMapDefinition.LevelName)),
            ExternalGameRootOverride = ReadString(resource, nameof(HypeMapDefinition.ExternalGameRootOverride)),
            LoadLanguageLayer = ReadBool(resource, nameof(HypeMapDefinition.LoadLanguageLayer), true),
            LoadScripts = ReadBool(resource, nameof(HypeMapDefinition.LoadScripts), true),
            LoadAnimationCatalog = ReadBool(resource, nameof(HypeMapDefinition.LoadAnimationCatalog), true),
            DesignerNotes = ReadString(resource, nameof(HypeMapDefinition.DesignerNotes))
        };
    }

    private static string ReadString(Resource resource, string propertyName, string fallback = "")
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsString();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Resource resource, string propertyName, bool fallback)
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsBool();
        }
        catch
        {
            return fallback;
        }
    }
}
