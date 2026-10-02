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
        var iniQuery = Regex.Match(source, @"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*GetNumericINISetting\s+""([^""]+)""\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Require(iniQuery.Success, "JDC does not contain its expected numeric INI assignment.");
        var iniName = iniQuery.Groups[2].Value;
        var iniDeclarations = FalloutExecutableStringTable.ReadIniDeclarations(executable);
        var iniDeclaration = iniDeclarations.Single(row => row.Name.Equals(iniName, StringComparison.OrdinalIgnoreCase));
        Require(iniDeclaration.Collection == FalloutIniCollection.Main && iniDeclaration.Kind == 'f',
            "JDC INI source declaration is not a Main Float32 setting.");
        var iniExpected = (double)BitConverter.Int32BitsToSingle(unchecked((int)iniDeclaration.Payload));
        Require(double.IsFinite(iniExpected), "JDC INI source declaration is non-finite.");
        var iniOrigin = "owned-executable-default";
        var userIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FalloutNV");
        var iniFiles = new[] { Path.Combine(setup.BaseInstallation.InstallRoot, "Fallout_default.ini"),
            Path.Combine(userIni, "Fallout.ini"), Path.Combine(userIni, "FalloutCustom.ini") };
        var iniHashes = iniFiles.Where(File.Exists).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        foreach (var path in iniFiles)
            if (ReadOwnedIniFloat(path, iniName) is { } value) { iniExpected = value; iniOrigin = path; }
        foreach (var row in content.Settings)
            if ((row.Key + ":" + row.Section).Equals(iniName, StringComparison.OrdinalIgnoreCase))
            { iniExpected = float.Parse(row.Value, System.Globalization.CultureInfo.InvariantCulture); iniOrigin = "profile"; }
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
            var iniSlot = bindings.Variable(iniQuery.Groups[1].Value);
            var iniEncoded = new byte[2 + iniName.Length]; BinaryPrimitives.WriteUInt16LittleEndian(iniEncoded, (ushort)iniName.Length);
            Encoding.ASCII.GetBytes(iniName).CopyTo(iniEncoded, 2);
            Require(iniSlot.Index == 15 && bytecode.Span.IndexOf(iniEncoded) >= 0 &&
                state.Variable(iniSlot.Owner, iniSlot.Index) == iniExpected && records.IniSettings.Get(iniName) == iniExpected,
                "JDC did not execute its compiled world-FOV query with independently resolved file/profile precedence.");
            Require(reached.Error is null && reached.Executions > 0, "JDC initializer did not complete: " + reached.Error);
            var afterIni = source[(iniQuery.Index + iniQuery.Length)..];
            Require(afterIni.Contains("IsModLoaded", StringComparison.OrdinalIgnoreCase) &&
                state.Variable(bindings.Variable("fDefaultDistanceMult").Owner, bindings.Variable("fDefaultDistanceMult").Index) > 0 &&
                state.Variable(bindings.Variable("iMode").Owner, bindings.Variable("iMode").Index) == -1 &&
                state.Variable(bindings.Variable("iHUDEditor").Owner, bindings.Variable("iHUDEditor").Index) ==
                    (records.Plugins.Any(plugin => plugin.Plugin.Name.Equals("TheHUDEditor.esm", StringComparison.OrdinalIgnoreCase)) ? 1 : 0),
                "JDC did not finish its ordinary authored loaded-plugin branch and suffix.");
            var eventState = JsonSerializer.SerializeToElement(events.State);
            var callbackFaults = eventState.GetProperty("mainLoop").EnumerateArray()
                .Where(row => row.GetProperty("Error").ValueKind == JsonValueKind.String).ToArray();
            Require(callbackFaults.Length > 0, "The expected callback boundary changed; inspect the complete source route.");

            Reject(() => FalloutPluginStack.Load(content.PluginSources.Skip(1).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Select((entry, index) =>
                index == 0 ? entry with { OwnedSource = null } : entry).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Select((entry, index) =>
                index == 0 ? entry with { RegisteredBytes = entry.RegisteredBytes.GetValueOrDefault() + 1 } : entry).ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources.Reverse().ToArray()));
            Reject(() => FalloutPluginStack.Load(content.PluginSources, false, out _,
                FalloutInstallationSettings.ReadIniLayers(() => [], [], "foreign-owner")));
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
            Require(iniHashes.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).AsSpan().SequenceEqual(pair.Value)),
                "Owned INI files changed during the read-only audit.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-jdc-source-string-audit/v2",
                quest = quest.FormKey.ToString(),
                script = script.FormKey.ToString(),
                pluginSha256 = records.Plugins.Single(plugin => plugin.Plugin.Name == setup.EntryPlugin).Sha256,
                settings = values,
                iniSetting = new { name = iniName, slot = iniSlot.Index, actual = state.Variable(iniSlot.Owner, iniSlot.Index),
                    expected = iniExpected, origin = iniOrigin, canonicalKind = iniDeclaration.Kind,
                    collection = iniDeclaration.Collection.ToString(), compiledLiteral = true },
                sourceBinding = "explicit-complete-graph",
                ambientSource = false,
                sourceIsolation = true,
                executions = reached.Executions,
                error = reached.Error,
                callbackFaults,
                nextOwner = "source main-loop callback equipment/extra-reference ownership",
                sourceReadonly = true,
                recording = false,
                boundary = "owned-JDC-initializer-only;callback-equipment-unbound;no-player-input-or-module-gameplay-acceptance",
            }));
        }
        finally
        {
            if (Directory.Exists(overlay)) Directory.Delete(overlay, recursive: true);
        }
    }

    private static float? ReadOwnedIniFloat(string path, string identity)
    {
        if (!File.Exists(path)) return null;
        var separator = identity.IndexOf(':');
        var key = identity[..separator]; var expectedSection = identity[(separator + 1)..];
        var section = ""; float? found = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var equals = line.IndexOf('=');
            if (equals <= 0 || !section.Equals(expectedSection, StringComparison.OrdinalIgnoreCase) ||
                !line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            found = float.Parse(line[(equals + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        return found;
    }
}
