using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerWeaponContinuationContracts()
    {
        using var warm = new WeaponContinuationFixture();
        var uninitialized = warm.Common.Capture();
        Reject(() => warm.Common.ReadPlayerWeaponAction(warm.Player, 1));
        Require(JsonSerializer.Serialize(uninitialized) == JsonSerializer.Serialize(warm.Common.Capture()),
            "A generic process constructor invented the later Player weapon-action store.");
        var fields = uninitialized.Current.Single().SceneFields?.WeaponContinuation ??
            throw new InvalidDataException("Authored High constructor omitted its independent cached-value context.");
        Require(fields is { WeaponAction: null, ActionStores: 0, Activity: 0, CacheFlags: 0, CacheStores: 0,
            FirstContextWord: -1, SecondContextWord: -1, FirstContextPointer: null, SecondContextPointer: null, ContextByte: 0 } &&
            fields.StatusContext != Guid.Empty && fields.CachedScalarBits.Count == FalloutPlayerWeaponContinuationSource.CachedScalarCount &&
            fields.CachedScalarBits.All(bits => bits == FalloutPlayerWeaponContinuationSource.UnqueriedScalarBits),
            "The complete source context constructor lost independent scalar, word, pointer or byte stores.");
        warm.Initialize();
        Reject(warm.Initialize);
        var initialized = warm.Common.Capture();
        Require(initialized.Current.Single().SceneFields?.WeaponContinuation is
            { WeaponAction: 0, ActionStores: 1, Initialization.PlayerConstructor: var constructor } && constructor == warm.Constructor,
            "Weapon action borrowed WeaponDrawn or skipped the actual independent Player constructor writer.");
        var first = warm.Execute();
        FalloutPlayerWeaponContinuation.Validate(first, warm.Source, warm.Stack);
        Require(first is { Returned: true, Action.Value: 0, WeaponEntry: null, Activity.Value: 0, EffectsRequired: false,
            Status: { PoolQueried: true, BeforeFlags: 0, AfterFlags: FalloutPlayerWeaponContinuationSource.StatusCacheMask,
                ScalarBits: 0, Positive: false } } && warm.PoolReads == 1 && warm.Calls.SequenceEqual(["action", "activity", "status"]),
            "Returned constructor action queried the skipped global/entry or inferred status without the current pool getter.");
        var queried = warm.Common.Capture();
        var queriedFields = queried.Current.Single().SceneFields?.WeaponContinuation ??
            throw new InvalidDataException("Returned source status omitted its committed cache fields.");
        Require(queriedFields is { CacheStores: 2, CacheFlags: FalloutPlayerWeaponContinuationSource.StatusCacheMask } &&
            queriedFields.CachedScalarBits[FalloutPlayerWeaponContinuationSource.ParalysisCacheSlot] == 0 &&
            first.Status?.Changed == queried.Current.Single().Changed,
            "Status did not commit the current Float32 cell before the separate mask store.");
        warm.Ordinal++;
        var repeat = warm.Execute();
        FalloutPlayerWeaponContinuation.Validate(repeat, warm.Source, warm.Stack);
        Require(repeat is { Returned: true, Status: { PoolQueried: false, ScalarBits: 0 } } && warm.PoolReads == 1 &&
            warm.Common.Capture().Sequence == queried.Sequence,
            "Repeated Player status replayed the current pool query or its cache writes.");
        var saved = JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(queried)) ??
            throw new InvalidDataException("Authored current status could not serialize its complete Common state.");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new WeaponContinuationFixture(saved);
            Reject(cold.Initialize);
            var coldReturned = cold.Execute();
            FalloutPlayerWeaponContinuation.Validate(coldReturned, cold.Source, cold.Stack);
            cold.Common.RequirePlayerWeaponContinuationSource(cold.Player, 1, cold.Source);
            Require(coldReturned is { Returned: true, Status: { PoolQueried: false } } && cold.PoolReads == 0 &&
                coldReturned.Action?.Process.Process != first.Action?.Process.Process &&
                coldReturned.Action?.Process.GameplayOwnership == first.Action?.Process.GameplayOwnership &&
                coldReturned.Status?.Context == first.Status?.Context &&
                JsonSerializer.Serialize(cold.Common.Capture().Current) == JsonSerializer.Serialize(queried.Current),
                "Cold current getters replayed constructor/pool stores or reused a prior actual Common factory.");
            saved = cold.Common.Capture();
        }
        var currentStatus = first.Status ?? throw new InvalidDataException("Authored returned call omitted current status.");
        var malformedProcess = JsonSerializer.Deserialize<FalloutProcessWeaponActionRead>(
            "{\"Process\":null,\"Value\":0}") ?? throw new InvalidDataException("Malformed authored getter did not deserialize.");
        Reject(() => FalloutPlayerWeaponContinuation.Validate(first with { Action = malformedProcess }, warm.Source, warm.Stack));
        var malformedActorJson = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(first)) ??
            throw new InvalidDataException("Malformed authored Actor fixture could not parse its original caller.");
        var actorProcess = malformedActorJson["Action"]?["Process"] ??
            throw new InvalidDataException("Malformed authored Actor fixture lost its original process.");
        actorProcess["Actor"] = null;
        var malformedActor = JsonSerializer.Deserialize<FalloutPlayerWeaponContinuationCall>(malformedActorJson.ToJsonString()) ??
            throw new InvalidDataException("Malformed authored Actor caller did not deserialize.");
        Reject(() => FalloutPlayerWeaponContinuation.Validate(malformedActor, warm.Source, warm.Stack));
        Reject(() => FalloutPlayerWeaponContinuation.Validate(first with { Status = currentStatus with { Context = Guid.Empty } }, warm.Source, warm.Stack));
        Reject(() => FalloutPlayerWeaponContinuation.Validate(first with { Status = currentStatus with { Positive = true } }, warm.Source, warm.Stack));
        Reject(() => FalloutPlayerWeaponContinuation.Validate(first with { Status = currentStatus with { ScalarBits = 0x3f800000 } }, warm.Source, warm.Stack));
        Reject(() => FalloutPlayerWeaponContinuation.Validate(first with { Status = currentStatus with
            { Process = currentStatus.Process with { Process = Guid.NewGuid() } } }, warm.Source, warm.Stack));
        var foreignSource = FalloutPlayerWeaponContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(
            FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('5', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)))));
        Reject(() => warm.Common.RequirePlayerWeaponContinuationSource(warm.Player, 1, foreignSource));
        Reject(() => FalloutActorProcessCommonState.Validate(queried with
        { Current = [queried.Current.Single() with { SceneFields = queried.Current.Single().SceneFields! with
            { WeaponContinuation = queriedFields with { CacheFlags = 0 } } }] }));
        Reject(() => FalloutActorProcessCommonState.Validate(queried with
        { Current = [queried.Current.Single() with { SceneFields = queried.Current.Single().SceneFields! with
            { WeaponContinuation = queriedFields with { CachedScalarBits = queriedFields.CachedScalarBits.Take(10).ToArray() } } }] }));
        Reject(() => FalloutActorProcessCommonState.Validate(queried with
        { Current = [queried.Current.Single() with { SceneFields = queried.Current.Single().SceneFields! with
            { WeaponContinuation = queriedFields with { SecondContextWord = 0 } } }] }));
        foreach (var lane in Enum.GetValues<FalloutProcessWeaponObservedLane>())
        {
            using var observed = new WeaponContinuationFixture(saved);
            var observation = Guid.NewGuid();
            observed.Common.ObservePlayerWeaponContinuationWriter(observed.Player, 1, observation, "authored-actual-source-writer", lane);
            var retained = observed.Common.Capture();
            Reject(() => observed.Common.ObservePlayerWeaponContinuationWriter(observed.Player, 1, observation, "authored-actual-source-writer", lane));
            Reject(() => observed.Execute());
            var failure = observed.Last ?? throw new InvalidDataException("Entered observed writer did not retain its actual failure.");
            FalloutPlayerWeaponContinuation.Validate(failure, observed.Source, observed.Stack);
            var expected = lane switch { FalloutProcessWeaponObservedLane.WeaponAction => FalloutPlayerWeaponContinuationStep.WeaponAction,
                FalloutProcessWeaponObservedLane.Activity => FalloutPlayerWeaponContinuationStep.Activity, _ => FalloutPlayerWeaponContinuationStep.ActorStatus };
            Require(failure.FailedAt == expected && !failure.Returned && observed.Common.SaveBlocker is not null &&
                JsonSerializer.Serialize(retained) == JsonSerializer.Serialize(observed.Common.Capture()),
                "Observed writer mutated a cache, reused constructor availability or lost its failed current prefix.");
            using var observedCold = new WeaponContinuationFixture(JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(retained))!);
            Reject(() => observedCold.Execute());
            Require(observedCold.Last?.FailedAt == expected && observedCold.Common.SaveBlocker is not null,
                "Cold restoration repaired an unbound writer instead of preserving its refusal.");
        }
        using (var positive = new WeaponContinuationFixture())
        {
            positive.Initialize(); positive.Pool = new(1, .5f, .25f, -.75f);
            Reject(() => positive.Execute());
            FalloutPlayerWeaponContinuation.Validate(positive.Last ?? throw new InvalidDataException("Positive status lost its failed effect call."), positive.Source, positive.Stack);
            Require(positive.Last is { FailedAt: FalloutPlayerWeaponContinuationStep.Effects,
                Status: { PoolQueried: true, Positive: true, ScalarBits: 0x3f800000 }, EffectsRequired: true } &&
                positive.Common.Capture().Current.Single().SceneFields?.WeaponContinuation is { CacheStores: 2 },
                "Positive current status bypassed effects or rolled back preceding source cache stores.");
        }
        foreach (var scalar in new[] { -0f, float.NaN, -1f })
        {
            using var ordered = new WeaponContinuationFixture(); ordered.Initialize(); ordered.ReadPool = () => scalar;
            Require(ordered.Execute() is { Returned: true, Status.Positive: false } &&
                ordered.Last?.Status?.ScalarBits == BitConverter.SingleToUInt32Bits(scalar),
                "Actor status changed raw Float32 storage or admitted unordered/nonpositive values.");
        }
        using (var reentry = new WeaponContinuationFixture())
        {
            reentry.Initialize(); reentry.ReadPool = () =>
            {
                Reject(() => reentry.Common.ReadPlayerFollowingStatus(reentry.Player, 1, () => 1));
                return 0;
            };
            Reject(() => reentry.Execute());
            Require(reentry.Last?.FailedAt == FalloutPlayerWeaponContinuationStep.ActorStatus &&
                reentry.Common.Capture().Current.Single().SceneFields?.WeaponContinuation is { CacheStores: 0, CacheFlags: 0 },
                "Caught recursive pool getter published a successful cache result.");
        }
        using (var drift = new WeaponContinuationFixture())
        {
            drift.Initialize(); drift.ReadPool = () => { drift.SourceDrift = true; return 0; };
            Reject(() => drift.Execute()); drift.SourceDrift = false;
            Require(drift.Last?.FailedAt == FalloutPlayerWeaponContinuationStep.ActorStatus &&
                drift.Common.Capture().Current.Single().SceneFields?.WeaponContinuation is { CacheStores: 0 },
                "Source drift inside the current pool getter committed stale process fields.");
        }
        var priorForeign = JsonSerializer.Serialize(warm.Common.Capture());
        Task.Run(() => Reject(() => warm.Execute())).GetAwaiter().GetResult();
        Require(priorForeign == JsonSerializer.Serialize(warm.Common.Capture()), "Foreign caller mutated source weapon continuation fields.");
        Reject(() => warm.Common.ReadPlayerWeaponAction(warm.Player, 2));
        using (var nonnull = new WeaponContinuationFixture())
        {
            nonnull.Initialize(); nonnull.AuthorAction = 1;
            nonnull.AuthorWeaponEntry = new(Guid.NewGuid(), new("WeaponFixture.esm", 0x201), 1);
            Reject(() => nonnull.Execute());
            Require(nonnull.Last is { FailedAt: FalloutPlayerWeaponContinuationStep.WeaponEntry, WeaponEntry.WeaponEntry: not null } &&
                nonnull.Calls.SequenceEqual(["action", "suppression", "entry"]),
                "The independent nonnull weapon arm bypassed its unowned state/form consumers.");
        }
        Console.WriteLine("OPENNV_PLAYER_WEAPON_CONTINUATION_PASS constructorWriter=true completeContext=true currentPool=true " +
            "cacheStoreOrder=true cacheHit=true coldCycles=3 observedWriterRefused=true failedEffects=true sourceDriftRefused=true " +
            "reentryRefused=true foreignCallerRefused=true wholePlayer=UNEXECUTED");
    }

    private sealed class WeaponContinuationFixture : IFalloutPlayerWeaponContinuationConsumers, IDisposable
    {
        internal readonly FalloutFormKey Player = new("WeaponFixture.esm", 0x14);
        internal readonly FalloutPlayerWeaponContinuationSource Source;
        internal readonly FalloutActorProcessCommonState Common;
        internal readonly string Stack = new('2', 64);
        internal readonly Guid PlayerProcess = Guid.NewGuid(), Main = Guid.NewGuid(), Invocation = Guid.NewGuid(), Constructor = Guid.NewGuid();
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _entered;
        internal long Ordinal = 1;
        internal int PoolReads;
        internal bool SourceDrift;
        internal FalloutActorValue Pool = new(0, .5f, .25f, -.75f);
        internal Func<float>? ReadPool;
        internal byte? AuthorAction;
        internal FalloutProcessWeaponEntry? AuthorWeaponEntry;
        internal List<string> Calls { get; } = [];
        internal FalloutPlayerWeaponContinuationCall? Last;

        internal WeaponContinuationFixture(FalloutProcessCommonSnapshot? saved = null)
        {
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            Source = FalloutPlayerWeaponContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            Common = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), Stack,
                actor => new(actor, new("WeaponFixture.esm", SourceDrift ? 8u : 7u), "ENGINE_PLAYER", 0, main.EngineSha256,
                    "NPC_", 0, new('4', 64), true),
                _ => throw new InvalidOperationException("Following getter called unrelated gameplay/body pools."),
                _ => throw new InvalidOperationException("Following getter called a native body factory."), saved);
            if (saved is null) Common.Construct(Player, 1, FalloutDetectionProcessLevel.High);
        }
        internal void Initialize() => Common.InitializePlayerWeaponActionConstructor(Player, 1, Source, Constructor, () =>
        { if (_entered || _thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Foreign or entered constructor caller."); });
        internal FalloutPlayerWeaponContinuationCall Execute()
        {
            if (_entered || _thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Foreign or recursive component caller.");
            _entered = true; Calls.Clear(); Last = null;
            try { return FalloutPlayerWeaponContinuation.Execute(Source, Stack, PlayerProcess, Main, Ordinal, Invocation, this, call => Last = call); }
            finally { _entered = false; }
        }
        public void RequireCurrent(Guid main, long ordinal, Guid invocation)
        {
            if (!_entered || _thread != Environment.CurrentManagedThreadId || main != Main || ordinal != Ordinal || invocation != Invocation)
                throw new InvalidOperationException("Component caller changed its actual process/thread/invocation.");
        }
        public FalloutProcessWeaponActionRead ReadAction()
        { Calls.Add("action"); var read = Common.ReadPlayerWeaponAction(Player, 1); return AuthorAction is { } value ? read with { Value = value } : read; }
        public bool ReadSuppression() { Calls.Add("suppression"); return false; }
        public FalloutProcessSceneQuery ReadWeaponEntry()
        { Calls.Add("entry"); return Common.ReadSceneQueries(Player, 1) with { WeaponEntry = AuthorWeaponEntry }; }
        public FalloutProcessWeaponActivityRead ReadActivity() { Calls.Add("activity"); return Common.ReadPlayerWeaponActivity(Player, 1); }
        public FalloutProcessActorStatusRead ReadStatus()
        {
            Calls.Add("status"); return Common.ReadPlayerFollowingStatus(Player, 1, () =>
            {
                ++PoolReads; if (ReadPool is not null) return ReadPool();
                var current = Pool.Base + Pool.Temporary; current += Pool.Permanent; return current + Pool.Damage;
            });
        }
        public void Dispose() => Common.Dispose();
    }
}
