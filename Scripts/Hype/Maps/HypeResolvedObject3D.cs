using Godot;

namespace HypeReborn.Hype.Maps;

[Tool]
[GlobalClass]
public partial class HypeResolvedObject3D : Node3D
{
    [Export]
    public string SourceCategory { get; set; } = "LevelObject";

    [Export]
    public string SourceId { get; set; } = string.Empty;

    [Export]
    public bool AutoBuildPlaceholder { get; set; } = true;

    public override void _Ready()
    {
        if (!Engine.IsEditorHint() || !AutoBuildPlaceholder)
        {
            return;
        }

        if (GetNodeOrNull<MeshInstance3D>("Preview") != null)
        {
            return;
        }

        var preview = new MeshInstance3D
        {
            Name = "Preview",
            Mesh = BuildPreviewMesh()
        };
        AddChild(preview);
        preview.Owner = Owner;
    }

    private Mesh BuildPreviewMesh()
    {
        return SourceCategory switch
        {
            "Geometry" => new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            "ParticleSource" => new SphereMesh { Radius = 0.25f, Height = 0.5f },
            "Light" => new SphereMesh { Radius = 0.2f, Height = 0.4f },
            _ => new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) }
        };
    }
}
