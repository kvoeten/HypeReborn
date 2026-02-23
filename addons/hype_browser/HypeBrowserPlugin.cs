using Godot;
using HypeReborn.Hype.Config;

namespace HypeReborn.Hype.EditorPlugin;

[Tool]
public partial class HypeBrowserPlugin : Godot.EditorPlugin
{
    private HypeBrowserDock? _dock;
    private EditorDock? _editorDock;

    public override void _EnterTree()
    {
        HypeProjectSettings.EnsureDefaults();

        _dock = new HypeBrowserDock();
        _dock.Initialize(EditorInterface.Singleton);
        _dock.Name = nameof(HypeBrowserDock);

        _editorDock = new EditorDock
        {
            Name = "HypeBrowserDockContainer",
            Title = "Hype Browser",
            DefaultSlot = EditorDock.DockSlot.LeftUl
        };
        _editorDock.AddChild(_dock);
        AddDock(_editorDock);
    }

    public override void _ExitTree()
    {
        if (_dock == null || _editorDock == null)
        {
            return;
        }

        RemoveDock(_editorDock);
        _editorDock.QueueFree();
        _editorDock = null;
        _dock = null;
    }
}
