namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutQuestScripts
{
    internal void ShowCompiledMessage(FalloutFormKey form, FalloutScriptMessageCall call)
    {
        call.Validate();
        var program = _records.GetEffective(call.Program);
        if (program.Signature is not ("SCPT" or "QUST" or "INFO" or "PACK" or "TERM"))
            throw new InvalidDataException("Compiled message has no original script/result program owner.");
        var player = _records.RuntimeFormId(call.Caller) == 0x14;
        var signature = player ? null : _records.GetEffective(call.Caller).Signature;
        var placed = !player && signature is "REFR" or "ACHR" or "ACRE";
        if (!placed && !player && signature != "QUST")
            throw new InvalidDataException("Compiled message has no actual reference/quest/player caller.");
        var message = FalloutSourceMessage.Read(_records.GetEffective(form));
        if (call.Substitutions.Count != 0 || message.Text.Contains('%'))
            throw new NotSupportedException("Compiled message numeric formatting requires an authoritative formatting owner.");
        // GetButtonPressed chooses this same actual reference or executing
        // program. A QUST caller must not replace its executing SCPT slot.
        ShowMessage(form, call.Program, placed ? call.Caller : null);
    }
}
