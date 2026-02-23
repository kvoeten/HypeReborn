using Godot;

namespace HypeReborn.Hype.Player;

[GlobalClass]
public partial class HypeCharacterDefinition : Resource
{
    [Export]
    public float WalkSpeed { get; set; } = 4.5f;

    [Export]
    public float SprintSpeed { get; set; } = 7.5f;

    [Export]
    public float GroundAcceleration { get; set; } = 24f;

    [Export]
    public float GroundDeceleration { get; set; } = 18f;

    [Export]
    public float AirAcceleration { get; set; } = 8f;

    [Export]
    public float AirDeceleration { get; set; } = 4f;

    [Export]
    public float Gravity { get; set; } = 24f;

    [Export]
    public float MaxFallSpeed { get; set; } = 45f;

    [Export]
    public bool EnableJump { get; set; } = true;

    [Export]
    public float JumpVelocity { get; set; } = 7.6f;

    [Export]
    public float MaxSlopeDegrees { get; set; } = 45f;

    [Export]
    public float FloorStickVelocity { get; set; } = 1.4f;

    [Export]
    public float CapsuleRadius { get; set; } = 0.35f;

    [Export]
    public float CapsuleHeight { get; set; } = 1.25f;
}
