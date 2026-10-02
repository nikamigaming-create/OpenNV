using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptSourceStringProbe
{
    internal static void Owned(string selected, string game, string[] dependencies)
    {
        Require(RuntimeLiveContentSource.Current is null, "This source-isolation audit requires no ambient content source.");
        var setup = FalloutModInstallation.Detect("jam", selected, game, dependencies);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var script = FalloutDialogueTopic.Find(records, "SCPT", "JDCScript");
        var quest = records.EffectiveRecords("QUST").Single(record =>
            FalloutScriptLocals.AttachedScript(records, record)?.FormKey == script.FormKey);
        var fields = script.ReadSubrecords().ToArray();
        var source = FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span);
        var queries = Regex.Matches(source, @"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*GetNumericGameSetting\s+([A-Za-z_][A-Za-z0-9_]*)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).ToArray();
        Require(queries.Length == 11, "Selected JDC source does not contain the eleven expected bare-name assignments.");
        var executable = Path.Combine(setup.BaseInstallation.InstallRoot, "FalloutNV.exe");
        var executableHash = SHA256.HashData(File.ReadAllBytes(executable));
        var defaults = FalloutExecutableStringTable.ReadFloatDefaults(executable);
        var overlay = Path.Combine(Path.GetTempPath(), "opennv-jdc-source-string-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overlay);
        try
        {
            var storage = FalloutScriptStorage.Open(content, overlay);
            var ui = FalloutUiComponentStore.Open(records, content);
            using var world = new FalloutReferenceWorld(records, auxiliary: storage.Auxiliary, ini: storage.Ini, ui: ui, controls: storage.Controls);
            var state = new FalloutQuestState(records);
            var events = new FalloutScriptEvents();
            var globals = FalloutGlobalState.Read(records);
            var excluded = records.EffectiveRecords("QUST").Where(record => record.FormKey != quest.FormKey)
                .Select(record => record.FormKey).ToHashSet();
            var scripts = new FalloutQuestScripts(records, state, excluded, new FalloutPlayerInventory(), globals,
                FalloutInstallationSettings.Read(content).Number("MAIN", "fQuestScriptDelayTime"), world, events, storage);
            var executor = new FalloutReferenceScripts(records, world, state, new((_, _) => false,
                _ => throw new NotSupportedException("JDC audit has no presentation host."), Globals: globals, Events: events));
            scripts.Host = new((_, _) => throw new NotSupportedException("JDC audit has no quest-stage host."),
                _ => throw new NotSupportedException("JDC audit has no player actor-value host."), executor.ExecuteProgram, executor.InvokeFunction);
            events.LoadGame();
            for (var frame = 0; frame < 360; ++frame)
            {
                ui.AdvanceAnimations(1.0 / 60);
                scripts.Advance(1.0 / 60);
                events.Advance(1.0 / 60, true, executor.InvokeFunction);
            }
            var bindings = new FalloutScriptBindings(records, quest, script, fields);
            var declarations = FalloutScriptLocals.ReadDeclarations(script);
            var bytecode = fields.Single(field => field.Signature == "SCDA").Data;
            var values = queries.Select(query =>
            {
                var local = query.Groups[1].Value;
                var name = query.Groups[2].Value;
                var slot = bindings.Variable(local);
                Require(declarations[local].Kind == FalloutScriptLocalKind.Number, "JDC cached setting local is not numeric.");
                var matches = records.EffectiveRecords("GMST").Where(record =>
                    FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span)
                        .Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                Require(matches.Length <= 1, "JDC setting has ambiguous winning GMST ownership.");
                double expected;
                string origin;
                if (matches.Length == 1)
                {
                    var data = matches[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data;
                    Require(name[0] == 'f' && data.Length == 4, "JDC winning numeric declaration is not a Float32 GMST.");
                    expected = BinaryPrimitives.ReadSingleLittleEndian(data.Span);
                    origin = matches[0].FormKey.ToString();
                }
                else
                {
                    Require(defaults.TryGetValue(name, out _), "JDC setting has no admitted executable default: " + name);
                    expected = defaults[name];
                    origin = "owned-executable-default";
                }
                Require(double.IsFinite(expected), "JDC source numeric declaration is non-finite.");
                var encoded = new byte[2 + name.Length]; BinaryPrimitives.WriteUInt16LittleEndian(encoded, (ushort)name.Length);
                Encoding.ASCII.GetBytes(name).CopyTo(encoded, 2);
                Require(bytecode.Span.IndexOf(encoded) >= 0, "JDC bare-name source argument has no matching compiled literal.");
                var actual = state.Variable(slot.Owner, slot.Index);
                Require(actual == expected && records.NumericSettings.Get(name) == expected,
                    $"JDC cache {local} did not execute its actual source query: actual={actual}; expected={expected}.");
                return new { local, name, slot = slot.Index, actual, expected, origin, compiledLiteral = true };
            }).ToArray();
            Require(values.Count(value => value.origin == "owned-executable-default") == 9,
                "Selected JDC defaults/GMST denominator changed.");
            var reached = scripts.Capture().Instances.Single(instance => instance.Quest == quest.FormKey);
            Require(reached.Error?.Contains("unbound operation", StringComparison.Ordinal) == true && reached.Executions == 0,
                "JDC should retain its next unsupported INI query rather than claim complete initialization.");
            var after = source[(queries[^1].Index + queries[^1].Length)..];
            var next = FalloutDialogueTopic.CodeLines(after).First();
            Require(FalloutGameModeProgram.Tokens(next).Skip(2).First().Equals("GetNumericINISetting", StringComparison.OrdinalIgnoreCase),
                "The next authored JDC operation is not the expected unbound INI getter.");
            var saved = JsonSerializer.Serialize(state.Capture());
            scripts.Advance(1);
            Require(JsonSerializer.Serialize(state.Capture()) == saved, "JDC replayed the prefix after its unsupported suffix.");

            Reject(() => FalloutPluginStack.Load(content.PluginSources.Skip(1).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Select((entry, index) =>
                index == 0 ? entry with { OwnedSource = null } : entry).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Select((entry, index) =>
                index == 0 ? entry with { RegisteredBytes = entry.RegisteredBytes.GetValueOrDefault() + 1 } : entry).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Reverse().ToArray()));
            Require(content.PluginSources is System.Collections.ObjectModel.ReadOnlyCollection<FalloutPluginSource> &&
                !JsonSerializer.Serialize(content.PluginSources).Contains("OwnedSource", StringComparison.Ordinal),
                "Owned source provenance is mutable or serializes its in-process owner.");
            using var otherContent = setup.OpenSource();
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Select((entry, index) =>
                index == 0 ? otherContent.PluginSources[index] : entry).ToArray()));
            using var other = FalloutPluginStack.Load(otherContent.PluginSources);
            var sample = values.First(value => value.origin == "owned-executable-default");
            Require(other.NumericSettings.Get(sample.name) == sample.expected &&
                other.NumericSettings.Set(sample.name, sample.expected + 1) &&
                records.NumericSettings.Get(sample.name) == sample.expected &&
                other.NumericSettings.Get(sample.name) == (float)(sample.expected + 1),
                "Independent source graphs shared numeric declarations or mutations.");
            using var cold = FalloutPluginStack.Load(content.PluginSources);
            Require(cold.NumericSettings.Get(sample.name) == sample.expected && RuntimeLiveContentSource.Current is null,
                "A new stack retained another graph's overrides or required an ambient source.");
            Require(SHA256.HashData(File.ReadAllBytes(executable)).AsSpan().SequenceEqual(executableHash),
                "Owned executable bytes changed during the read-only audit.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-jdc-source-string-audit/v1",
                quest = quest.FormKey.ToString(),
                script = script.FormKey.ToString(),
                pluginSha256 = records.Plugins.Single(plugin => plugin.Plugin.Name == setup.EntryPlugin).Sha256,
                settings = values,
                sourceBinding = "explicit-complete-graph",
                ambientSource = false,
                sourceIsolation = true,
                failedPrefixRetained = true,
                executions = reached.Executions,
                error = reached.Error,
                nextOwner = "GetNumericINISetting",
                sourceReadonly = true,
                recording = false,
                boundary = "owned-JDC-cache-prefix-only;INI-and-equipment-queries-unbound;no-player-input-or-module-gameplay-acceptance",
            }));
        }
        finally
        {
            if (Directory.Exists(overlay)) Directory.Delete(overlay, recursive: true);
        }
    }
}
