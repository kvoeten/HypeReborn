using Godot;

namespace HypeReborn.Hype.Player;

public static class HypePlayerInputDefaults
{
    public const string ActionMoveLeft = "hype_move_left";
    public const string ActionMoveRight = "hype_move_right";
    public const string ActionMoveForward = "hype_move_forward";
    public const string ActionMoveBack = "hype_move_back";
    public const string ActionJump = "hype_jump";
    public const string ActionSprint = "hype_sprint";
    public const string ActionInteract = "hype_interact";
    public const string ActionMouseCaptureToggle = "hype_mouse_capture_toggle";

    public static void EnsureDefaults()
    {
        EnsureAction(ActionMoveLeft, Key.A);
        EnsureAction(ActionMoveRight, Key.D);
        EnsureAction(ActionMoveForward, Key.W);
        EnsureAction(ActionMoveBack, Key.S);
        EnsureAction(ActionJump, Key.Space);
        EnsureAction(ActionSprint, Key.Shift);
        EnsureAction(ActionInteract, Key.E);
        EnsureAction(ActionMouseCaptureToggle, Key.Escape);
    }

    private static void EnsureAction(string actionName, Key physicalKey)
    {
        if (!InputMap.HasAction(actionName))
        {
            InputMap.AddAction(actionName);
        }

        var keyEvent = new InputEventKey
        {
            PhysicalKeycode = physicalKey
        };

        if (!InputMap.ActionHasEvent(actionName, keyEvent))
        {
            InputMap.ActionAddEvent(actionName, keyEvent);
        }
    }
}
