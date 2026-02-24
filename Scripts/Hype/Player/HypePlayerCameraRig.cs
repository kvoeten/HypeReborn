using Godot;

namespace HypeReborn.Hype.Player;

public partial class HypePlayerCameraRig : Node3D
{
    [Export]
    public float Distance { get; set; } = 4.6f;

    [Export]
    public float Height { get; set; } = 1.55f;

    [Export]
    public float FollowSharpness { get; set; } = 16f;

    [Export]
    public float LookSensitivity { get; set; } = 0.0028f;

    [Export]
    public float MinPitchDegrees { get; set; } = -60f;

    [Export]
    public float MaxPitchDegrees { get; set; } = 70f;

    [Export]
    public float CollisionMargin { get; set; } = 0.12f;

    private Node3D? _target;
    private Node3D? _pitchPivot;
    private SpringArm3D? _springArm;
    private Camera3D? _camera;
    private float _yaw;
    private float _pitch;

    public override void _Ready()
    {
        // Keep camera world-space stable even when the character rotates.
        TopLevel = true;
        EnsureRigHierarchy();
    }

    public override void _Process(double delta)
    {
        if (_target == null)
        {
            return;
        }

        if (_springArm != null)
        {
            _springArm.SpringLength = Distance;
            _springArm.Margin = CollisionMargin;
        }

        var dt = (float)delta;
        var anchor = _target.GlobalPosition + Vector3.Up * Height;
        var t = 1f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(anchor, t);

        Rotation = new Vector3(0f, _yaw, 0f);
        if (_pitchPivot != null)
        {
            _pitchPivot.Rotation = new Vector3(_pitch, 0f, 0f);
        }
    }

    public void BindTarget(Node3D target)
    {
        if (_target == target)
        {
            return;
        }

        _target = target;
        var basis = target.GlobalBasis;
        _yaw = basis.GetEuler().Y;
    }

    public void ConsumeLookInput(Vector2 lookInput)
    {
        _yaw -= lookInput.X * LookSensitivity;

        var minPitch = Mathf.DegToRad(MinPitchDegrees);
        var maxPitch = Mathf.DegToRad(MaxPitchDegrees);
        _pitch = Mathf.Clamp(_pitch - (lookInput.Y * LookSensitivity), minPitch, maxPitch);
    }

    public Basis GetMovementBasis()
    {
        return new Basis(Vector3.Up, _yaw);
    }

    private void EnsureRigHierarchy()
    {
        _pitchPivot = GetNodeOrNull<Node3D>("PitchPivot");
        if (_pitchPivot == null)
        {
            _pitchPivot = new Node3D { Name = "PitchPivot" };
            AddChild(_pitchPivot);
            _pitchPivot.Owner = Owner;
        }

        _springArm = _pitchPivot.GetNodeOrNull<SpringArm3D>("SpringArm3D");
        if (_springArm == null)
        {
            _springArm = new SpringArm3D
            {
                Name = "SpringArm3D",
                SpringLength = Distance,
                Margin = CollisionMargin
            };
            _pitchPivot.AddChild(_springArm);
            _springArm.Owner = Owner;
        }

        _camera = _springArm.GetNodeOrNull<Camera3D>("Camera3D");
        if (_camera == null)
        {
            _camera = new Camera3D
            {
                Name = "Camera3D",
                Current = true,
                Position = new Vector3(0f, 0f, 0f)
            };
            _springArm.AddChild(_camera);
            _camera.Owner = Owner;
        }
        else
        {
            _camera.Current = true;
        }
    }
}
