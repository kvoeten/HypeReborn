namespace HypeReborn.Hype.Player;

/// <summary>
/// Defines the minimal contract between gameplay/motor state and visual character presentation.
/// Implementations can use legacy actor frame playback, Godot AnimationTree, skeletal rigs, or any
/// future renderer, as long as they consume the same motor state.
/// </summary>
public interface ICharacterVisualController
{
    /// <summary>
    /// Applies latest character state for this frame.
    /// Called from <c>HypeCharacterRoot</c> each physics tick.
    /// </summary>
    void ApplyState(HypeCharacterMotorState state, float delta);

    /// <summary>
    /// Forces the visual representation to rebuild/reload from its active source data.
    /// </summary>
    void RebuildVisual();

    /// <summary>
    /// Sets the active visual actor by stable key.
    /// Implementations that do not use actor keys can ignore this safely.
    /// </summary>
    void SetActorSelection(string actorKey, bool persistToSave);

    /// <summary>
    /// Supplies locomotion speed references used by state-driven animation playback.
    /// </summary>
    void ConfigureSpeedReferences(float walkSpeed, float runSpeed);
}
