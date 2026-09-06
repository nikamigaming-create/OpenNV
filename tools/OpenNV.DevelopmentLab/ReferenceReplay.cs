using System.Diagnostics;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReferenceReplay
{
    private sealed record Checkpoint(IReadOnlyList<FalloutReferenceSnapshot> References,
        IReadOnlyList<FalloutQuestSnapshot> Quests);

    internal static int Lifecycle(FalloutPluginStack records, IReadOnlyList<string> cells)
    {
        var reports = new List<object>();
        var failures = new List<object>();
        var selected = cells.SequenceEqual(["--all"]) ? records.EffectiveRecords("CELL") :
            cells.Select(name => FalloutDialogueTopic.Find(records, "CELL", name)).ToArray();
        foreach (var cell in selected)
        {
            var nameField = cell.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID");
            var cellName = nameField.Data.IsEmpty ? cell.FormKey.ToString() : FalloutDialogueTopic.Text(nameField.Data.Span);
            try
            {
                var timer = Stopwatch.StartNew();
                var scene = FalloutCellSceneReader.Read(records, cell.FormKey);
                using var world = new FalloutReferenceWorld(records);
                var instances = world.LoadCell(scene);
                var firstLoad = timer.Elapsed.TotalMilliseconds;
                foreach (var instance in instances.Where(instance => instance.Variables.Count != 0))
                    instance.Write(instance.Variables.Keys.First(), records.RuntimeFormId(instance.Reference));
                var expected = JsonSerializer.Serialize(world.Capture());
                var before = world.InstanceCount;
                timer.Restart();
                for (var pass = 0; pass < 30; ++pass)
                {
                    world.UnloadCell(scene.Cell.FormKey);
                    world.LoadCell(scene);
                }
                if (world.InstanceCount != before || JsonSerializer.Serialize(world.Capture()) != expected)
                    throw new InvalidDataException("Repeated teardown reset, duplicated or leaked reference state.");
                var warmMilliseconds = timer.Elapsed.TotalMilliseconds / 30;
                var checkpoint = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(expected)!;
                using var restored = new FalloutReferenceWorld(records);
                restored.Restore(checkpoint);
                restored.LoadCell(scene);
                if (JsonSerializer.Serialize(restored.Capture()) != expected)
                    throw new InvalidDataException("Fresh world restoration changed exact reference state.");
                reports.Add(new { cellName, references = instances.Count, scripts = instances.Count(instance => instance.Script is not null),
                    world.ScriptDefinitionCount, firstLoadMilliseconds = firstLoad, warmMilliseconds,
                    reassemblies = 30, coldStateRoundtrip = true, stateIsolation = true });
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { cellName, error = error.Message }); }
            if ((reports.Count + failures.Count) % 5000 == 0)
                Console.Error.WriteLine($"LIFECYCLE cells={reports.Count + failures.Count}/{selected.Count} failures={failures.Count}");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-reference-lifecycle-lab/v1", reports, failures,
            boundary = "Disposable headless reference/script state; graphics, physics, ordinary input and parity remain unverified." }));
        return failures.Count == 0 ? 0 : 1;
    }

    internal static int Run(FalloutPluginStack records, string path)
    {
        using var scenario = JsonDocument.Parse(File.ReadAllText(path));
        var effects = new List<FalloutReferenceScriptEffect>();
        var reports = new List<object>();
        var furniture = new Dictionary<FalloutFormKey, FalloutFormKey>();
        var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var host = new FalloutReferenceScriptHost((actor, seat) => furniture.TryGetValue(actor, out var occupied) && occupied == seat, effects.Add);
        var scripts = new FalloutReferenceScripts(records, world, quests, host);
        var byEditorId = new Dictionary<string, FalloutFormKey>(StringComparer.OrdinalIgnoreCase);
        FalloutFormKey Form(string name)
        {
            if (name.Equals("player", StringComparison.OrdinalIgnoreCase)) return records.RuntimeFormKey(0x14);
            if (uint.TryParse(name, System.Globalization.NumberStyles.HexNumber, null, out var id)) return records.RuntimeFormKey(id);
            if (byEditorId.TryGetValue(name, out var key)) return key;
            var matches = new[] { "CELL", "REFR", "ACHR", "ACRE", "QUST" }.SelectMany(records.EffectiveRecords)
                .Where(record => record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                    FalloutDialogueTopic.Text(field.Data.Span).Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"Lab selector {name} is absent or ambiguous.");
            return byEditorId[name] = matches[0].FormKey;
        }
        uint Slot(FalloutFormKey form, string name) => FalloutScriptLocals.Read(
            FalloutScriptLocals.AttachedScript(records, records.GetEffective(form)) ??
                throw new InvalidDataException("Lab variable target has no script."))[name];
        var stepIndex = 0;
        string? failure = null;
        try
        {
            foreach (var step in scenario.RootElement.GetProperty("steps").EnumerateArray())
            {
                var operation = step.GetProperty("operation").GetString()!;
                string Text(string name) => step.GetProperty(name).GetString()!;
                FalloutFormKey Target() => Form(Text("target"));
                double Value() => step.GetProperty("value").GetDouble();
                switch (operation)
                {
                    case "load": world.LoadCell(FalloutCellSceneReader.Read(records, Target())); break;
                    case "unload": scripts.UnloadCell(Target()); world.UnloadCell(Target()); break;
                    case "furniture": furniture[Target()] = Form(Text("seat")); break;
                    case "objective": quests.ApplyObjective(new(Text("target"), step.GetProperty("index").GetUInt32(),
                        Text("state") == "displayed", step.GetProperty("value").GetBoolean())); break;
                    case "quest-variable": quests.SetVariable(Target(), Slot(Target(), Text("name")), Value()); break;
                    case "reference-variable": world.Get(Target()).Write(Slot(Target(), Text("name")), Value()); break;
                    case "event":
                        var result = scripts.Dispatch(Target(), Text("name"),
                            step.TryGetProperty("actor", out var actor) ? Form(actor.GetString()!) : null,
                            step.TryGetProperty("seconds", out var seconds) ? seconds.GetDouble() : 0);
                        reports.Add(result);
                        if (result.Error is not null) throw new InvalidDataException(result.Error);
                        break;
                    case "assert-reference":
                        if (world.Get(Target()).Read(Slot(Target(), Text("name"))) != Value())
                            throw new InvalidDataException($"Reference {Text("target")}.{Text("name")} differs from {Value()}.");
                        break;
                    case "assert-quest":
                        if (quests.Variable(Target(), Slot(Target(), Text("name"))) != Value())
                            throw new InvalidDataException($"Quest {Text("target")}.{Text("name")} differs from {Value()}.");
                        break;
                    case "assert-effects":
                        if (effects.Count(effect => effect.Kind.ToString() == Text("kind")) != Value())
                            throw new InvalidDataException($"Effect count for {Text("kind")} differs from {Value()}.");
                        break;
                    case "cold-restore":
                        var bytes = JsonSerializer.Serialize(new Checkpoint(world.Capture(), quests.Capture()));
                        var restored = JsonSerializer.Deserialize<Checkpoint>(bytes)!;
                        world.Dispose();
                        world = new(records);
                        quests = new(records);
                        world.Restore(restored.References);
                        quests.Restore(restored.Quests);
                        scripts = new(records, world, quests, host);
                        if (JsonSerializer.Serialize(new Checkpoint(world.Capture(), quests.Capture())) != bytes)
                            throw new InvalidDataException("Cold script/reference/quest checkpoint changed.");
                        break;
                    default: throw new InvalidDataException($"Unknown lab operation {operation}.");
                }
                ++stepIndex;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
        { failure = error.Message; }
        finally { world.Dispose(); }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-reference-event-replay/v1", path,
            completedSteps = stepIndex, reports, effects, failure,
            boundary = "Disposable OpenNV state; host furniture and effects are isolated test inputs/outputs. No ordinary gameplay or parity claim." }));
        return failure is null ? 0 : 1;
    }
}
