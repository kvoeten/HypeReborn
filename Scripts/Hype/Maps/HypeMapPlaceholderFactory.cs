using Godot;
using HypeReborn.Hype.Runtime;

namespace HypeReborn.Hype.Maps;

public static class HypeMapPlaceholderFactory
{
    public static void CreateLight(Node3D parent, Node? owner, HypeResolvedEntity entity)
    {
        var light = new OmniLight3D
        {
            Name = entity.Name,
            Transform = entity.Transform,
            LightColor = new Color(1f, 0.95f, 0.8f),
            LightEnergy = 1.3f,
            OmniRange = 8f
        };
        parent.AddChild(light);
        light.Owner = owner;
    }

    public static void CreateParticleSource(Node3D parent, Node? owner, HypeResolvedEntity entity)
    {
        var particles = new GpuParticles3D
        {
            Name = entity.Name,
            Transform = entity.Transform,
            Amount = 32,
            Lifetime = 1.2f,
            OneShot = false,
            Emitting = true,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.2f, 0.2f) },
            ProcessMaterial = new ParticleProcessMaterial
            {
                Gravity = new Vector3(0f, -0.1f, 0f),
                InitialVelocityMin = 0.2f,
                InitialVelocityMax = 0.6f
            }
        };
        parent.AddChild(particles);
        particles.Owner = owner;
    }

    public static void CreateAnimationAnchor(Node3D parent, Node? owner, HypeResolvedEntity entity)
    {
        var anchor = new Node3D
        {
            Name = entity.Name,
            Transform = entity.Transform
        };
        parent.AddChild(anchor);
        anchor.Owner = owner;

        var label = new Label3D
        {
            Name = "Label",
            Position = new Vector3(0f, 0.6f, 0f),
            Text = entity.Name
        };
        anchor.AddChild(label);
        label.Owner = owner;
    }
}
