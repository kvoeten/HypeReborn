using Godot;

namespace HypeReborn.Hype.Player;

public enum HypeLocomotionState
{
    Idle,
    Walk,
    Run,
    Jump,
    Fall
}

public sealed class HypeCharacterMotorState
{
    public bool Grounded { get; set; }
    public float HorizontalSpeed { get; set; }
    public float VerticalSpeed { get; set; }
    public HypeLocomotionState LocomotionState { get; set; } = HypeLocomotionState.Idle;
    public Vector3 DesiredWorldDirection { get; set; } = Vector3.Forward;
    public Vector3 VelocityWorldDirection { get; set; } = Vector3.Forward;
    public Vector3 FacingDirection { get; set; } = Vector3.Forward;
    public Vector3 AttackAimDirection { get; set; } = Vector3.Forward;

    public string MovementState => LocomotionState.ToString();
}
