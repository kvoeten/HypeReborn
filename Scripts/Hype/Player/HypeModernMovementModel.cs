using Godot;

namespace HypeReborn.Hype.Player;

/// <summary>
/// Uses the same input contract as classic movement, but with snappier steering and turn response.
/// </summary>
public sealed class HypeModernMovementModel : IHypeMovementModel
{
    private const float GroundAccelerationMultiplier = 1.35f;
    private const float GroundDecelerationMultiplier = 1.2f;
    private const float AirAccelerationMultiplier = 1.2f;
    private const float AirDecelerationMultiplier = 0.9f;
    private const float ReverseDirectionBoost = 1.5f;

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
        var acceleration = (onFloor ? definition.GroundAcceleration : definition.AirAcceleration)
            * (onFloor ? GroundAccelerationMultiplier : AirAccelerationMultiplier);
        var deceleration = (onFloor ? definition.GroundDeceleration : definition.AirDeceleration)
            * (onFloor ? GroundDecelerationMultiplier : AirDecelerationMultiplier);

        if (desiredDirection.LengthSquared() > float.Epsilon)
        {
            var currentDir = horizontalVelocity.LengthSquared() > 0.001f
                ? horizontalVelocity.Normalized()
                : Vector3.Zero;
            if (currentDir.LengthSquared() > 0.001f && currentDir.Dot(desiredDirection) < 0f)
            {
                acceleration *= ReverseDirectionBoost;
            }

            horizontalVelocity = horizontalVelocity.MoveToward(desiredHorizontalVelocity, acceleration * delta);
        }
        else
        {
            horizontalVelocity = horizontalVelocity.MoveToward(Vector3.Zero, deceleration * delta);
        }

        velocity.Y = HypeMovementModelCommon.ApplyVerticalVelocity(velocity.Y, onFloor, definition, command, delta);
        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;

        HypeMovementModelCommon.ConfigureBody(body, definition);
        body.Velocity = velocity;
        body.MoveAndSlide();

        HypeMovementModelCommon.UpdatePostStepState(body, definition, command, desiredDirection, state);
    }
}

