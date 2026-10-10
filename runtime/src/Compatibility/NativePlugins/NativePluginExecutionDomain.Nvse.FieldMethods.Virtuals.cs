using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeSourceVirtualFieldEvent(ulong Generation, ulong Module, uint Thread, ulong Callback,
    ulong Caller, ulong Object, uint Method, string RuntimeSha256, string? BodySha256, uint? Result, string? Failure);

internal sealed partial class NativePluginExecutionDomain
{
    private readonly List<NativeSourceVirtualFieldEvent> _sourceVirtualFieldEvents = [];
    internal IReadOnlyList<NativeSourceVirtualFieldEvent> SourceVirtualFieldEvents => _sourceVirtualFieldEvents.AsReadOnly();

    // The existing genuine native thiscall vtable producer already supplies
    // this callback and its real expression/campaign caller. Resolve the
    // selected class's actual virtual declaration before evaluating its fields;
    // class identity alone no longer supplies a constant successful result.
    private uint ReadSourceVirtualFieldMethod(NativeNvsePlugin plugin, NativeNvseSourceObject value, uint slot,
        ulong callback, ulong caller)
    {
        VerifyNvseSourceObject(plugin, value);
        var host = _nvseHostSource ?? throw new InvalidDataException("Source virtual field method lost its selected engine lease.");
        host.Check(Generation);
        FalloutEngineFieldMethod? declaration = null; uint? result = null;
        try
        {
            _sourceFieldMethodSource ??= FalloutExecutableStringTable.ReadEngineFieldMethodSource(host.RuntimePath, host.RuntimeSha256);
            declaration = _sourceFieldMethodSource.ReadVirtual(value.Class, checked((int)slot));
            ++value.Calls;
            try
            {
                var actual = EvaluateSourceFieldExpression(plugin, declaration, NativePluginAbi.Thiscall, value.Class, value);
                if (actual.Role != NativeSourceFieldValueRole.Scalar)
                    throw new InvalidDataException("Source scalar/Boolean virtual returned an unowned pointer role.");
                result = slot is 0xf0 or 0x100 ? (actual.Bits & 255U) != 0 ? 1U : 0U : actual.Bits;
                _sourceVirtualFieldEvents.Add(new(Generation, plugin.Module, NativeThread, callback, caller, value.Id, slot,
                    host.RuntimeSha256, declaration.BodySha256, result, null)); return result.Value;
            }
            finally { --value.Calls; }
        }
        catch (Exception failure)
        {
            _sourceVirtualFieldEvents.Add(new(Generation, plugin.Module, NativeThread, callback, caller, value.Id, slot,
                host.RuntimeSha256, declaration?.BodySha256, result, failure.Message)); throw;
        }
    }
}
