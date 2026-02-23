using Godot;

namespace HypeReborn.Hype.Player;

public partial class HypeCharacterInputAdapter : Node, ICharacterCommandSource
{
    [Export]
    public bool CaptureMouseOnReady { get; set; } = true;

    [Export]
    public float LookScale { get; set; } = 1f;

    private Vector2 _lookDelta;

    public override void _Ready()
    {
        HypePlayerInputDefaults.EnsureDefaults();
        if (CaptureMouseOnReady)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _lookDelta += motion.Relative * LookScale;
        }
    }

    public HypeCharacterCommand PollCommand()
    {
        if (Input.IsActionJustPressed(HypePlayerInputDefaults.ActionMouseCaptureToggle))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }

        var command = new HypeCharacterCommand(
            moveInput: Input.GetVector(
                HypePlayerInputDefaults.ActionMoveLeft,
                HypePlayerInputDefaults.ActionMoveRight,
                HypePlayerInputDefaults.ActionMoveBack,
                HypePlayerInputDefaults.ActionMoveForward),
            lookInput: _lookDelta,
            jumpPressed: Input.IsActionJustPressed(HypePlayerInputDefaults.ActionJump),
            sprintHeld: Input.IsActionPressed(HypePlayerInputDefaults.ActionSprint),
            interactPressed: Input.IsActionJustPressed(HypePlayerInputDefaults.ActionInteract));

        _lookDelta = Vector2.Zero;
        return command;
    }
}
