using Godot;
using HypeReborn.Hype.Player;
using HypeReborn.Hype.Runtime;
using HypeReborn.Hype.Runtime.Characters;

namespace HypeReborn.Hype.Maps;

public static class HypeMapActorNodeFactory
{
    public static void Create(Node3D parent, Node? owner, string levelName, HypeResolvedEntity entity)
    {
        var actorId = !string.IsNullOrWhiteSpace(entity.ActorId) ? entity.ActorId : entity.Id;
        var actorKey = HypeActorCatalogService.BuildActorKey(levelName, actorId);
        var actor = new HypeNpcActor
        {
            Name = entity.Name,
            Transform = entity.Transform,
            ActorKey = actorKey,
            FallbackLevelName = levelName,
            FallbackActorId = actorId,
            AutoLoadOnReady = true
        };
        parent.AddChild(actor);
        actor.Owner = owner;

        var role = entity.IsMainActor
            ? "HERO"
            : !entity.IsSectorCharacterListMember
                ? "LEVEL"
                : entity.IsTargettable
                    ? "ENEMY"
                    : "NPC";
        var label = new Label3D
        {
            Name = "ActorLabel",
            Position = new Vector3(0f, 1.7f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Text = $"{role} | bits:0x{entity.CustomBits:X8}"
        };
        actor.AddChild(label);
        label.Owner = owner;
    }
}
