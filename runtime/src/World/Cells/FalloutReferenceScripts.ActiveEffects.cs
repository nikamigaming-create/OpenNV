using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    internal void ExecuteActiveEffect(FalloutFormKey target, FalloutPluginRecord script,
        FalloutGameModeProgram program, FalloutScriptEffectLocals locals)
    {
        if (records.RuntimeFormId(target) != 0x14 &&
            records.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Active-effect script target has no actual actor identity.");
        if (script.Signature != "SCPT" || locals.Script != script.FormKey)
            throw new InvalidDataException("Active-effect local cells do not belong to the invoked source script.");
        // SCPT supplies the genuine SCRO names; the actual target supplies the
        // calling reference. Local storage is the active effect, not world.Get.
        var bindings = Bindings(script, script, script.ReadSubrecords());
        foreach (var _ in Steps(target, bindings, program, null, 0, effectLocals: locals)) { }
    }
}
