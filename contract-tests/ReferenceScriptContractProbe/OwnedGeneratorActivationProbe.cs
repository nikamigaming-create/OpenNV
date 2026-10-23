using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedGeneratorActivationProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = records.GetEffective(new("FalloutNV.esm", 0xe5df9));
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, reference) ?? throw new InvalidDataException("Generator has no source script.");
        Require(reference.Signature == "REFR" && basis.Signature == "ACTI" && basis.FormKey == new FalloutFormKey("FalloutNV.esm", 0x8d36f) &&
            script.FormKey == new FalloutFormKey("FalloutNV.esm", 0x8d19b), "Selected generator source chain changed.");
        var fields = script.ReadSubrecords().ToArray();
        Require(Hash(fields.Single(field => field.Signature == "SCTX").Data.Span) == "9C8BD7512BB7C7F70E8B5B9F1FFE82EB1CBF4B12F54B2A04EDDC226BC3F3742E" &&
            Hash(fields.Single(field => field.Signature == "SCDA").Data.Span) == "7D901C6EF1FBFE4757D52F746CAB934CB515371CC122E8A77E56113E8BE76AEE",
            "Owned source/compiled control structure differs from the qualified generator.");
        var bindings = new FalloutScriptBindings(records, reference, script, fields);
        var player = bindings.Reference("player");
        var message = bindings.Form("QJGeneratorActiveMsg").FormKey;
        var originals = new[] { reference, basis, script, records.GetEffective(message) }
            .Select(record => (Record: record, Hash: Hash(record.ReadData()))).ToArray();
        foreach (var activator in new[] { reference.FormKey, player })
        {
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, world.Get(reference.FormKey).Cell);
            // Selected source reference only: this fixture is not campaign/cell admission.
            cell = cell with { References = cell.References.Where(value => value.FormKey == reference.FormKey).ToArray() };
            Require(cell.References.Count == 1, "Actual source CELL lacks the generator.");
            world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var effects = new List<FalloutReferenceScriptEffect>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
            var before = JsonSerializer.Serialize(world.Capture());
            var result = scripts.Activate(reference.FormKey, activator);
            Require(result.Error is null && result.Blocks == 1 && world.Get(reference.FormKey).ScriptError is null,
                result.Error ?? "Actual owned activation did not execute exactly one source block.");
            CheckEffects(effects, activator == player);
            Require(before == JsonSerializer.Serialize(world.Capture()) && quests.Capture().Count == 0,
                "Activation changed generator locals/world/quests instead of only requesting its authored message.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(saved); cold.LoadCell(cell);
            Require(before == JsonSerializer.Serialize(cold.Capture()), "Generator state changed on cold restore.");
            var coldEffects = new List<FalloutReferenceScriptEffect>();
            var coldScripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false, coldEffects.Add));
            var retried = coldScripts.Activate(reference.FormKey, activator);
            Require(retried.Error is null && retried.Blocks == 1 && before == JsonSerializer.Serialize(cold.Capture()),
                "Cold activation lost the authored guard or changed reference state.");
            CheckEffects(coldEffects, activator == player);
        }
        Require(originals.All(value => value.Hash == Hash(value.Record.ReadData())), "Owned generator source bytes changed.");
        Console.WriteLine("OPENNV_OWNED_GENERATOR_ACTIVATION_PASS actualOnActivate=true compiledBindings=true playerGuard=true " +
            "cold=true sourceUnchanged=true controlledActivator=true physicalInputMenuModeRepairSalvageAndRetail=unverified");

        void CheckEffects(IReadOnlyList<FalloutReferenceScriptEffect> effects, bool expected)
        {
            Require(expected ? effects.Count == 1 && effects[0] is { Kind: FalloutReferenceEffectKind.Message } &&
                effects[0].Source == reference.FormKey && effects[0].Target == message : effects.Count == 0,
                "Actual source player guard or authored message identity changed.");
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
