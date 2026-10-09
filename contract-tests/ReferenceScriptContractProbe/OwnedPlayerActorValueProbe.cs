using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPlayerActorValueProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var source = FalloutPlayerActorValueSource.Read(records);
        var sourceHash = SHA256.HashData(records.GetEffective(source.Player).ReadData());
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutPlayerInventory();
        FalloutPlayerSkills Bind(FalloutPlayerActorValues owner)
        {
            var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(source.Player), "RNAM");
            var skills = new FalloutPlayerSkills(records, () => owner.BaseSpecial, _ => false, () => [], globals, inventory,
                source.Player, () => race, () => false, actorValues: owner);
            owner.BindConstantModifiers(skills.Modifiers); return skills;
        }
        var owner = new FalloutPlayerActorValues(records); var skills = Bind(owner);
        var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
        var initial = owner.BaseSpecial;
        var initialPermanent = Enumerable.Range(5, 7).Select(owner.ReadPermanent).ToArray();
        owner.AddModifier(5, FalloutActorValuePool.Permanent, .5f);
        owner.AddModifier(5, FalloutActorValuePool.Temporary, 1.25f);
        owner.AddModifier(5, FalloutActorValuePool.Damage, -1);
        var permanent = owner.ReadPermanent(5); var current = owner.ReadCurrent(5);
        Require(permanent != current, "Owned actor values conflated permanent and current.");
        var before = owner.Capture();
        var bookReference = FalloutDialogueTopic.Find(records, "REFR", "CG01SpecialBookREF").FormKey;
        var bookSource = records.GetEffective(bookReference);
        var cell = FalloutCellSceneReader.ParentCell(bookSource) ?? throw new InvalidDataException("Owned book has no source cell.");
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
        var quests = new FalloutQuestState(records); quests.EnterStage(quest, 30);
        FalloutSpecialAllocationSession? session = null;
        var order = new List<string>(); var attempts = 0;
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            Require(effect.Kind == FalloutReferenceEffectKind.SetStage && effect.Target == quest && effect.Stage == 50,
                "Owned book guessed another source stage effect.");
            order.Add("SetStage50"); quests.EnterStage(quest, effect.Stage);
        }, Command: (_, _, command, arguments) =>
        {
            Require(command.Equals("ssbmp", StringComparison.OrdinalIgnoreCase) && arguments.SequenceEqual(new[] { "40" }),
                "Owned book command budget differs from winning source.");
            attempts++; order.Add("ssbmp40");
            session = new(int.Parse(arguments[0], CultureInfo.InvariantCulture), owner.AllocationBinding);
        }));
        var activation = executor.Activate(bookReference, records.RuntimeFormKey(0x14));
        Require(activation.Error is null && order.SequenceEqual(new[] { "SetStage50", "ssbmp40" }) && session is not null,
            "Owned activation lost its stage-before-menu prefix: " + activation.Error);
        var active = session!;
        Require(active.Change(5, 1) && owner.ReadBase(5) == MathF.Floor(permanent) + 1 &&
            owner.Capture().Values[5] == before.Values[5] with { Base = MathF.Floor(permanent) + 1 } &&
            owner.Capture().Values.Where(pair => pair.Key != 5).All(pair => pair.Value == before.Values[pair.Key]),
            "Owned book did not immediately change one BASE integer while retaining its modifiers.");
        // Retiring a menu retains its edits; this fixture does not synthesize
        // a source cancellation result, stage or ordinary player traversal.
        session = null;
        var coldSnapshot = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, coldSnapshot); var coldSkills = Bind(cold);
        var coldVitals = FalloutPlayerVitals.FromActorValues(records, cold,
            JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
        Require(Enumerable.Range(5, 7).All(value => owner.ReadBase(value) == cold.ReadBase(value) &&
            owner.ReadPermanent(value) == cold.ReadPermanent(value) && owner.ReadCurrent(value) == cold.ReadCurrent(value)) &&
            skills.Value("Guns") == coldSkills.Value("Guns") && vitals.State == coldVitals.State,
            "Owned cold pool state diverged from the live skill/vitals consumers.");
        var coldBook = new FalloutSpecialAllocationSession(40, cold.AllocationBinding);
        for (var value = 6; value <= 11 && !coldBook.CanFinish; value++) Require(coldBook.Change(value, 1), "Owned source budget edit was refused.");
        Require(coldBook.CanFinish && quests.Stage(quest) == 50 && quests.StageDone(quest, 50),
            "Owned session completion guessed another quest stage or failed its explicit budget.");
        var coldBefore = JsonSerializer.Serialize(cold.Capture()); var rejected = false;
        try { cold.Restore(coldSnapshot with { StatsSha256 = new string('0', 64) }); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected && JsonSerializer.Serialize(cold.Capture()) == coldBefore, "Failed owned source restore partially changed player values.");
        var guarded = executor.Activate(bookReference, records.RuntimeFormKey(0x14));
        Require(guarded.Error is null && attempts == 1, "Owned stage guard replayed a retired book command.");
        var migrated = new FalloutPlayerActorValues(records);
        foreach (var value in Enumerable.Range(5, 7)) migrated.WriteBaseInteger(value, initial.Values[value - 5]);
        Bind(migrated);
        Require(migrated.Capture().Values.Values.All(value => value.Permanent == 0 && value.Temporary == 0 && value.Damage == 0) &&
            Enumerable.Range(5, 7).Select(migrated.ReadPermanent).SequenceEqual(initialPermanent), "Owned source modifiers were baked into assigned BASE values.");
        Require(sourceHash.AsSpan().SequenceEqual(SHA256.HashData(records.GetEffective(source.Player).ReadData())), "Owned player input changed.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-player-actor-values-audit/v1", player = source.Player, playerWinner = source.PlayerWinner,
            statsOwner = source.StatsOwner, statsWinner = source.StatsWinner, reference = FalloutPlayerActorValues.PlayerReference,
            baseValues = cold.BaseSpecial.Values, permanentBefore = permanent, currentBefore = current,
            sourceBook = bookReference, sourceOrder = order, budget = coldBook.Budget, remaining = coldBook.Remaining,
            immediateBase = true, modifierPoolsRetained = true, liveSkillsAndVitals = true, cold = true, atomicSourceFailure = rejected,
            legacy = true, sourceReadOnly = true, recording = false,
            boundary = "isolated-owned-player-and-book-session-component; production-menu-input-save-retirement-campaign-and-retail-parity-unverified"
        }));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
