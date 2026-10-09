namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseArrayAccessorKind { Id, PublicOpaqueId, InternalObject }
internal static class NativeNvseArrayAccessor
{
    // Names alone never make unrelated pointer types interchangeable. The
    // opaque public declaration has no readable object layout. Internal views
    // require the complete release member layout consumed by the native owner.
    internal static NativeNvseArrayAccessorKind Read(string field, string abi, string? pointee)
    {
        if (field == "ScriptTokenGetArrayID" && abi == "uint32" && pointee is null) return NativeNvseArrayAccessorKind.Id;
        if (field == "ScriptTokenGetArray" && abi == "pointer32" && pointee == "NVSEArrayVarInterface::Array")
            return NativeNvseArrayAccessorKind.PublicOpaqueId;
        // A private field returning an internal class needs an independent
        // complete nested-type admission. The public host's ExtractArgsV has
        // its genuine ArrayVar** ABI below; that does not alias every pointer-
        // shaped client declaration or certify a different private class.
        throw new NotSupportedException("Original expression field11 return has no exact public opaque-ID/internal-object owner.");
    }
}
