using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime;

public sealed class HypeLevelRecord
{
    public required string LevelName { get; init; }
    public required string LevelDirectoryPath { get; init; }
    public required Dictionary<string, string> CoreFiles { get; init; }
    public required Dictionary<string, string> LanguageFiles { get; init; }
}

public sealed class HypeAnimationRecord
{
    public required string Id { get; init; }
    public required string SourceLevel { get; init; }
    public required string SourceFile { get; init; }
    public required string VirtualPath { get; init; }
}

public sealed class HypeScriptRecord
{
    public required string Id { get; init; }
    public required string SourceLevel { get; init; }
    public required string SourceFile { get; init; }
    public required string ScriptKind { get; init; }
    public required string VirtualPath { get; init; }
}

public sealed class HypeTextureContainerRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SourceFile { get; init; }
    public required string VirtualPath { get; init; }
}

public sealed class HypeTextureEntryRecord
{
    public required string Id { get; init; }
    public required string ContainerId { get; init; }
    public required string ContainerSourceFile { get; init; }
    public required string EntryName { get; init; }
    public required string EntryFullName { get; init; }
    public required int SizeBytes { get; init; }
    public required string VirtualPath { get; init; }
}

public enum HypeParseSeverity
{
    Info,
    Warning,
    Error
}

public sealed class HypeParseDiagnostic
{
    public required HypeParseSeverity Severity { get; init; }
    public required string Phase { get; init; }
    public required string Message { get; init; }
}

public sealed class HypeParsedLevelRecord
{
    public required string LevelName { get; init; }
    public required bool Succeeded { get; init; }
    public required int FixSnaBlockCount { get; init; }
    public required int LevelSnaBlockCount { get; init; }
    public required int FixRelocationBlockCount { get; init; }
    public required int LevelRelocationBlockCount { get; init; }
    public required int ResolvedSnaPointers { get; init; }
    public required int UnresolvedSnaPointers { get; init; }
    public required int ResolvedGptPointers { get; init; }
    public required int UnresolvedGptPointers { get; init; }
    public required int ResolvedPtxPointers { get; init; }
    public required int UnresolvedPtxPointers { get; init; }
    public required IReadOnlyList<HypeParseDiagnostic> Diagnostics { get; init; }
}

public sealed class HypeParsedTextureUsageRecord
{
    public required string TextureTgaName { get; init; }
    public required int SurfaceCount { get; init; }
}

public sealed class HypeParsedMapAssetRecord
{
    public required string LevelName { get; init; }
    public required bool Succeeded { get; init; }
    public required int GeometryEntityCount { get; init; }
    public required int LightEntityCount { get; init; }
    public required int ParticleSourceCount { get; init; }
    public required int AnimationAnchorCount { get; init; }
    public required int MeshSurfaceCount { get; init; }
    public required IReadOnlyList<HypeParsedTextureUsageRecord> TextureUsages { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class HypeAssetIndex
{
    public required string GameRoot { get; init; }
    public required string Language { get; init; }
    public required IReadOnlyList<HypeLevelRecord> Levels { get; init; }
    public required IReadOnlyList<HypeAnimationRecord> Animations { get; init; }
    public required IReadOnlyList<HypeScriptRecord> Scripts { get; init; }
    public required IReadOnlyList<HypeTextureContainerRecord> TextureContainers { get; init; }
    public required IReadOnlyList<HypeTextureEntryRecord> TextureEntries { get; init; }
    public required IReadOnlyList<HypeParsedLevelRecord> ParsedLevels { get; init; }
    public required IReadOnlyList<HypeParsedMapAssetRecord> ParsedMapAssets { get; init; }
}
