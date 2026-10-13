using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompiledPackageProbe
{
    // This audits original winning header/record identity. Actual package
    // production, callbacks, frame timing and campaign play are separate lanes.
    internal static int Run(string root, string game, string[] options)
    {
        var roots = new List<string>();
        var plugins = new List<string>();
        for (var index = 0; index < options.Length; index += 2)
        {
            if (index + 1 >= options.Length) throw new ArgumentException("Package audit option has no value.");
            switch (options[index])
            {
                case "--mod-root": roots.Add(options[index + 1]); break;
                case "--plugin": plugins.Add(options[index + 1]); break;
                default: throw new ArgumentException("Unknown package audit option: " + options[index]);
            }
        }
        using var content = RuntimeLiveContentSource.Open(root, game, roots, plugins.Count == 0 ? null : plugins);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var scripts = 0;
        var compiled = 0;
        var callbacks = new List<object>();
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
                foreach (var (block, ordinal) in program.Events.Select((block, ordinal) => (block, ordinal))
                    .Where(row => FalloutCompiledScriptEvents.IsPackage(row.block.Event)))
                {
                    var filter = FalloutCompiledScriptEvents.ActorCallbackFilter(program, block)
                        ?? throw new InvalidDataException("Compiled package callback has no required source filter.");
                    var package = filter.Form ?? throw new NotSupportedException("Package filter has no fixed source binding.");
                    FalloutReferencePackageEvents.RequirePackage(records, package);
                    var selected = records.GetEffective(package);
                    callbacks.Add(new
                    {
                        script = record.FormKey.ToString(), scriptWinner = record.Plugin.Name,
                        scriptSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())), ordinal,
                        @event = FalloutCompiledScriptEvents.Name(block.Event), filter.Index,
                        package = package.ToString(), packageWinner = selected.Plugin.Name,
                        packageSha256 = Convert.ToHexString(SHA256.HashData(selected.ReadData()))
                    });
                }
            }
            catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            {
                failures.Add(new { script = record.FormKey.ToString(), winner = record.Plugin.Name, error = failure.Message });
            }
        }
        Console.WriteLine("OPENNV_OWNED_COMPILED_PACKAGE_HEADER_REPORT " + JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-compiled-package-headers/v1", game, content.StackId,
            plugins = content.PluginSources.Select(source => source.Name).ToArray(), scripts, compiled,
            callbackCount = callbacks.Count, callbacks, uncompiled, failures,
            sourceReadOnly = true, renderer = "NONE", gameplay = "UNEXECUTED",
            actualLifecycleCallbacks = "UNEXECUTED", variableFilterCacheLifetime = "UNOWNED", parity = "UNVERIFIED"
        }));
        if (failures.Count != 0) return 1;
        Console.WriteLine("OPENNV_OWNED_COMPILED_PACKAGE_HEADERS_PASS winningGraph=true requiredScroPack=true sourceReadOnly=true renderer=NONE gameplay=UNEXECUTED");
        return 0;
    }
}
