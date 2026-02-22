using Godot;

namespace HypeReborn.Hype.Maps;

[GlobalClass]
public partial class HypeMapDefinition : Resource
{
    [Export]
    public string LevelName { get; set; } = string.Empty;

    [Export(PropertyHint.Dir)]
    public string ExternalGameRootOverride { get; set; } = string.Empty;

    [Export]
    public bool LoadLanguageLayer { get; set; } = true;

    [Export]
    public bool LoadScripts { get; set; } = true;

    [Export]
    public bool LoadAnimationCatalog { get; set; } = true;

    [Export(PropertyHint.MultilineText)]
    public string DesignerNotes { get; set; } = string.Empty;
}