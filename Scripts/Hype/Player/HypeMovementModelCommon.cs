using Godot;

namespace HypeReborn.Hype.Player;

internal static class HypeMovementModelCommon
{
    public static Vector3 ResolveDesiredDirection(Basis movementBasis, Vector2 moveInput)
    {
        var rawDirection = (movementBasis.X * moveInput.X) + (-movementBasis.Z * moveInput.Y);
        rawDirection.Y = 0f;
        return rawDirection.LengthSquared() > float.Epsilon
            ? rawDirection.Normalized()
            : Vector3.Zero;
    }

    public static float ResolveDesiredSpeed(HypeCharacterDefinition definition, in HypeCharacterCommand command)
    {
        return command.SprintHeld ? definition.SprintSpeed : definition.WalkSpeed;
    }

    public static float ApplyVerticalVelocity(
        float verticalVelocity,
        bool onFloor,
        HypeCharacterDefinition definition,
        in HypeCharacterCommand command,
        float delta)
    {
        if (onFloor)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -definition.FloorStickVelocity;
            }

            if (definition.EnableJump && command.JumpPressed)
            {
                if (definition.JumpAbsolute || definition.JumpWithoutAddingSpeed)
                {
                    verticalVelocity = definition.JumpVelocity;
                }
                else
                {
                    verticalVelocity += definition.JumpVelocity;
                }
            }

            return verticalVelocity;
        }

        return Mathf.Max(verticalVelocity - (definition.Gravity * delta), -definition.MaxFallSpeed);
    }

    public static void ConfigureBody(CharacterBody3D body, HypeCharacterDefinition definition)
    {
        body.UpDirection = Vector3.Up;
        body.FloorMaxAngle = Mathf.DegToRad(definition.MaxSlopeDegrees);
    }

    public static void UpdatePostStepState(
        CharacterBody3D body,
        HypeCharacterDefinition definition,
        in HypeCharacterCommand command,
        Vector3 desiredDirection,
        HypeCharacterMotorState state)
    {
        var resultingVelocity = body.Velocity;
        var resultingHorizontalVelocity = new Vector3(resultingVelocity.X, 0f, resultingVelocity.Z);
        var resultingHorizontalSpeed = resultingHorizontalVelocity.Length();
        if (resultingHorizontalSpeed > 0.001f)
        {
            state.VelocityWorldDirection = resultingHorizontalVelocity / resultingHorizontalSpeed;
        }

        state.Grounded = body.IsOnFloor();
        state.HorizontalSpeed = resultingHorizontalSpeed;
        state.VerticalSpeed = resultingVelocity.Y;
        state.LocomotionState = ResolveLocomotionState(
            state.Grounded,
            state.HorizontalSpeed,
            state.VerticalSpeed,
            definition,
            command);
        state.AttackAimDirection = desiredDirection.LengthSquared() > 0.001f
            ? desiredDirection
            : state.FacingDirection;
    }

    private static HypeLocomotionState ResolveLocomotionState(
        bool grounded,
        float horizontalSpeed,
        float verticalSpeed,
        HypeCharacterDefinition definition,
        in HypeCharacterCommand command)
    {
        if (!grounded)
        {
            return verticalSpeed > 0.05f
                ? HypeLocomotionState.Jump
                : HypeLocomotionState.Fall;
        }

        var stopThreshold = Mathf.Max(0.05f, definition.WalkSpeed * 0.08f);
        if (horizontalSpeed <= stopThreshold)
        {
            return HypeLocomotionState.Idle;
        }

        if (command.SprintHeld)
        {
            return HypeLocomotionState.Run;
        }

        var runThreshold = Mathf.Max(
            definition.WalkSpeed * 0.92f,
            (definition.WalkSpeed + definition.SprintSpeed) * 0.5f);
        return horizontalSpeed >= runThreshold
            ? HypeLocomotionState.Run
            : HypeLocomotionState.Walk;
    }
}

