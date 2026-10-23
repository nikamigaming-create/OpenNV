using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class OwnedCampfireBranchProbe
{
    internal static void Run(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var trigger = records.GetEffective(new("FalloutNV.esm", 0x15d9f4));
        var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(trigger, "NAME"));
        var script = FalloutScriptLocals.AttachedScript(records, trigger) ?? throw new InvalidDataException("Owned trigger has no script.");
        Require(trigger.Signature == "REFR" && basis.Signature == "ACTI" && basis.FormKey == new FalloutFormKey("FalloutNV.esm", 0x15d9ef) &&
            script.FormKey == new FalloutFormKey("FalloutNV.esm", 0x15d9ed), "Owned campfire source chain differs from the qualified records.");
        var fields = script.ReadSubrecords().ToArray();
        var text = fields.Single(field => field.Signature == "SCTX").Data;
        var compiled = fields.Single(field => field.Signature == "SCDA").Data;
        Require(Hash(text.Span) == "93B169995D741E19142BE1DF8B6BD484136BC498CF831810BC082515D435A7CF" &&
            Hash(compiled.Span) == "1478C913B4D17B6655398B8E3DA8E44D2519C8E95BBCE8039BE09C1B861676DA",
            "Owned script differs from the qualified source/compiled control flow.");
        var bindings = new FalloutScriptBindings(records, trigger, script, fields);
        var quest = bindings.Form("VCG03").FormKey; var player = bindings.Reference("Player");
        var powder = bindings.Form("NVHealingPowder").FormKey; var sunny = bindings.Reference("SunnyREF");
        var start = bindings.Variable("VCG03.nHealingPowdersAtStart");
        Require(start.Owner == quest && player == records.RuntimeFormKey(0x14) &&
            powder == new FalloutFormKey("FalloutNV.esm", 0x136a1d), "Actual compiled reference/variable bindings changed.");
        var originals = new[] { trigger, basis, script, records.GetEffective(quest), records.GetEffective(powder), records.GetEffective(sunny) }
            .Select(record => (Record: record, Hash: Hash(record.ReadData()))).ToArray();
        var source = FalloutDialogueTopic.ScriptText(text.Span);
        var block = FalloutGameModeProgram.ReadEvents(source).Single();
        Require(block.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase) && block.Filter?.Equals("player", StringComparison.OrdinalIgnoreCase) == true,
            "Owned script lost its original filtered event.");
        var identity = block.Program.ProgramSha256;
        foreach (var (starting, inventory, expected) in new (int, int, string[])[]
        {
            (0, 1, ["SetObjectiveDisplayed VCG03 20 0", "SetObjectiveDisplayed VCG03 30 1", "SetObjectiveCompleted VCG03 30 1", "SunnyREF.StartConversation player", "Disable", "MarkForDelete"]),
            (1, 1, ["SetObjectiveCompleted VCG03 30 1", "SunnyREF.StartConversation player", "Disable", "MarkForDelete"]),
            (1, 0, [])
        })
        {
            var calls = new List<string>();
            block.Program.Execute(name =>
            {
                Require(bindings.Variable(name) == start, "Unexpected source variable query.");
                return starting;
            }, (_, _) => throw new InvalidDataException("Owned source unexpectedly assigned a local."),
            (name, arguments) => calls.Add(string.Join(" ", new[] { name }.Concat(arguments))), Function);
            Require(calls.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase) && block.Program.ProgramSha256 == identity,
                "Owned branch changed effect order, admitted the dead tail, or changed source identity.");

            FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
            {
                "vcg03.nhealingpowdersatstart" => null, // The evaluator probes functions before bound variables.
                "player.getitemcount" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == powder, "Wrong item-count owner."); return inventory;
                }),
                "getquestcompleted" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == quest, "Wrong quest completion owner."); return 0;
                }),
                "getobjectivedisplayed" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
                {
                    Require(bindings.Form(arguments[0].Identifier!).FormKey == quest && arguments[1].Number == 10,
                        "Ignored Else payload queried objective 30."); return 1;
                }),
                _ => throw new InvalidDataException("Dead tail or unknown source function queried: " + name)
            };
        }
        Require(originals.All(pair => pair.Hash == Hash(pair.Record.ReadData())), "Owned source bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_CAMPFIRE_BRANCH_PASS trigger={trigger.FormKey} script={script.FormKey} cases=3 " +
            "originalSourceAndBindings=true deadTailNeverQueried=true sourceOrder=true sourceReadonly=true " +
            "controlledFunctionInputs=true commandCallbacksOnly=true physicalTriggerCampaignAndRetail=unverified");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
}
