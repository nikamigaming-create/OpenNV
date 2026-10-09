namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    internal NativeNvseExpressionCallerReceipt CallNvseSourceCommand(NativeNvsePlugin plugin, NativeNvseCommand command,
        NativePluginGuestAllocation scriptData, NativeNvseExpressionArgumentOwner arguments, NativeNvseLocalContext locals,
        NativeNvseSourceObject script, IReadOnlyList<NativeNvseSourceObject> sourceObjects,
        NativeNvseValueResultTarget? resultTarget = null)
    {
        VerifyNvseSourceObject(plugin, script); VerifyNvseLocalContext(plugin, locals);
        if (!_nvseScriptInterface || script.Class != NativeNvseSourceClass.Script || !ReferenceEquals(locals.Script, script) ||
            !ReferenceEquals(script.Code, scriptData) || sourceObjects is null || !sourceObjects.Contains(script) ||
            sourceObjects.Distinct().Count() != sourceObjects.Count)
            throw new InvalidDataException("Original command has no exact Script/code/local/source graph ownership.");
        RequireNvseLocalCode(locals, scriptData);
        foreach (var value in sourceObjects) VerifyNvseSourceObject(plugin, value);
        foreach (var value in sourceObjects) ++value.Calls;
        try { return CallNvseCommand(plugin, command, scriptData, arguments, resultTarget, locals, script, sourceObjects); }
        finally { foreach (var value in sourceObjects) --value.Calls; }
    }
}
