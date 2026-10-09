namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // Prepare the real event list first, then close the Script/Quest cycle.
    // A script-less staged event list cannot become an original command frame.
    internal void AttachNvseLocalScript(NativeNvsePlugin plugin, NativeNvseLocalContext context, NativeNvseSourceObject script)
    {
        VerifyNvseLocalContext(plugin, context); VerifyNvseSourceObject(plugin, script); RequireNvseEmptyCall();
        if (context.Script is not null || context.Active != 0 || context.Transfer is not null || script.Class != NativeNvseSourceClass.Script ||
            script.Authority is not NativeNvseScriptAuthority authority ||
            !StringComparer.OrdinalIgnoreCase.Equals(context.Authority.SourceSha256, authority.SourceSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(context.Authority.CodeSha256, authority.CodeSha256))
            throw new InvalidDataException("Prepared event list has a competing owner or different actual Script declaration.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseLocalAttachScript,
                Payload(writer => { writer.Write(plugin.Module); writer.Write(context.Id); writer.Write(script.Id); }));
            if (reader.ReadUInt64() != context.Id || reader.ReadUInt32() != context.EventList || reader.ReadUInt32() != script.Address)
                throw new InvalidDataException("Native prepared event list did not close its genuine Script cycle.");
            Finish(reader); context.Script = script;
        }
        catch (Exception error) { throw Fatal(error); }
    }
}
