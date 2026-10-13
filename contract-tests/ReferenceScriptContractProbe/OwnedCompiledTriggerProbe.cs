using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedCompiledTriggerProbe
{
    // Read every winning standalone script in the selected graph. This checks
    // original header bindings, not campaign execution or native contact parity.
    internal static int Run(string root, string game, string[] options)
    {
        var roots = new List<string>();
        var plugins = new List<string>();
        for (var index = 0; index < options.Length; index += 2)
        {
            if (index + 1 >= options.Length) throw new ArgumentException("Trigger audit option has no value.");
            switch (options[index])
            {
                case "--mod-root": roots.Add(options[index + 1]); break;
                case "--plugin": plugins.Add(options[index + 1]); break;
                default: throw new ArgumentException("Unknown trigger audit option: " + options[index]);
            }
        }
        using var content = RuntimeLiveContentSource.Open(root, game, roots, plugins.Count == 0 ? null : plugins);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var scripts = 0;
        var compiled = 0;
        var callbacks = 0;
        var groups = new Dictionary<(string Event, string Binding, string Signature), int>();
        var uncompiled = new List<string>();
        var failures = new List<object>();
        foreach (var record in records.EffectiveRecords().Where(record => record.Signature == "SCPT"))
        {
            ++scripts;
            try
            {
                var fields = record.ReadSubrecords().ToArray();
                if (!FalloutCompiledScriptProgram.HasProgram(fields)) { uncompiled.Add(record.FormKey.ToString()); continue; }
                var program = FalloutCompiledScriptProgram.Read(record, FalloutScriptScope.Standalone(record), standalone: true);
                ++compiled;
                foreach (var block in program.Events.Where(block => FalloutCompiledScriptEvents.IsTrigger(block.Event)))
                {
                    ++callbacks;
                    var filter = FalloutCompiledScriptEvents.ActorCallbackFilter(program, block);
                    var signature = filter?.Form is { } form ? FalloutScriptBindings.IsPlayerReference(records, form)
                        ? "engine-player-reference" : records.GetEffective(form).Signature : "none";
                    if (filter?.Form is not null && signature is not ("REFR" or "ACHR" or "ACRE" or "engine-player-reference"))
                        throw new NotSupportedException("Trigger fixed filter has an unsupported source class: " + signature);
                    if (filter?.Variable is { } slot)
                    {
                        _ = FalloutScriptLocals.ReadDeclarations(record, FalloutScriptDeclarationAuthority.CompiledVanilla);
                        if (!fields.Any(field => field.Signature == "SCRV" && field.Data.Length == sizeof(uint) &&
                            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == slot))
                            throw new InvalidDataException("Trigger variable filter lost its original reference storage declaration.");
                    }
                    var binding = filter is null ? "unfiltered" : filter.Form is not null ? "fixed-reference" : "live-reference-variable";
                    var key = (FalloutCompiledScriptEvents.Name(block.Event), binding, signature);
                    groups[key] = groups.GetValueOrDefault(key) + 1;
                }
            }
            catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            {
                failures.Add(new { script = record.FormKey.ToString(), winner = record.Plugin.Name, error = failure.Message });
            }
        }
        Console.WriteLine("OPENNV_OWNED_COMPILED_TRIGGER_HEADER_REPORT " + JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-compiled-trigger-headers/v1", game, content.StackId,
            plugins = content.PluginSources.Select(source => source.Name).ToArray(), scripts, compiled, callbacks,
            bindings = groups.OrderBy(row => row.Key.Event).ThenBy(row => row.Key.Binding).ThenBy(row => row.Key.Signature)
                .Select(row => new { row.Key.Event, row.Key.Binding, row.Key.Signature, count = row.Value }).ToArray(),
            uncompiled, failures, sourceReadOnly = true, renderer = "NONE", gameplay = "UNEXECUTED", nativeContactParity = "UNVERIFIED"
        }));
        if (failures.Count != 0) return 1;
        Console.WriteLine("OPENNV_OWNED_COMPILED_TRIGGER_HEADERS_PASS winningGraph=true headerBindings=true sourceReadOnly=true renderer=NONE gameplay=UNEXECUTED");
        return 0;
    }
}
