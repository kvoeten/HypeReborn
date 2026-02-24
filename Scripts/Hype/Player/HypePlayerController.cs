using Godot;

namespace HypeReborn.Hype.Player;

/// <summary>
/// Owns player input + camera and drives a possessed <see cref="HypeCharacterRoot"/>.
/// </summary>
public partial class HypePlayerController : Node
{
    [Export]
    public NodePath ControlledCharacterPath { get; set; } = new();

    [Export]
    public NodePath InputAdapterPath { get; set; } = new("InputAdapter");

    [Export]
    public NodePath CameraRigPath { get; set; } = new("CameraRig");

    [Export]
    public bool AutoFindCharacterInScene { get; set; } = true;

    private HypeCharacterRoot? _controlledCharacter;
    private HypeCharacterInputAdapter? _inputAdapter;
    private HypePlayerCameraRig? _cameraRig;

    public override void _Ready()
    {
        ProcessPriority = -50;
        ResolveInputAdapter();
        ResolveCameraRig();
        ResolveControlledCharacter();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_controlledCharacter == null && !ResolveControlledCharacter())
        {
            return;
        }

        if (_inputAdapter == null)
        {
            ResolveInputAdapter();
        }

        if (_cameraRig == null)
        {
            ResolveCameraRig();
        }

        if (_inputAdapter == null || _cameraRig == null || _controlledCharacter == null)
        {
            return;
        }

        _cameraRig.BindTarget(_controlledCharacter);

        var command = _inputAdapter.PollCommand();
        _cameraRig.ConsumeLookInput(command.LookInput);
        var movementBasis = _cameraRig.GetMovementBasis();
        _controlledCharacter.StepFromPlayerController(command, movementBasis, (float)delta);
    }

    public void SetControlledCharacter(HypeCharacterRoot? character)
    {
        _controlledCharacter = character;
    }

    private void ResolveInputAdapter()
    {
        _inputAdapter = GetNodeOrNull<HypeCharacterInputAdapter>(InputAdapterPath);
        if (_inputAdapter != null)
        {
            return;
        }

        _inputAdapter = GetNodeOrNull<HypeCharacterInputAdapter>("InputAdapter");
        if (_inputAdapter == null)
        {
            _inputAdapter = new HypeCharacterInputAdapter { Name = "InputAdapter" };
            AddChild(_inputAdapter);
            _inputAdapter.Owner = Owner;
        }
    }

    private void ResolveCameraRig()
    {
        _cameraRig = GetNodeOrNull<HypePlayerCameraRig>(CameraRigPath);
        if (_cameraRig != null)
        {
            return;
        }

        _cameraRig = GetNodeOrNull<HypePlayerCameraRig>("CameraRig");
        if (_cameraRig == null)
        {
            _cameraRig = new HypePlayerCameraRig { Name = "CameraRig" };
            AddChild(_cameraRig);
            _cameraRig.Owner = Owner;
        }
    }

    private bool ResolveControlledCharacter()
    {
        if (!ControlledCharacterPath.IsEmpty)
        {
            _controlledCharacter = GetNodeOrNull<HypeCharacterRoot>(ControlledCharacterPath);
            if (_controlledCharacter != null)
            {
                return true;
            }
        }

        if (!AutoFindCharacterInScene)
        {
            return false;
        }

        _controlledCharacter = FindFirstCharacter(GetTree()?.CurrentScene ?? GetTree()?.Root);
        return _controlledCharacter != null;
    }

    private static HypeCharacterRoot? FindFirstCharacter(Node? root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is HypeCharacterRoot character)
        {
            return character;
        }

        foreach (var child in root.GetChildren())
        {
            if (child is not Node childNode)
            {
                continue;
            }

            var nested = FindFirstCharacter(childNode);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
