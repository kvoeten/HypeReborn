using Godot;

namespace HypeReborn.Hype.Player;

/// <summary>
/// Closely follows the legacy acceleration/deceleration profile and jump semantics.
/// </summary>
public sealed class HypeClassicMovementModel : IHypeMovementModel
{
    public void Step(
        CharacterBody3D body,
        HypeCharacterDefinition definition,
        Basis movementBasis,
        in HypeCharacterCommand command,
        HypeCharacterMotorState state,
        float delta)
    {
        var desiredDirection = HypeMovementModelCommon.ResolveDesiredDirection(movementBasis, command.MoveInput);
        state.DesiredWorldDirection = desiredDirection;

        var velocity = body.Velocity;
        var horizontalVelocity = new Vector3(velocity.X, 0f, velocity.Z);
        var desiredSpeed = HypeMovementModelCommon.ResolveDesiredSpeed(definition, command);
        var desiredHorizontalVelocity = desiredDirection * desiredSpeed;

        var onFloor = body.IsOnFloor();
        var acceleration = onFloor ? definition.GroundAcceleration : definition.AirAcceleration;
        var deceleration = onFloor ? definition.GroundDeceleration : definition.AirDeceleration;
        if (onFloor && desiredDirection.LengthSquared() <= float.Epsilon)
        {
            deceleration *= Mathf.Max(1f, definition.BrakeDecelerationMultiplier);
        }

        horizontalVelocity = desiredDirection.LengthSquared() > float.Epsilon
            ? horizontalVelocity.MoveToward(desiredHorizontalVelocity, acceleration * delta)
            : horizontalVelocity.MoveToward(Vector3.Zero, deceleration * delta);

        velocity.Y = HypeMovementModelCommon.ApplyVerticalVelocity(velocity.Y, onFloor, definition, command, delta);
        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;

        HypeMovementModelCommon.ConfigureBody(body, definition);
        body.Velocity = velocity;
        body.MoveAndSlide();

        HypeMovementModelCommon.UpdatePostStepState(body, definition, command, desiredDirection, state);
    }
}

