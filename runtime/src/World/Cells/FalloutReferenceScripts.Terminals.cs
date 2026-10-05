using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    // The actual placed terminal remains the caller even in a child TERM page.
    // The selected page and source ordinal supply only the compiled result scope.
    internal void ExecuteTerminalResult(FalloutTerminalSelection selection)
    {
        if (selection is null || selection.Generation <= 0 || selection.Entry.Index < 0)
            throw new InvalidDataException("Terminal result has no source selection receipt.");
        if (!world.IsResident(selection.Reference) || !world.CanActivate(selection.Reference))
            throw new InvalidOperationException("Terminal result requires its resident activatable reference.");
        var reference = records.GetEffective(selection.Reference);
        if (reference.Signature != "REFR" || records.GetEffective(world.Get(selection.Reference).Base).Signature != "TERM")
            throw new InvalidDataException("Terminal result caller is not an actual placed TERM reference.");
        var current = FalloutTerminal.Read(records, selection.Page.Record.FormKey);
        if (current.SourceHash != selection.Page.SourceHash || selection.Entry.Index >= current.Entries.Count)
            throw new InvalidDataException("Terminal result source scope changed after selection.");
        var entry = current.Entries[selection.Entry.Index];
        if (entry.Program.Identity != selection.Entry.Program.Identity)
            throw new InvalidDataException("Terminal result fragment differs from its selection receipt.");
        entry.Program.RequireSourceExecution();
        Execute(selection.Reference, Bindings(reference, current.Record, entry.Program.Fields),
            FalloutGameModeProgram.Read("begin Result\n" + entry.Program.Source + "\nend", "Result"), null, 0);
    }
}
