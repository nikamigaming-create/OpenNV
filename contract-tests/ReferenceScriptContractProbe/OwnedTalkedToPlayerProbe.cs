using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedTalkedToPlayerProbe
{
    internal static void Run(string game, string ttwRoot, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", ttwRoot, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var package = records.GetEffective(new("Fallout3.esm", 0x095500));
        var caller = records.GetEffective(new("Fallout3.esm", 0x0300f2));
        var subject = records.GetEffective(new("Fallout3.esm", 0x0309ad));
        if (package.Signature != "PACK" || caller.Signature != "ACHR" || subject.Signature != "ACHR")
            throw new InvalidDataException("Selected talked-to-player audit requires its winning package and actual actor references.");
        var hashes = new[] { package, caller, subject }.Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var conditions = FalloutCondition.Read(package).Where(condition => condition.Function == 50).ToArray();
        if (conditions.Length != 1) throw new InvalidDataException("Selected source package has no unique talked-to-player condition.");
        var condition = conditions[0];
        if (condition.Flags != 0 || condition.Comparison != 1 || condition.RunOn != 2 ||
            condition.Argument1 != 0 || condition.Argument2 != 0 ||
            package.Plugin.AdjustOptionalFormId(condition.Reference) != subject.FormKey ||
            condition.Reference == subject.FormKey.ObjectId)
            throw new InvalidDataException("Selected source talked-to-player scope or master-adjusted reference changed.");
        var callerState = world.Get(caller.FormKey);
        var subjectState = world.Get(subject.FormKey);
        var quests = new FalloutQuestState(records);
        var questState = JsonSerializer.Serialize(quests.Capture());
        var queries = 0;

        // Isolated reference-state fixtures prove that the authored explicit
        // subject owns its history, independently of the package's caller.
        // No package/result script, campaign stage or user's save is executed.
        callerState.TalkedToPlayer = true;
        subjectState.TalkedToPlayer = false;
        Check(world, false);
        CheckCold(false);
        callerState.TalkedToPlayer = false;
        subjectState.TalkedToPlayer = true;
        Check(world, true);
        CheckCold(true);
        subjectState.TalkedToPlayer = false;
        Check(world, false);
        if (questState != JsonSerializer.Serialize(quests.Capture()) ||
            hashes.Any(pair => !pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))))
            throw new InvalidDataException("Talked-to-player query fixtures changed campaign state or winning source bytes.");
        Console.WriteLine($"OPENNV_OWNED_TALKED_TO_PLAYER_PASS package={package.FormKey} caller={caller.FormKey} subject={subject.FormKey} " +
            $"queries={queries} explicit=true masterAdjusted=true liveChanges=true coldFalse=true coldTrue=true " +
            "sourceReadonly=true queryReadonly=true referenceState=isolated-history-fixture ordinaryInput=separate campaignProgress=unverified parity=unverified");

        void Check(FalloutReferenceWorld owner, bool expected)
        {
            var before = JsonSerializer.Serialize(owner.Capture());
            bool Query(FalloutFormKey reference)
            {
                if (reference != subject.FormKey)
                    throw new InvalidDataException("Source talked-to-player predicate queried the caller or another reference.");
                queries++;
                return owner.Get(reference).TalkedToPlayer;
            }
            var actual = FalloutAiPackages.HasTalkedToPlayer(condition, caller.FormKey, Query);
            var passes = FalloutCondition.AllPass([condition], value =>
                FalloutAiPackages.HasTalkedToPlayer(value, caller.FormKey, Query) ? 1 : 0, evaluateRunOn: true);
            if (actual != expected || passes != expected || before != JsonSerializer.Serialize(owner.Capture()))
                throw new InvalidDataException("Talked-to-player predicate returned stale/wrong history or mutated reference state.");
        }

        void CheckCold(bool expected)
        {
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))
                ?? throw new InvalidDataException("Talked-to-player reference fixture did not serialize.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(saved);
            if (cold.Get(caller.FormKey).TalkedToPlayer != callerState.TalkedToPlayer ||
                cold.Get(subject.FormKey).TalkedToPlayer != subjectState.TalkedToPlayer)
                throw new InvalidDataException("Cold reference history differs from its isolated retained fixture.");
            Check(cold, expected);
        }
    }
}
