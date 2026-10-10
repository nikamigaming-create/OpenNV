using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    // A genuine caller supplies the source-declared method entry and its ABI,
    // never a retail execution result. The selected module and already-published
    // graph are the same living campaign identities used by original commands.
    internal NativeSourceFieldMethodReceipt CallEngineSourceFieldMethod(string logicalModule, uint originalEntry,
        NativePluginAbi abi, FalloutFormKey? sourceForm)
    {
        RequireCurrent();
        var modules = _modules.Where(value => StringComparer.OrdinalIgnoreCase.Equals(value.Admission.LogicalPath, logicalModule)).ToArray();
        if (modules.Length != 1) throw new InvalidDataException("Source engine caller has no unique actual selected native module.");
        var module = modules[0];
        if (module.Retired || module.ObjectFailure is not null || module.Plugin.Phase != NativeNvsePhase.LoadedTrue || module.Domain.Fault is not null)
            throw new InvalidOperationException("Source engine caller retains an absent/failed native campaign generation.");
        NativeNvseSourceObject? receiver = null;
        if (sourceForm is { } key)
        {
            var objects = module.GraphObjects.Values.Concat(module.Objects.Values).Distinct().Where(value =>
                value.FormId == _records.RuntimeFormId(key)).ToArray();
            if (objects.Length != 1 || objects[0].Staged || objects[0].Retired)
                throw new NotSupportedException("Source engine caller requires its actual complete published form graph, not an actor descriptor.");
            receiver = objects[0];
        }
        if (abi == NativePluginAbi.Thiscall && receiver is null || abi == NativePluginAbi.Cdecl && receiver is not null)
            throw new InvalidDataException("Source campaign field invocation changed its declared receiver ABI.");
        ++_active;
        try
        {
            var method = module.Domain.PrepareNvseSourceFieldMethod(module.Plugin, originalEntry, abi, receiver?.Class);
            return module.Domain.CallNvseSourceFieldMethod(module.Plugin, method, receiver);
        }
        catch (Exception failure) { module.ObjectFailure ??= failure.Message; throw; }
        finally { --_active; }
    }
}
