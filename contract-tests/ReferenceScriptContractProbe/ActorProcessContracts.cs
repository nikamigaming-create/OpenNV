using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorProcessContracts
{
    private static readonly string[] Images =
    ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
     "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];
    private static readonly FalloutFormKey Player = Key(0x14), Npc = Key(0x901), Creature = Key(0x902);

    internal static void Run()
    {
        CohortOrder(); ArithmeticAndElection();
        foreach (var image in Images)
        {
            var declaration = FalloutActorProcessDeclaration.ForExecutable(image);
            ConstructorAdmission(declaration); PlayerAndFactory(declaration); EmptySourceSchedule(declaration);
        }
        Console.WriteLine("OPENNV_ACTOR_PROCESS_CONTRACT_PASS segmentedSourceOrder=true swapLast=true nullGaps=true lookupToLowEnd=true tierOrdinalIndependent=true playerHighOutsideCohort=true constructorRegistrationInputs=required sameEpochAdmission=bothOwners exactStoredArithmetic=true strictNegativeBoundary=true cellPhaseDistinctFromResidency=true missingCommonCopyRetainsOldAndNew=true retiredEpochRefused=true coldNewProcessNoReplay=true foreignWinnerRefused=true emptyCohortCommit=actualPureOwner fo3FinalConsumer=retainedUnowned nativeGameplay=unexecuted sourceFactoryComplete=unverified");
    }
    private static void ConstructorAdmission(FalloutActorProcessDeclaration declaration)
    {
        using var perception = Perception(declaration); perception.Construct(Npc); perception.Construct(Creature);
        using var missing = Manager(declaration, perception, _ => throw Missing(), construction: key => new(key,
            new(null, "authored-missing-original-registration-argument"), new(null, "unqueried-registration-mode"), "actual-constructor-refusal"));
        missing.Construct(Npc);
        Require(missing.Read(Npc) is { Registered: false, Level: FalloutDetectionProcessLevel.Low, Boundary: not null } &&
            missing.Capture().Cohort.Ends[3] == 0 && missing.SaveBlocker is not null,
            "Allocating a source Low process fabricated its constructor registration argument.");
        Reject(() => missing.EnsureHigh(Npc, 1, "authored-high-entry-with-unowned-old-registration"));
        Require(missing.Capture().Factory is null, "Unowned old registration was discarded by a new factory prefix.");
        FalloutActorProcessManager.ValidateShape(missing.Capture());

        using var absent = Manager(declaration, perception, _ => throw Missing(), construction: key => new(key,
            new(false, "authored-original-registration-disabled"), new(null, "unqueried-mode-on-disabled-arm"), "actual-disabled-registration-constructor"));
        absent.Construct(Npc);
        Require(!absent.Read(Npc).Registered && absent.Read(Npc).Boundary is null && absent.Capture().Cohort.Ends[3] == 0,
            "The source false-registration arm was changed into an unknown or manager entry.");
        FalloutActorProcessManager.ValidateShape(absent.Capture());

        using var alternate = Manager(declaration, perception, _ => throw Missing(), construction: key => new(key,
            new(true, "authored-original-registration-enabled"), new(true, "authored-original-alternate-mode"), "actual-alternate-registration-constructor"));
        alternate.Construct(Creature);
        Require(!alternate.Read(Creature).Registered && alternate.SaveBlocker is not null && alternate.Capture().Cohort.Ends[3] == 0,
            "The source alternate-registration arm silently entered the normal cohort.");
        Reject(() => alternate.Retire(Creature, "authored-retirement-with-unowned-alternate-registration"));
        FalloutActorProcessManager.ValidateShape(alternate.Capture());
    }

    private static void EmptySourceSchedule(FalloutActorProcessDeclaration declaration)
    {
        using var perception = Perception(declaration);
        using var manager = Manager(declaration, perception, _ => throw Missing());
        if (declaration.Arithmetic == FalloutDetectionScalarKind.NewVegas) manager.AdvanceSourceDetection(0f);
        else Reject(() => manager.AdvanceSourceDetection(0f));
        var saved = RoundTrip(manager.Capture());
        Require(saved.Schedule is { CompletedCommits: 1, NextSlot: 0, HighEndAtEntry: 0 } receipt &&
            receipt.Complete == (declaration.Arithmetic == FalloutDetectionScalarKind.NewVegas) &&
            saved.PlayerScheduleFlag == false && perception.Capture().CompletedCacheCommits == 1,
            "Empty original cohort lost the distinct Player commit or invented the FO3 final consumer.");
        FalloutActorProcessManager.ValidateShape(saved);
        Reject(() => FalloutActorProcessManager.ValidateShape(saved with
        { Schedule = saved.Schedule! with { CompletedCommits = 2 } }));
        Reject(() => FalloutActorProcessManager.ValidateShape(saved with
        { Schedule = saved.Schedule! with { NextSlot = 1 } }));
    }
    private static void CohortOrder()
    {
        var a = Key(1); var b = Key(2); var c = Key(3); var d = Key(4); var e = Key(5); var g = Key(6);
        var keys = new[] { a, b, c, d, e, g };
        var cohort = new FalloutActorProcessCohort(key => keys.Contains(key));
        cohort.Add(a, FalloutDetectionProcessLevel.Low); cohort.Add(b, FalloutDetectionProcessLevel.Low); cohort.Add(c, FalloutDetectionProcessLevel.Low);
        Require(cohort.Segment(3).SequenceEqual(new FalloutFormKey?[] { a, b, c }), "Original Low insertion order changed.");
        cohort.Add(d, FalloutDetectionProcessLevel.High);
        Require(cohort.Segment(0).SequenceEqual(new FalloutFormKey?[] { d }) &&
            cohort.Segment(3).SequenceEqual(new FalloutFormKey?[] { b, c, a }), "Boundary displacement was replaced by stable per-tier lists.");
        cohort.Add(e, FalloutDetectionProcessLevel.MiddleHigh);
        Require(cohort.Segment(1).SequenceEqual(new FalloutFormKey?[] { e }) &&
            cohort.Segment(3).SequenceEqual(new FalloutFormKey?[] { c, a, b }), "Recursive displacement omitted an intermediate empty segment.");
        Require(cohort.Remove(a, FalloutDetectionProcessLevel.Low) && cohort.Segment(3).SequenceEqual(new FalloutFormKey?[] { c, b }),
            "Original removal did not swap the last source entry.");
        cohort.Add(g, FalloutDetectionProcessLevel.High);
        Require(cohort.Segment(0).SequenceEqual(new FalloutFormKey?[] { d, g }) && cohort.Segment(1).SequenceEqual(new FalloutFormKey?[] { e }) &&
            cohort.Segment(3).SequenceEqual(new FalloutFormKey?[] { b, c }), "A later High insertion reordered or dropped displaced unrelated actors.");
        Require(cohort.Remove(d, FalloutDetectionProcessLevel.High), "High removal failed.");
        var hole = cohort.Capture();
        Require(hole.Slots[1] is null && cohort.Segment(0).SequenceEqual(new FalloutFormKey?[] { g }), "Source null gap or High swap-last was discarded.");
        var copy = new FalloutActorProcessCohort(key => keys.Contains(key), RoundTrip(hole));
        Require(JsonSerializer.Serialize(copy.Capture()) == JsonSerializer.Serialize(hole), "Cold cohort normalized away original gaps/cursors.");
        Require(copy.Remove(c, FalloutDetectionProcessLevel.High), "Original lookup was narrowed to a requested tier instead of the low end.");
        Require(FalloutActorProcessCohort.Tier(FalloutDetectionProcessLevel.High) == 0 &&
            FalloutActorProcessCohort.Tier(FalloutDetectionProcessLevel.Low) == 3, "C# enum ordinal became the source process number.");
        Reject(() => new FalloutActorProcessCohort(key => keys.Contains(key), hole with
        { Slots = hole.Slots.Select((key, index) => index == 1 ? b : key).ToArray() }));
        Reject(() => new FalloutActorProcessCohort(key => keys.Contains(key), hole with
        { Slots = hole.Slots.Select(key => key == b ? new FalloutFormKey("Foreign.esm", b.ObjectId) : key).ToArray() }));
    }

    private static void ArithmeticAndElection()
    {
        var fnv = FalloutActorProcessDeclaration.ForExecutable(Images[0]); var fo3 = FalloutActorProcessDeclaration.ForExecutable(Images[1]);
        // 16777217*1.5/2^32 is above a Float32 halfway point. FO3 first
        // rounds the unsigned word, while the x87 producer stores only once.
        Require(BitConverter.SingleToInt32Bits(fnv.CadenceDraw(16777217, 1.5f)) == 0x3bc00001 &&
            BitConverter.SingleToInt32Bits(fo3.CadenceDraw(16777217, 1.5f)) == 0x3bc00000,
            "The two source cadence arithmetic sequences were collapsed.");
        var delta = BitConverter.Int32BitsToSingle(0x33c00000);
        Require(BitConverter.SingleToInt32Bits(fnv.RandomWindow(16777217, delta)) == 0x3fc00001 &&
            BitConverter.SingleToInt32Bits(fo3.RandomWindow(16777217, delta)) == 0x3fc00000,
            "Source count conversion/store order changed.");
        Require(fnv.RandomWindow(uint.MaxValue, float.MaxValue) == 5f && fo3.RandomWindow(uint.MaxValue, float.MaxValue) == 5f,
            "Original final cap was replaced by an artificial actor/frame budget or overflow refusal.");
        Require(fnv.PlayerScoreAdmitted(-35, -35) && !fo3.PlayerScoreAdmitted(-35, -35), "FO3 inherited FNV's inclusive player score boundary.");
        Require(!FalloutActorProcessDeclaration.TimerNeedsProducer(0f) &&
            !FalloutActorProcessDeclaration.TimerNeedsProducer(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))) &&
            FalloutActorProcessDeclaration.TimerNeedsProducer(-float.Epsilon) &&
            BitConverter.SingleToInt32Bits(FalloutActorProcessDeclaration.DecrementTimer(0f, .125f)) == unchecked((int)0xbe000000) &&
            BitConverter.SingleToInt32Bits(FalloutActorProcessDeclaration.DecrementTimer(.5f, .125f)) == 0x3ec00000,
            "The actual countdown owner changed the signed-zero or fractional source boundary.");
        Reject(() => FalloutActorProcessDeclaration.TimerNeedsProducer(float.NaN));
        Reject(() => FalloutActorProcessDeclaration.DecrementTimer(0f, float.NaN));
        Reject(() => FalloutActorProcessDeclaration.DecrementTimer(-float.MaxValue, float.MaxValue));
        foreach (var phase in new byte[] { 2, 3, 4 })
            Require(!FalloutActorProcessManager.CellEligible(phase, true) && FalloutActorProcessManager.CellEligible(phase, false),
                "A secondary loaded cell phase became High eligibility.");
        foreach (var phase in new byte[] { 5, 6 })
            Require(FalloutActorProcessManager.CellEligible(phase, true) && FalloutActorProcessManager.CellEligible(phase, false), "Actual high cell phase was dropped.");
        Require(!FalloutActorProcessManager.CellEligible(0, false) && !FalloutActorProcessManager.CellEligible(255, true),
            "Unknown or uninitialized CELL phase became resident High.");
        Reject(() => (fnv with { PlayerScoreIncludesEquality = false }).Validate());
        Reject(() => FalloutActorProcessDeclaration.ForExecutable(Digest("unreviewed original")));
    }
    private static void PlayerAndFactory(FalloutActorProcessDeclaration declaration)
    {
        using var perception = Perception(declaration); perception.Construct(Npc); perception.Construct(Creature);
        var factoryCalls = 0;
        var owner = Manager(declaration, perception, copy =>
        { factoryCalls++; Require(copy.Phase == FalloutActorProcessFactoryPhase.ConstructedNew && copy.NewCache is not null && copy.NewLight is not null,
            "Original common copy did not retain the allocated new source owner."); throw new NotSupportedException("authored-absent-common-transfer-owner"); });
        owner.Construct(Npc); owner.Construct(Creature);
        Require(owner.HighCohort.Count == 0 && owner.Read(Player).Level == FalloutDetectionProcessLevel.High && !owner.Read(Player).Registered &&
            perception.Capture().Actors.Single(actor => actor.Source.Reference == Player).Cache is not null,
            "Player process/cache construction fabricated a manager cohort entry.");
        var epoch = owner.Read(Player).Epoch; owner.EnsureHigh(Player, epoch, "actual-already-high-arm");
        Require(owner.Read(Player).Epoch == epoch && factoryCalls == 0, "Already-High source entry replaced its epoch or replayed copy.");
        var election = new FalloutActorProcessElection(Npc, 1, new(0, "authored-original-player-counter"),
            new(false, "authored-original-main-bit"), new(false, "authored-original-processing-tree"),
            new(3, "authored-original-cell-phase"), new(false, "authored-original-cell-extra9"), "authored-source-tier-getter");
        Require(owner.Reevaluate(election) == FalloutDetectionProcessLevel.MiddleLow,
            "Unloaded secondary cell was promoted from distance/native residency.");
        owner.RetainBoundary(Npc, "authored-source-current-tier-awaiting-admission");
        perception.RequestProcess(Npc, 1, null, "authored-source-current-tier-awaiting-admission");
        owner.AdmitCurrentEpoch(election with { SourceCellPhase = new(0, "authored-current-unloaded-cell-phase") });
        Require(owner.Read(Npc).Boundary is null && perception.CaptureSourceProcessActor(Npc).ProcessBoundary is null && owner.Read(Npc).Epoch == 1,
            "Same-tier admission left one owner blocked or created a new process epoch.");
        Reject(() => owner.Reevaluate(election with { SourceCellPhase = new(null, "authored-missing-cell-loader") }));
        Reject(() => owner.EnsureHigh(Npc, 0, "retired-factory-invocation"));
        Reject(() => owner.EnsureHigh(Npc, 1, "authored-original-high-factory-entry"));
        var saved = RoundTrip(owner.Capture());
        Require(saved.Factory is { Phase: FalloutActorProcessFactoryPhase.ConstructedNew } &&
            saved.Actors.Single(actor => actor.Source.Reference == Npc) is { Epoch: 1, Registered: false, Level: FalloutDetectionProcessLevel.Low } &&
            perception.Process(Npc)?.Level == FalloutDetectionProcessLevel.Low && owner.SaveBlocker is not null,
            "A failed common copy retired/published the wrong owner or lost its actual prefix.");
        FalloutActorProcessManager.ValidateShape(saved);
        Reject(() => FalloutActorProcessManager.ValidateShape(saved with
        { Factory = saved.Factory! with { NewCache = saved.Factory!.NewCache! with { Owner = Creature } } }));
        Reject(() => FalloutActorProcessManager.ValidateShape(saved with
        { Factory = saved.Factory! with { NewLight = saved.Factory!.NewLight! with { Amount = 1f } } }));
        Reject(() => FalloutActorProcessManager.ValidateShape(saved with
        { Actors = saved.Actors.Select(actor => actor.Source.Reference == Creature ? actor with { Registered = false } : actor).ToArray() }));
        using var coldPerception = Perception(declaration, perception.Capture());
        var cold = Manager(declaration, coldPerception, _ => throw new InvalidDataException("Cold replayed common copy."), saved);
        var restored = cold.Capture();
        Require(restored.Seconds == saved.Seconds && restored.Factory == saved.Factory && restored.CapturedProcess != saved.CapturedProcess &&
            restored.ColdHandoff is { } handoff && handoff.PreviousProcess == saved.CapturedProcess && handoff.CurrentProcess == restored.CapturedProcess &&
            factoryCalls == 1, "Cold process publication replayed source calls, time or process identity.");
        Reject(() => cold.ContinueSourceFactory(Npc, 1, "authored-original-high-factory-entry"));
        Reject(() => Manager(declaration, coldPerception, _ => { }, saved with
        { Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ? actor with { Source = actor.Source with { BaseSha256 = Digest("foreign winner") } } : actor).ToArray() }));
        // These are actual retained failed owners, so disposal must refuse;
        // there is no native handle or fabricated native cleanup callback.
        Reject(owner.Dispose); Reject(cold.Dispose);
    }
    private static FalloutActorProcessManager Manager(FalloutActorProcessDeclaration source, FalloutActorPerception perception,
        Action<FalloutActorProcessFactorySnapshot> copy, FalloutActorProcessesSnapshot? restore = null,
        Func<FalloutFormKey, FalloutActorProcessConstruction>? construction = null) =>
        new(source, "authored-process-selection", Player, perception, key => Identity(source, key), Actor,
            new((_, _) => throw Missing(), (_, _) => throw Missing(), (_, _) => throw Missing(), () => throw Missing(), () => throw Missing(),
                _ => throw Missing(), _ => throw Missing(), _ => throw Missing(), (_, _, _) => throw Missing(), _ => throw Missing(),
                () => throw Missing(), _ => throw Missing(), copy, _ => throw Missing(), _ => throw Missing(),
                construction ?? (key => new(key, new(true, "authored-original-caller-registration-argument"),
                    new(false, "authored-original-normal-registration-mode"), "authored-original-actor-constructor"))), restore);
    private static FalloutActorPerception Perception(FalloutActorProcessDeclaration source, FalloutActorPerceptionSnapshot? restore = null) =>
        new(FalloutActorPerceptionDeclaration.ForExecutable(source.ExecutableSha256), "authored-process-selection", Player, 3, Actor,
            key => Identity(source, key), new(_ => throw Missing(), (_, _, _, _) => throw Missing(), _ => throw Missing(), _ => throw Missing(),
                () => throw Missing(), () => throw Missing()), restore);
    private static FalloutCombatActorIdentity Identity(FalloutActorProcessDeclaration source, FalloutFormKey key) =>
        new(key, Key(key == Player ? 7u : key == Creature ? 0x702u : 0x701u), key == Player ? "ENGINE_PLAYER" : key == Creature ? "ACRE" : "ACHR", 0,
            key == Player ? source.ExecutableSha256 : Digest("authored-reference:" + key), key == Creature ? "CREA" : "NPC_", 0,
            Digest("authored-base:" + key), key == Player);
    private static bool Actor(FalloutFormKey key) => key == Player || key == Npc || key == Creature;
    private static FalloutFormKey Key(uint id) => new("ActorProcessFixture.esm", id);
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static NotSupportedException Missing() => new("Authored fixture provides no native or source sensory completion.");
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Foreign, incomplete or retired source process input was accepted.");
    }
}
