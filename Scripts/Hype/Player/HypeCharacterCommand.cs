using Godot;

namespace HypeReborn.Hype.Player;

public readonly struct HypeCharacterCommand
{
    public HypeCharacterCommand(
        Vector2 moveInput,
        Vector2 lookInput,
        bool jumpPressed,
        bool sprintHeld,
        bool interactPressed,
        bool toggleMovementModelPressed)
    {
        MoveInput = moveInput;
        LookInput = lookInput;
        JumpPressed = jumpPressed;
        SprintHeld = sprintHeld;
        InteractPressed = interactPressed;
        ToggleMovementModelPressed = toggleMovementModelPressed;
    }

    public Vector2 MoveInput { get; }
    public Vector2 LookInput { get; }
    public bool JumpPressed { get; }
    public bool SprintHeld { get; }
    public bool InteractPressed { get; }
    public bool ToggleMovementModelPressed { get; }

    public static HypeCharacterCommand Empty => new(
        Vector2.Zero,
        Vector2.Zero,
        jumpPressed: false,
        sprintHeld: false,
        interactPressed: false,
        toggleMovementModelPressed: false);
}
