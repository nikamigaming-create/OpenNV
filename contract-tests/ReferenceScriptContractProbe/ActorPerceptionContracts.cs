using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorPerceptionContracts
{
    private static readonly FalloutFormKey Player = Key(0x14), Npc = Key(0x901), Creature = Key(0x902);
    private static readonly string[] Images =
    ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
     "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];

    internal static void Run()
    {
        foreach (var image in Images) { ProcessAndCache(FalloutActorPerceptionDeclaration.ForExecutable(image)); Failures(FalloutActorPerceptionDeclaration.ForExecutable(image)); }
        ScalarFamilies(); Reaction(); SourceAndNativeLeases();
        Console.WriteLine("OPENNV_ACTOR_PERCEPTION_CONTRACT_PASS sourceLowAndPlayerHigh=true pendingDistinct=true signedCache=true negativeObservationRetained=true timerCrossing=true partialCommitCold=true staleEpochRefused=true caughtReentryRetained=true missingScheduleRefused=true scalarFamiliesDistinct=true ordinaryFalsePerkConsumer=true winningNpcCreature=true foreignMasterRefused=true coldNativeRepublished=true oldNativeRetirementScoped=true nativeGameplay=unexecuted retailParity=unmatched");
    }
    private static FalloutActorPerception New(FalloutActorPerceptionDeclaration source,
        Func<FalloutFormKey, FalloutPerceptionControllerNotification>? notify = null,
        FalloutActorPerceptionSnapshot? restore = null, Func<FalloutFormKey, FalloutCombatActorIdentity>? identity = null)
    {
        return new(source, "authored-selection", Player, 3, key => key == Player || key == Npc || key == Creature,
            identity ?? (key => Identity(source, key)),
            new(key => Observation(key, "authored-source-native", 1),
                (from, to, fromEpoch, toEpoch) => new(from, to, fromEpoch, toEpoch, Input(),
                    new(false, false, "authored-full-visibility-input"), "authored-pair-input"),
                notify ?? (_ => new(false, false, "authored-known-absent-controller")),
                _ => throw new NotSupportedException("No affecting-light list was authored."), () => Settings(), () => .125f), restore);
    }
    private static FalloutCombatActorIdentity Identity(FalloutActorPerceptionDeclaration source, FalloutFormKey actor) =>
        new(actor, Key(actor == Player ? 7u : actor == Creature ? 0x702u : 0x701u), actor == Player ? "ENGINE_PLAYER" : actor == Creature ? "ACRE" : "ACHR", 0,
            actor == Player ? source.ExecutableSha256 : Digest(actor.ToString()), actor == Creature ? "CREA" : "NPC_", 0, Digest("base:" + actor), actor == Player);
    private static FalloutPerceptionNativeObservation Observation(FalloutFormKey actor, string owner, long lease) =>
        new(actor, owner, lease, Key(0x800), actor == Player ? [0, 0, 0] : [128, 0, 0], 0, true, false, false, false, false, false, false);
    private static FalloutDetectionScoreInputs Input() => new(ReceiverPerception: 1, TargetSneak: 1,
        VisualLineOfSight: false, VisualCone: false, SourceDistance: 128, TargetLightAmount: 0, VisionPenalty: 0,
        TargetStealthSuppressed: false, EquipmentWeight: 0, TargetSneaking: false, TargetMoving: false,
        TargetRunning: false, ReceiverSleepPredicate: false, ReceiverRawInCombat: false, TargetActionSound: 0,
        ReceiverHeightClass: 1, ReceiverExterior: false, ReceiverCombatWithOther: false, AmbushMode: 0,
        ReceiverLevel: 1, TargetLevel: 1, ArmorPenalty: 0);
    private static FalloutDetectionScoreSettings Settings() => new(BaseValue: 100, StartBonus: 0, LevelBonus: 0, StartBonusLevelPenalty: 0,
        AmbushTargetModifier: 0, AmbushNonTargetModifier: 0, PerceptionMinimum: 0, PerceptionMaximum: 0,
        AlertModifier: 0, SleepBonus: 0, CombatModifier: 0, ExteriorDistanceMultiplier: 1, MaximumDistance: 4096,
        DistanceExponent: 1, SoundLosMultiplier: 0, BootWeightBase: 0, BootWeightMultiplier: 0, RunningMultiplier: 0,
        ActionMultiplier: 0, SoundsMultiplier: 0, LightMoveMultiplier: 0, LightRunMultiplier: 0, SneakLightModifier: 0,
        LightMultiplier: 0, LargeActorSizeMultiplier: 0, StealthBoyMultiplier: 0, SkillMultiplier: 0);

    private static void ProcessAndCache(FalloutActorPerceptionDeclaration source)
    {
        using var owner = New(source); owner.Construct(Npc); owner.Construct(Creature);
        Require(owner.Process(Player)?.Level == FalloutDetectionProcessLevel.High && owner.Process(Npc)?.Level == FalloutDetectionProcessLevel.Low &&
            owner.Process(Creature)?.Level == FalloutDetectionProcessLevel.Low, "Native presentation replaced the actual class constructors.");
        owner.ComputeSourcePair(Player, Npc, true);
        Require(owner.GetDetected(Player, Npc, false, false, false, _ => { }) == 0, "A pending score escaped mode-zero committed access.");
        owner.CommitSourceCache(Player);
        Require(owner.GetDetected(Player, Npc, false, false, false, _ => { }) == 1, "Positive committed source detection was lost.");
        var positive = owner.Capture().Actors.Single(actor => actor.Source.Reference == Player).Cache!.Detected.Single();
        Require(positive.LastObservation?.Position.SequenceEqual([128f, 0, 0]) == true, "Positive source pose/time was not retained.");
        var cache = new FalloutDetectionCache(Player, FalloutDetectionProcessLevel.High, 3, key => key == Npc || key == Player);
        cache.Stage(Npc, 3, int.MaxValue, false, false, false, FalloutDetectionDirection.Detected, [1, 2, 3], 0);
        Require(cache.BeginCommit(), "Source detected write omitted the controller notification boundary.");
        cache.CompleteCombatNotification(false, false);
        var query = new FalloutDetectionQuery(_ => true, actor => new(actor, new(true, FalloutDetectionProcessLevel.High), 0, false, false, true, false),
            () => new(false, false), _ => cache, _ => { });
        Require(query.ReadCommitted(new(Player, new(true, FalloutDetectionProcessLevel.High), 0, false, false, true, false),
            new(Npc, new(true, FalloutDetectionProcessLevel.High), 0, false, false, true, false), new(true, FalloutDetectionProcessLevel.High)).Score == -100,
            "Mode-zero treated INT_MAX as positive original detection.");
        cache.Stage(Npc, 0, -50, false, false, false, FalloutDetectionDirection.Detected, [99, 0, 0], .25f);
        cache.BeginCommit(); cache.CompleteCombatNotification(false, false);
        Require(cache.Read(Npc, FalloutDetectionDirection.Detected)!.LastObservation!.Position.SequenceEqual([1f, 2, 3]), "A negative score erased last positive observation.");
        owner.SetActionSound(Npc, 7); owner.AdvanceClocks(.2f);
        Require(owner.Capture().Actors.Single(actor => actor.Source.Reference == Npc).ActionSound.Level == 7, "Countdown crossing cleared action noise early.");
        owner.AdvanceClocks(0);
        Require(owner.Capture().Actors.Single(actor => actor.Source.Reference == Npc).ActionSound.Level == 0, "The next original update did not expire action noise.");
        Require(owner.ReadCombatDetection(Npc).Level is null && owner.SaveBlocker is not null, "An absent actual source scheduler certified a settled empty pass.");
        var snapshot = RoundTrip(owner.Capture());
        using var cold = New(source, restore: snapshot);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshot), "Cold perception replayed a source prefix.");
        Reject(() => New(source, restore: snapshot with { Actors = snapshot.Actors.Where(actor => actor.Source.Reference != Npc).ToArray() }).Dispose());
        Reject(() => New(source, restore: snapshot, identity: key => Identity(source, key) with { BaseSha256 = Digest("changed winner") }).Dispose());
        Reject(() => owner.RequestProcess(Npc, owner.ProcessEpoch(Npc) - 1, new(true, FalloutDetectionProcessLevel.High), "retired-epoch"));
    }
    private static void Failures(FalloutActorPerceptionDeclaration source)
    {
        using var failing = New(source, _ => new(null, null, "authored-unowned-controller")); failing.Construct(Npc);
        failing.ComputeSourcePair(Player, Npc, true); Reject(() => failing.CommitSourceCache(Player));
        var snapshot = RoundTrip(failing.Capture());
        Require(snapshot.Phase == FalloutPerceptionFramePhase.NotifyingController && snapshot.Actors.Single(actor => actor.Source.Reference == Player).Cache!.Detected.Count == 1,
            "Controller failure discarded the committed Detected prefix.");
        using var cold = New(source, restore: snapshot);
        cold.ContinueSourceCommit();
        Require(cold.Capture().Phase == FalloutPerceptionFramePhase.Idle && cold.SaveBlocker is not null, "Cold notification completion erased its original failure.");
        FalloutActorPerception? reentry = null;
        using var caught = New(source, _ =>
        {
            try { reentry!.SetActionSound(Npc, 1); } catch (InvalidOperationException) { }
            return new(false, false, "authored-caught-reentry");
        }); reentry = caught; caught.Construct(Npc); caught.ComputeSourcePair(Player, Npc, true); caught.CommitSourceCache(Player);
        Require(caught.Capture().Failures.Any(failure => failure.Owner == "actor-perception-reentry") && caught.SaveBlocker is not null,
            "A caught native callback reentry fabricated source readiness.");
        using var unknown = New(source); unknown.Construct(Npc); var epoch = unknown.ProcessEpoch(Npc);
        unknown.RequestProcess(Npc, epoch, null, "authored-unknown-election");
        Require(unknown.Process(Npc)?.Level == FalloutDetectionProcessLevel.Low && unknown.ProcessEpoch(Npc) == epoch && unknown.SaveBlocker is not null,
            "An unknown source transition destroyed the last owned process prefix.");
    }
    private static void ScalarFamilies()
    {
        var settings = Settings() with { BaseValue = 0, StartBonus = 10, LevelBonus = 2, StartBonusLevelPenalty = 1, SkillMultiplier = 1 };
        var input = Input() with { SourceDistance = 0, TargetSneaking = true, TargetSneak = 20, ReceiverLevel = 1, TargetLevel = 6, ArmorPenalty = 3 };
        Require(FalloutDetectionScalar.Calculate(FalloutDetectionScalarKind.NewVegas, input, settings).Score == -31 &&
            FalloutDetectionScalar.Calculate(FalloutDetectionScalarKind.Fallout3, input, settings).Score == -20,
            "FO3 imported FNV's absent level/start/armor scalar terms.");
    }
    private static void Reaction()
    {
        var calls = 0;
        var input = new FalloutPerceptionReactionInput(1, 1, 2, false, false, false, null, false, "authored-ordinary-reaction");
        var result = FalloutPerceptionReaction.GroupTarget(input, initial => { calls++; Require(!initial, "Ally reaction did not reach perk with false."); return new(true, "authored-entry15"); });
        Require(result.Allowed == true && calls == 1, "Ordinary false skipped entry15.");
        result = FalloutPerceptionReaction.GroupTarget(input with { Aggression = 3, Relation = null }, _ => throw new InvalidDataException("Frenzied arm invoked later unowned work."));
        Require(result.Allowed == true, "Source frenzied direct return was removed.");
        Require(FalloutPerceptionReaction.GroupTarget(input with { SpecialTeammateBranch = null }, _ => new(true, "wrong-fallback")).Allowed is null,
            "Unowned teammate election became an ordinary positive branch.");
    }
    private static void SourceAndNativeLeases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-perception-source-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Actors.esm"), Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Actor("NPC_", 7), Actor("NPC_", 0x701), Actor("CREA", 0x702), Record("CELL", 0x800, Field("DATA", [1, 0])),
                Group(Ref("ACHR", 0x901, 0x701), Ref("ACRE", 0x902, 0x702))));
            using var records = FalloutPluginStack.Load(directory, ["Actors.esm"]);
            var source = FalloutActorPerceptionDeclaration.ForExecutable(Images[0]);
            var group = FalloutCombatGroupDeclaration.ForExecutable(Images[0]);
            using var world = new FalloutReferenceWorld(records); world.ConfigureCombatGroups(group, "authored-world"); world.ConfigureActorPerception(source, "authored-world");
            using var inputs = world.BindPerceptionSourceInputs(_ => new(Key(0x800), [0, 0, 0], [0, 0, 0]));
            world.Get(Npc); world.Get(Creature);
            Require(world.ActorPerception.Process(Npc)?.Level == FalloutDetectionProcessLevel.Low && world.ActorPerception.Process(Creature)?.Level == FalloutDetectionProcessLevel.Low,
                "Actual winning actor reader did not retain both source constructors.");
            using var first = world.BindNativePerception(Npc, "authored-body-one", lease => Observation(Npc, "authored-body-one", lease));
            using var second = world.BindNativePerception(Npc, "authored-body-two", lease => Observation(Npc, "authored-body-two", lease));
            first.Dispose();
            Require(world.CaptureActorPerception().Actors.Single(actor => actor.Source.Reference == Npc).Source3D,
                "Old body retirement detached a new actual publication.");
            var saved = RoundTrip(world.CaptureActorPerception()); var references = world.Capture();
            using var cold = new FalloutReferenceWorld(records); cold.Restore(references); cold.ConfigureCombatGroups(group, "authored-world", world.CaptureCombatGroups());
            cold.ConfigureActorPerception(source, "authored-world", saved);
            using var coldInputs = cold.BindPerceptionSourceInputs(_ => new(Key(0x800), [0, 0, 0], [0, 0, 0]));
            Reject(() => cold.GetDetected(Npc, Creature, _ => { }));
            using var fresh = cold.BindNativePerception(Npc, "authored-cold-body", lease => Observation(Npc, "authored-cold-body", lease));
            Require(cold.GetDetected(Npc, Creature, _ => { }) == 0, "Known Low getter was replaced by a score producer.");
            Reject(() => world.BindNativePerception(Npc, "foreign-body", lease => Observation(Creature, "foreign-body", lease)).Dispose());
            using var foreign = new FalloutReferenceWorld(records); foreign.Restore(references);
            foreign.ConfigureCombatGroups(group, "authored-world", world.CaptureCombatGroups());
            Reject(() => foreign.ConfigureActorPerception(source, "authored-world", saved with
            {
                Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ? actor with
                {
                    Source = actor.Source with { Base = new("Other.esm", actor.Source.Base.ObjectId) }
                } : actor).ToArray()
            }));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static FalloutActorPerceptionSnapshot RoundTrip(FalloutActorPerceptionSnapshot state) => JsonSerializer.Deserialize<FalloutActorPerceptionSnapshot>(JsonSerializer.Serialize(state))!;
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static FalloutFormKey Key(uint id) => new("Actors.esm", id);
    private static byte[] Actor(string signature, uint id) => Record(signature, id, Field("ACBS", new byte[24]), Field("AIDT", new byte[20]));
    private static byte[] Ref(string signature, uint id, uint basis) => Record(signature, id, Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]));
    private static byte[] Group(params byte[][] values)
    {
        var data = Join(values); var bytes = new byte[data.Length + 24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Malformed, stale, omitted or foreign source perception was accepted.");
    }
}
