using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerIdleAnimation()
    {
        RunPlayerPoseBounds();
        RunIdlePackageSelection();
        var fixture = new AuthoredIdleConsumers { NullModel = true };
        var fields = FalloutActorIdleAnimationFields.Constructed;
        FalloutActorIdleAnimationTrace? trace = null;
        Execute();
        Require(trace is { Returned: true, Step: "null-model-return", Models.Count: 1, Animation: null } &&
            fixture.Calls.SequenceEqual(["model"]) && fields == FalloutActorIdleAnimationFields.Constructed,
            "Actual null-model return consumed an independent animation or timer child.");
        fixture = new(); fields = fields with { WaitBits = BitConverter.SingleToUInt32Bits(.01f) }; Execute();
        Require(trace is { Returned: true, Step: "subtract-return", MainTimerBits: 0x3f000000, Models.Count: 2 } &&
            fields.WaitBits == BitConverter.SingleToUInt32Bits(.01f - .5f) && fields.Stores == 1 &&
            fixture.Calls.SequenceEqual(["model", "object", "animation", "model", "bound", "distance", "distance-setting", "actor-state", "actor-class", "main-timer"]),
            "Idle update clamped its wait, skipped a model requery or used the delivered scene argument.");
        fixture = new() { WaitSettings = [float.NaN, -2f] };
        fields = FalloutActorIdleAnimationFields.Constructed with { WaitBits = 0xbf800000 }; Execute();
        Require(trace is { Returned: true, Step: "reset-return", IdleCalled: true, MainTimerBits: null } &&
            fields.WaitBits == 0xc0000000 && fixture.Calls.TakeLast(3).SequenceEqual(["wait-setting", "idle-child", "wait-setting"]),
            "Source unordered wait setting skipped its idle call or failed to reread the setting after the child.");
        fixture = new() { WaitSettings = [-0f] };
        fields = FalloutActorIdleAnimationFields.Constructed with { WaitBits = 0xbf800000 }; Execute();
        Require(trace is { Step: "subtract-return", IdleCalled: false } && fields.WaitBits == 0xbfc00000,
            "Signed zero wait setting entered the unequal/unordered source reset arm.");
        fixture = new() { Animation = null }; fields = FalloutActorIdleAnimationFields.Constructed; Execute();
        Require(trace is { Step: "null-animation-return", Returned: true, Models.Count: 2, RadiusBits: 0x42800000, DistanceBits: 0 } &&
            fields.Stores == 0 && fixture.Calls.Last() == "distance-setting",
            "Null animation was inferred from a model or skipped the preceding bound/distance queries.");
        fixture = new() { Distance = 2001 }; fields = FalloutActorIdleAnimationFields.Constructed; Execute();
        Require(trace is { Step: "distance-return", Returned: true } && fields.Stores == 0 &&
            fixture.Calls.Last() == "distance-setting", "Idle distance omitted source radius scaling before its winning threshold.");
        fixture = new() { Distance = float.NaN }; fields = FalloutActorIdleAnimationFields.Constructed; Execute();
        Require(trace is { Step: "subtract-return", Returned: true }, "Unordered source distance was treated as ordered greater.");
        fixture = new() { DisappearSecondModel = true }; fields = FalloutActorIdleAnimationFields.Constructed;
        Reject(() => Execute());
        Require(trace is { Returned: false, Failure: not null, Models.Count: 2 } && fixture.Calls.Last() == "model",
            "Idle second-model disappearance borrowed the earlier model or continued to bounds.");
        fixture = new() { ThrowAfterClear = true }; fields = FalloutActorIdleAnimationFields.Constructed with { Flag = 1 };
        Reject(() => Execute());
        Require(fields is { Flag: 0, Stores: 1 } && trace is { Returned: false, Failure: not null },
            "Failed High idle child discarded the already committed animation flag clear.");
        foreach (var (first, second, blocked, allowed) in new (FalloutIdleAnimationAction?, FalloutIdleAnimationAction?, bool, bool)[]
        {
            (null, null, false, true), (new(0, null), null, true, false),
            (new(3, null), new(3, 2), false, false), (new(3, 0), null, false, true),
            (new(3, 2), null, true, true), (new(3, 5), null, true, true),
            (new(3, 0), new(0, null), true, false), (new(3, 0), new(3, 0), false, false)
        })
        {
            var actual = new FalloutIdleAnimationContext(Guid.NewGuid(), first, second);
            Require(actual.BlocksIdle == blocked && actual.AllowsIdle == allowed,
                "Independent source animation slots changed their asymmetric idle predicates.");
        }
        fixture = new() { FailAnyCall = true }; fields = FalloutActorIdleAnimationFields.Constructed;
        Execute(FalloutDetectionProcessLevel.Low);
        Require(trace is { Step: "lower-class-return", Returned: true, Models.Count: 0 } && fixture.Calls.Count == 0,
            "Lower process empty virtual consumed High animation state.");
        Console.WriteLine("OPENNV_PLAYER_IDLE_ANIMATION_PASS selectedModelRequery=true independentContext=true " +
            "sourcePredicates=true rawWaitSetting=true committedFailure=true nativeAnimationFactories=UNOWNED gameplay=UNEXECUTED");
        void Execute(FalloutDetectionProcessLevel level = FalloutDetectionProcessLevel.High) =>
            FalloutActorIdleAnimationUpdate.Execute(level, fixture, () => fields, value => fields = value, value => trace = value);
    }

    private sealed class AuthoredIdleConsumers : IFalloutActorIdleAnimationConsumers
    {
        internal readonly List<string> Calls = [];
        internal bool NullModel, DisappearSecondModel, ThrowAfterClear, FailAnyCall;
        internal FalloutIdleAnimationContext? Animation = new(Guid.NewGuid(), null, null);
        internal float[] WaitSettings = [1];
        internal float Distance;
        internal Action? ModelCallback;
        internal Func<FalloutSourceSceneNode?>? ModelOverride;
        internal FalloutIdlePackageRead? PackageRead;
        public FalloutIdlePackageRead? SelectedPackageRead => PackageRead;
        private int _models, _settings;
        private readonly FalloutSourceSceneNode _model = new(Guid.NewGuid(), Guid.NewGuid(), 123);
        public string Owner => "authored-independent-idle-children";
        private void Call(string name) { Calls.Add(name); if (FailAnyCall) throw new InvalidOperationException("Forbidden child " + name); }
        public FalloutSourceSceneNode? ReadModel()
        {
            Call("model"); ++_models;
            ModelCallback?.Invoke();
            if (_models == 2 && ThrowAfterClear) throw new InvalidOperationException("Actual second model failed after flag clear.");
            if (ModelOverride is not null) return ModelOverride();
            return NullModel || _models == 2 && DisappearSecondModel ? null : _model;
        }
        public FalloutIdleSelectedObject? ReadSelectedObject() { Call("object"); return null; }
        public FalloutIdleAnimationContext? ReadAnimation() { Call("animation"); return Animation; }
        public FalloutIdleSelectedObject? ReadAlternateObject() { Call("alternate-object"); return null; }
        public uint? ReadBoundRadius(FalloutSourceSceneNode model) { Call("bound"); return 0x42800000; }
        public float ReadDistanceToPlayer() { Call("distance"); return Distance; }
        public float ReadDistanceSetting() { Call("distance-setting"); return 2000; }
        public bool ReadActorStatePredicate() { Call("actor-state"); return false; }
        public bool ReadActorClassPredicate() { Call("actor-class"); return false; }
        public float ReadWaitSetting() { Call("wait-setting"); return WaitSettings[Math.Min(_settings++, WaitSettings.Length - 1)]; }
        public float ReadMainScaledTimer() { Call("main-timer"); return .5f; }
        public void StartIdle() { Call("idle-child"); }
    }

    private static void RunIdlePackageSelection()
    {
        var first = new FalloutIdleSelectedObject(6, true);
        var replacement = new FalloutIdleSelectedObject(7, false);
        var primaryReads = 0; var currentReads = 0;
        var value = FalloutIdlePackageSelection.Read(() => { ++primaryReads; return null; },
            () => { ++currentReads; return first; });
        Require(value == first && primaryReads == 1 && currentReads == 1,
            "Null primary did not select the independent current-package getter.");
        primaryReads = 0; currentReads = 0;
        value = FalloutIdlePackageSelection.Read(() => ++primaryReads == 1 ? first : replacement,
            () => { ++currentReads; return null; });
        Require(value == replacement && primaryReads == 2 && currentReads == 0,
            "Nonnull primary was cached or queried the unrelated current slot.");
        primaryReads = 0;
        value = FalloutIdlePackageSelection.Read(() => ++primaryReads == 1 ? first : null,
            () => throw new InvalidOperationException("Disappearing primary queried current."));
        Require(value is null && primaryReads == 2, "Second primary null was replaced with a stale retained package.");
    }
}
