using System.Collections.Generic;

namespace HypeReborn.Hype.Runtime;

public sealed class HypeVirtualFileEntry
{
    public required string Name { get; init; }
    public required string VirtualPath { get; init; }
    public required HypeContentKind Kind { get; init; }
    public string? AbsolutePath { get; init; }
    public string? AuxData { get; init; }
    public List<HypeVirtualFileEntry> Children { get; } = new();
}
