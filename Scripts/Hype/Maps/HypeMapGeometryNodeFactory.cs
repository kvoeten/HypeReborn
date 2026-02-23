using Godot;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Rendering;

namespace HypeReborn.Hype.Maps;

public static class HypeMapGeometryNodeFactory
{
    public static void Create(Node3D parent, Node? owner, string gameRoot, HypeResolvedEntity entity, bool invertNormals)
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
        node.Owner = owner;

        var mesh = entity.Mesh == null || entity.Mesh.Surfaces.Count == 0
            ? null
            : HypeMeshBuilder.BuildArrayMesh(
                gameRoot,
                entity.Mesh,
                flipWinding: entity.FlipWinding,
                invertNormals: invertNormals,
                preferAuthoredNormals: false);

        Mesh meshResource;
        if (mesh != null)
        {
            meshResource = mesh;
        }
        else
        {
            meshResource = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) };
        }
        var preview = new MeshInstance3D
        {
            Name = mesh == null ? "Preview" : "ResolvedMesh",
            Mesh = meshResource
        };
        node.AddChild(preview);
        preview.Owner = owner;
    }
}
