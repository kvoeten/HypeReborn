using Godot;
using HypeReborn.Hype.Config;

namespace HypeReborn.Hype.EditorPlugin;

[Tool]
public partial class HypeBrowserPlugin : Godot.EditorPlugin
{
    private HypeBrowserDock? _dock;

    public override void _EnterTree()
    {
        HypeProjectSettings.EnsureDefaults();

        _dock = new HypeBrowserDock();
        _dock.Initialize(GetEditorInterface());
        _dock.Name = "Hype Browser";

        AddControlToDock(DockSlot.LeftUl, _dock);
    }

    public override void _ExitTree()
    {
        if (_dock == null)
        {
            return;
        }

        RemoveControlFromDocks(_dock);
        _dock.QueueFree();
        _dock = null;
    }
}
