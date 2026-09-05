namespace OpenNV.Runtime.Content;

internal static class FalloutNewVegasBuiltinForms
{
    // FNV's native TESObjectSTAT::IsInternal predicate compares these engine
    // bootstrap forms. Match runtime identity, not EDID, model path, or color:
    // overriding a builtin's model does not turn it into visible world art.
    internal static bool IsInternalStatic(string signature, uint runtimeFormId) =>
        signature == "STAT" && runtimeFormId is
            0x01 or 0x02 or 0x06 or 0x05 or 0x03 or 0x10 or 0x12 or
            0x3b or 0x34 or 0x32 or 0x15 or 0x33 or 0x23 or 0x24;
}
