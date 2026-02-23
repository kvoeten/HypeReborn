using Godot;

namespace HypeReborn.Hype.Player;

public sealed class HypeCharacterMotor
{
    public HypeCharacterMotorState State { get; } = new();

    public void Step(
        CharacterBody3D body,
        HypeCharacterDefinition definition,
        Basis movementBasis,
        HypeCharacterCommand command,
        float delta)
    {
        var moveInput = command.MoveInput;
        var rawDirection = (movementBasis.X * moveInput.X) + (-movementBasis.Z * moveInput.Y);
        rawDirection.Y = 0f;
        var desiredDirection = rawDirection.LengthSquared() > float.Epsilon
            ? rawDirection.Normalized()
            : Vector3.Zero;
        State.DesiredWorldDirection = desiredDirection;

        var velocity = body.Velocity;
        var horizontalVelocity = new Vector3(velocity.X, 0f, velocity.Z);
        var desiredSpeed = command.SprintHeld ? definition.SprintSpeed : definition.WalkSpeed;
        var desiredHorizontalVelocity = desiredDirection * desiredSpeed;

        var onFloor = body.IsOnFloor();
        var acceleration = onFloor ? definition.GroundAcceleration : definition.AirAcceleration;
        var deceleration = onFloor ? definition.GroundDeceleration : definition.AirDeceleration;

        horizontalVelocity = desiredDirection.LengthSquared() > float.Epsilon
            ? horizontalVelocity.MoveToward(desiredHorizontalVelocity, acceleration * delta)
            : horizontalVelocity.MoveToward(Vector3.Zero, deceleration * delta);

        if (onFloor)
        {
            if (velocity.Y < 0f)
            {
                velocity.Y = -definition.FloorStickVelocity;
            }

            if (definition.EnableJump && command.JumpPressed)
            {
                velocity.Y = definition.JumpVelocity;
            }
        }
        else
        {
            velocity.Y = Mathf.Max(velocity.Y - (definition.Gravity * delta), -definition.MaxFallSpeed);
        }

        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;

        body.UpDirection = Vector3.Up;
        body.FloorMaxAngle = Mathf.DegToRad(definition.MaxSlopeDegrees);
        body.Velocity = velocity;
        body.MoveAndSlide();

        var resultingVelocity = body.Velocity;
        State.Grounded = body.IsOnFloor();
        State.HorizontalSpeed = new Vector3(resultingVelocity.X, 0f, resultingVelocity.Z).Length();
        State.MovementState = ResolveMovementState(State.Grounded, State.HorizontalSpeed, resultingVelocity.Y);
    }

    private static string ResolveMovementState(bool grounded, float horizontalSpeed, float verticalSpeed)
    {
        if (!grounded)
        {
            return verticalSpeed > 0f ? "Jump" : "Fall";
        }

        if (horizontalSpeed > 3f)
        {
            return "Run";
        }

        if (horizontalSpeed > 0.15f)
        {
            return "Walk";
        }

        return "Idle";
    }
}
