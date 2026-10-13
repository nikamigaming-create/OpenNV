using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private static bool IsTriggerEvent(string name) =>
        name.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OnTriggerEnter", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OnTriggerLeave", StringComparison.OrdinalIgnoreCase);

    // Physics publishes contact identity; the interpreter owns filtering and
    // script execution. Leave may refer to a body that is no longer resident.
    private FalloutReferenceScriptEvent AdmitTriggerEvent(FalloutReferenceScriptEvent delivered)
    {
        var continuous = delivered.Name.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase);
        if (delivered.Topic is not null || delivered.Topics is not null)
            throw new InvalidDataException("Trigger callbacks cannot borrow speech-completion identity.");
        if (continuous)
        {
            if (delivered.ActionReference is not null || delivered.TriggerReferences is not { Count: > 0 } contacts)
                throw new InvalidDataException("OnTrigger requires current contact membership and no action reference.");
            var retained = new HashSet<FalloutFormKey>(contacts);
            foreach (var contact in retained) RequireTriggerReference(contact);
            return delivered with { TriggerReferences = retained };
        }
        if (delivered.TriggerReferences is not null || delivered.ActionReference is not { } subject)
            throw new InvalidDataException("Trigger enter/leave requires its one actual contact subject.");
        RequireTriggerReference(subject);
        return delivered;
    }

    private void RequireTriggerReference(FalloutFormKey reference) =>
        FalloutTriggerContacts.RequireReference(records, reference);

    private bool MatchesCompiledTriggerCallback(FalloutCompiledScriptProgram program, FalloutCompiledEvent block,
        FalloutReferenceScriptEvent delivered, FalloutFormKey caller, FalloutCompiledReference? filter)
    {
        if (filter is null) return true;
        FalloutFormKey selected;
        if (filter.Form is { } fixedReference) selected = fixedReference;
        else
        {
            var slot = filter.Variable ?? throw new InvalidDataException("Compiled trigger filter has no declared binding.");
            var value = ReadCompiledStoredVariable(program.Source, caller, slot, (byte)'f');
            if (value.Kind != FalloutScriptValueKind.Form)
                throw new InvalidDataException("Compiled trigger filter variable is not reference storage.");
            if (value.Number == 0) return false;
            selected = value.FormKey(records);
        }
        RequireTriggerReference(selected);
        return block.Event == FalloutCompiledScriptEvents.Trigger
            ? delivered.TriggerReferences!.Contains(selected)
            : delivered.ActionReference == selected;
    }
}
