namespace HypeReborn.Hype.Runtime.Parsing;

internal readonly struct HypeParseContext
{
    public required HypeRelocatedAddressSpace Space { get; init; }
    public required HypeAddress LevelGptAddress { get; init; }
}
