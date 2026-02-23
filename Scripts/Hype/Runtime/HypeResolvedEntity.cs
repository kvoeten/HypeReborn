using Godot;

namespace HypeReborn.Hype.Runtime;

public enum HypeResolvedEntityKind
{
    Geometry,
    Light,
    ParticleSource,
    AnimationAnchor,
    Actor
}

public sealed class HypeResolvedMeshSurface
{
    public required Vector3[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public required Vector2[] Uvs { get; init; }
    public Vector3[]? Normals { get; init; }
    public required bool DoubleSided { get; init; }
    public string? TextureTgaName { get; init; }
    public uint VisualMaterialFlags { get; init; }
    public uint TextureFlags { get; init; }
    public byte TextureFlagsByte { get; init; }
    public uint TextureAlphaMask { get; init; }
}

public sealed class HypeResolvedMesh
{
    public required System.Collections.Generic.IReadOnlyList<HypeResolvedMeshSurface> Surfaces { get; init; }
}

public sealed class HypeResolvedEntity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required HypeResolvedEntityKind Kind { get; init; }
    public required Transform3D Transform { get; init; }
    public bool FlipWinding { get; init; }
    public string? SourceFile { get; init; }
    public HypeResolvedMesh? Mesh { get; init; }
    public string? ActorId { get; init; }
    public bool IsMainActor { get; init; }
    public bool IsSectorCharacterListMember { get; init; }
    public bool IsTargettable { get; init; }
    public uint CustomBits { get; init; }
}
