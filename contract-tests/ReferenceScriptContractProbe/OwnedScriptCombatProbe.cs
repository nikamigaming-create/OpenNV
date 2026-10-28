using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedScriptCombatProbe
{
    internal static void FindMissingBases(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var missing = records.RuntimeFormKey(0x17);
        var primitives = 0;
        var other = 0;
        FalloutFormKey? sample = null;
        foreach (var signature in new[] { "REFR", "ACHR", "ACRE" })
        foreach (var reference in records.EffectiveRecords(signature))
        {
            var names = reference.ReadSubrecords().Where(field => field.Signature == "NAME").ToArray();
            if (names.Length != 1 || names[0].Data.Length != 4) continue;
            var form = reference.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span));
            if (form != missing) continue;
            if (reference.ReadSubrecords().Any(field => field.Signature == "XPRM")) { primitives++; sample ??= reference.FormKey; }
            else other++;
        }
        if (sample is null || other != 0) throw new InvalidDataException("Primitive source population changed.");
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, world.Get(sample.Value).Cell);
        var selected = cell.References.Where(reference => reference.FormKey == sample.Value).ToArray();
        if (selected.Length != 1) throw new InvalidDataException("Primitive source cell lost its reference.");
        world.LoadCell(cell with { References = selected });
        var actor = new FalloutFormKey("FalloutNV.esm", 0x17b049);
        var package = FalloutSandboxPackage.Read(records.GetEffective(new FalloutFormKey("FalloutNV.esm", 0x074fe6)));
        package = package with { Activities = new((ushort)(package.Activities.Exclusions | 1)) };
        var candidates = world.SandboxCandidateSources(actor, package, 1, null, .0142875f);
        if (candidates.Any(candidate => candidate.Reference == sample))
            throw new InvalidDataException("Engine primitive became a sandbox activity candidate.");
        Console.WriteLine($"OPENNV_ENGINE_PRIMITIVE_SOURCE_AUDIT base={missing} xprmReferences={primitives} otherReferences={other} sandboxScan=true nativeTrigger=unverified");
    }

    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var owner = records.GetEffective(new FalloutFormKey("FalloutNV.esm", 0x168d19));
        var script = FalloutScriptLocals.AttachedScript(records, owner) ?? throw new InvalidDataException("Source reference has no script.");
        var source = FalloutDialogueTopic.ScriptText(script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
        var line = FalloutDialogueTopic.CodeLines(source).Single(value => value.Contains(".StartCombat ", StringComparison.OrdinalIgnoreCase));
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidDataException("Source combat statement is incomplete.");
        var binding = new FalloutScriptBindings(records, owner, script, script.ReadSubrecords());
        var actor = binding.Reference(parts[0].Split('.')[0]);
        var target = binding.Reference(parts[1]);
        var executor = new FalloutReferenceScripts(records, world, new(records),
            new((_, _) => false, _ => throw new InvalidDataException("Script combat invented a host effect.")));
        executor.ExecuteProgram(owner, script, FalloutGameModeProgram.Read("begin GameMode\n" + line + "\nend"), 0);
        if (world.Get(actor).Engagement?.Target != target) throw new InvalidDataException("Owned script combat lost its compiled target.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        if (cold.Get(actor).Engagement?.Target != target) throw new InvalidDataException("Owned script combat lost its cold target.");
        Console.WriteLine($"OPENNV_OWNED_SCRIPT_COMBAT_PASS script={script.FormKey} actor={actor} target={target} compiledBindings=true cold=true fixture=source-statement nativeCombat=unverified retail=unverified");
    }
}
