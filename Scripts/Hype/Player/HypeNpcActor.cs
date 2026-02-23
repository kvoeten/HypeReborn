using Godot;
using HypeReborn.Hype.Runtime.Characters;

namespace HypeReborn.Hype.Player;

[Tool]
public partial class HypeNpcActor : Node3D
{
    [Export]
    public string ActorKey { get; set; } = string.Empty;

    [Export]
    public string FallbackLevelName { get; set; } = string.Empty;

    [Export]
    public string FallbackActorId { get; set; } = string.Empty;

    [Export]
    public bool AutoLoadOnReady { get; set; } = true;

    private HypeCharacterVisualDriver? _visualDriver;

    public override void _Ready()
    {
        EnsureVisualDriver();

        if (AutoLoadOnReady)
        {
            RebuildVisual();
        }
    }

    public void RebuildVisual()
    {
        if (_visualDriver == null)
        {
            return;
        }

        _visualDriver.UseSaveActorSelection = false;
        _visualDriver.AutoDetectLevelFromScene = false;

        if (HypeActorCatalogService.TryParseActorKey(ActorKey, out var levelName, out var actorId))
        {
            _visualDriver.SetActorSelection(levelName, actorId, persistToSave: false);
            return;
        }

        _visualDriver.SetActorSelection(FallbackLevelName, FallbackActorId, persistToSave: false);
    }

    private void EnsureVisualDriver()
    {
        _visualDriver = GetNodeOrNull<HypeCharacterVisualDriver>("VisualDriver");
        if (_visualDriver != null)
        {
            return;
        }

        _visualDriver = new HypeCharacterVisualDriver
        {
            Name = "VisualDriver",
            UseSaveActorSelection = false,
            AutoDetectLevelFromScene = false
        };
        AddChild(_visualDriver);
        _visualDriver.Owner = Owner;
    }
}
