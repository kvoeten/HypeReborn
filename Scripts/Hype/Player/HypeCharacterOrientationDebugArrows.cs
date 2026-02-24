using Godot;

namespace HypeReborn.Hype.Player;

/// <summary>
/// Lightweight runtime gizmo that renders movement/facing vectors as 3D arrows.
/// Intended for sandbox/debug scenes to inspect orientation and velocity behavior.
/// </summary>
[Tool]
public partial class HypeCharacterOrientationDebugArrows : Node3D
{
    [Export]
    public float LineLength { get; set; } = 1.2f;

    [Export]
    public float HeadLength { get; set; } = 0.22f;

    [Export]
    public float HeadWidth { get; set; } = 0.11f;

    [Export]
    public float HeightOffset { get; set; } = 1.1f;

    private MeshInstance3D? _meshInstance;
    private ImmediateMesh? _mesh;
    private Vector3 _facingDirection = Vector3.Forward;
    private Vector3 _velocityDirection = Vector3.Zero;
    private Vector3 _desiredDirection = Vector3.Zero;

    public override void _Ready()
    {
        EnsureMesh();
        RebuildMesh();
    }

    public void UpdateDirections(HypeCharacterMotorState state)
    {
        _facingDirection = state.FacingDirection;
        _velocityDirection = state.VelocityWorldDirection;
        _desiredDirection = state.DesiredWorldDirection;
        RebuildMesh();
    }

    private void EnsureMesh()
    {
        _meshInstance = GetNodeOrNull<MeshInstance3D>("Arrows");
        if (_meshInstance == null)
        {
            _meshInstance = new MeshInstance3D
            {
                Name = "Arrows",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(_meshInstance);
            _meshInstance.Owner = Owner;
        }

        _mesh = _meshInstance.Mesh as ImmediateMesh;
        if (_mesh == null)
        {
            _mesh = new ImmediateMesh();
            _meshInstance.Mesh = _mesh;
        }

        if (_meshInstance.MaterialOverride == null)
        {
            _meshInstance.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true
            };
        }
    }

    private void RebuildMesh()
    {
        if (_mesh == null)
        {
            return;
        }

        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var origin = new Vector3(0f, HeightOffset, 0f);

        AddArrow(origin, _facingDirection, new Color(0.18f, 0.95f, 0.32f, 1f));
        AddArrow(origin, _velocityDirection, new Color(0.2f, 0.85f, 1f, 1f));
        AddArrow(origin, _desiredDirection, new Color(1f, 0.88f, 0.18f, 1f));

        _mesh.SurfaceEnd();
    }

    private void AddArrow(Vector3 origin, Vector3 direction, Color color)
    {
        if (direction.LengthSquared() <= 0.000001f)
        {
            return;
        }

        var dir = direction.Normalized();
        var tip = origin + (dir * LineLength);
        var side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() <= 0.000001f)
        {
            side = dir.Cross(Vector3.Right);
        }
        side = side.Normalized();

        var back = tip - (dir * HeadLength);
        var left = back + (side * HeadWidth);
        var right = back - (side * HeadWidth);

        AddLine(origin, tip, color);
        AddLine(tip, left, color);
        AddLine(tip, right, color);
    }

    private void AddLine(Vector3 a, Vector3 b, Color color)
    {
        _mesh!.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(b);
    }
}
