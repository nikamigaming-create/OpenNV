namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGamebryoStartMenu
{
    // Launcher input enters the same admitted original menu consumer. New Game
    // retains the owned confirmation; Continue still requires full cold admission.
    internal bool RequestLauncherEntry(NativeGodotLauncherEntry entry)
    {
        if (entry == NativeGodotLauncherEntry.Menu) return false;
        if (_records is null) throw new InvalidOperationException("Launcher entry requires the indexed source menu.");
        var action = entry switch
        {
            NativeGodotLauncherEntry.NewGame => "sNew",
            NativeGodotLauncherEntry.Continue => "sContinue",
            _ => throw new InvalidDataException("Unknown native launcher entry."),
        };
        var index = Array.IndexOf(_actions, action);
        if (index < 0 || _buttons[index].Disabled)
            throw new NotSupportedException("The selected source menu cannot admit " +
                (entry == NativeGodotLauncherEntry.Continue ? "this saved game. Choose New Game or another save." : "New Game at this time."));
        Activate(action);
        return true;
    }
}
