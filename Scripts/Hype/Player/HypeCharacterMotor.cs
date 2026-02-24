using Godot;

namespace HypeReborn.Hype.Player;

public sealed class HypeCharacterMotor
{
    private readonly IHypeMovementModel _classicModel = new HypeClassicMovementModel();
    private readonly IHypeMovementModel _modernModel = new HypeModernMovementModel();

    public HypeCharacterMotorState State { get; } = new();
    public HypeMovementModelKind MovementModel { get; private set; } = HypeMovementModelKind.Classic;

    public void SetMovementModel(HypeMovementModelKind movementModel)
    {
        MovementModel = movementModel;
    }

    public void Step(
        CharacterBody3D body,
        HypeCharacterDefinition definition,
        Basis movementBasis,
        in HypeCharacterCommand command,
        float delta)
    {
        var model = MovementModel == HypeMovementModelKind.Modern
            ? _modernModel
            : _classicModel;
        model.Step(body, definition, movementBasis, command, State, delta);
    }
}
