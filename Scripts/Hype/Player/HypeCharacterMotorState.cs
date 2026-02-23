using Godot;

namespace HypeReborn.Hype.Player;

public sealed class HypeCharacterMotorState
{
    public bool Grounded { get; set; }
    public float HorizontalSpeed { get; set; }
    public string MovementState { get; set; } = "Idle";
    public Vector3 DesiredWorldDirection { get; set; } = Vector3.Forward;
}
