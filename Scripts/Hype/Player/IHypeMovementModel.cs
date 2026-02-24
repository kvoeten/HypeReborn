using Godot;

namespace HypeReborn.Hype.Player;

/// <summary>
/// Implements a single locomotion model variant (classic or modern).
/// </summary>
public interface IHypeMovementModel
{
    void Step(
        CharacterBody3D body,
        HypeCharacterDefinition definition,
        Basis movementBasis,
        in HypeCharacterCommand command,
        HypeCharacterMotorState state,
        float delta);
}

