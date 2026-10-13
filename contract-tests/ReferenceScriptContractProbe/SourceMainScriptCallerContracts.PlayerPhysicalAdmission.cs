using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerPhysicalAdmission()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutActorPhysicalAdmissionSource.Reviewed(main);
        var stack = new string('2', 64);
        var player = new FalloutFormKey("PhysicalAdmissionFixture.esm", 0x14);
        var npc = new FalloutFormKey("PhysicalAdmissionFixture.esm", 0x101);
        FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, new("PhysicalAdmissionFixture.esm", 7),
            actor == player ? "ENGINE_PLAYER" : "ACHR", 0, main.EngineSha256, "NPC_", 0, new('4', 64), actor == player);
        using var runtime = new FalloutActorProcessRuntimeState(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256),
            stack, player, Identity);
        using var common = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256),
            stack, Identity, _ => throw new InvalidOperationException("Physical getter copied gameplay."),
            _ => throw new InvalidOperationException("Physical getter allocated a body."));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High);
        common.Construct(npc, 1, FalloutDetectionProcessLevel.Low);
        FalloutActorPhysicalAdmissionFields.Constructed.Validate();
        Reject(() => (FalloutActorPhysicalAdmissionFields.Constructed with { State = 2 }).Validate());
        Reject(() => (source with { Contract = new('8', 64) }).Validate());
        var before = common.Capture(); var first = common.ReadPhysicalActivity(player, 1);
        Require(first.Value == 0 && common.Capture().Sequence == before.Sequence,
            "Physical activity was borrowed from idle Action=-1 or its pure signed getter mutated state.");
        Reject(() => common.ReadPhysicalActivity(player, 2));
        Reject(() => common.ReadPhysicalActivity(npc, 1));
        var owner = new PhysicalRegistrationFixture(source, player, first)
        {
            ModeReader = () => runtime.Life(player),
            ActivityReader = () => common.ReadPhysicalActivity(player, 1)
        };
        FalloutActorPhysicalRegistrationTrace? retained = null;
        var ordinary = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, value => retained = value);
        Require(ordinary is { Returned: true, Register: false, Motion: 6, ModelReturned: true, ProcessPresent: true } &&
            ordinary.Modes.Count == 2 && ordinary.Activities.Count == 5 && owner.ActivityReads == 5 &&
            ordinary.Activities.All(query => query.Value == 0) && common.Capture().Sequence == before.Sequence,
            "Ordinary constructed Player registration was inferred from first-person or collision counts instead of source mode/activity.");
        FalloutActorPhysicalRegistrationPredicate.Validate(ordinary);
        var history = JsonSerializer.Deserialize<FalloutActorPhysicalRegistrationTrace>(JsonSerializer.Serialize(ordinary))!;
        FalloutActorPhysicalRegistrationPredicate.Validate(history);
        using var cold = new FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256),
            stack, Identity, _ => throw new InvalidOperationException("Cold physical getter copied gameplay."), _ => null,
            JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(common.Capture()))!);
        var coldQuery = cold.ReadPhysicalActivity(player, 1);
        Require(coldQuery.Value == 0 && coldQuery.Process.Process != first.Process.Process &&
            history.Activities[0].Process.Process == first.Process.Process &&
            cold.Capture().Current.Single(entry => entry.Source.EnginePlayer).SceneFields!.PhysicalActivity is { Value: 0, Stores: 0 },
            "Cold physical activity replayed a writer or promoted historical process/model IDs.");
        Reject(() => FalloutActorProcessCommonState.Validate(before with
        {
            Current = before.Current.Select(entry => entry.Source.EnginePlayer ? entry with
            { SceneFields = entry.SceneFields! with { PhysicalActivity = new(-1, 0) } } : entry).ToArray()
        }));
        Reject(() => FalloutActorProcessCommonState.Validate(before with { Schema = "opennv-actor-process-common/v8" }));
        Reject(() => FalloutActorPhysicalRegistrationPredicate.Validate(history with { Register = true, Motion = 1 }));
        Reject(() => FalloutActorPhysicalRegistrationPredicate.Validate(history with { Activities = history.Activities.Take(4).ToArray() }));
        foreach (var mode in new[] { 1, 6 })
        {
            owner = new(source, player, first) { ModeReader = () => new(mode, "authored-independent-mode") };
            var qualified = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, _ => { });
            Require(qualified is { Register: true, Motion: 1 } && owner.ModelReads == 1 && owner.ActivityReads == 0,
                "Qualified mode did not inspect its actual reference model before returning.");
            FalloutActorPhysicalRegistrationPredicate.Validate(qualified);
        }
        owner = new(source, player, first) { ModeReader = () => new(2, "authored-independent-mode") };
        var direct = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, _ => { });
        Require(direct is { Register: true, Motion: 1, ModelReturned: false } && owner.ModelReads == 0 && owner.ActivityReads == 0,
            "Caller mode-two short circuit entered the separate predicate or model getter.");
        FalloutActorPhysicalRegistrationPredicate.Validate(direct);
        owner = new(source, player, first) { Model = null, ModeReader = () => new(1, "authored-independent-mode") };
        var absent = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, _ => { });
        Require(absent is { Register: false, Motion: 6 } && absent.Modes.Count == 1 && owner.ActivityReads == 0,
            "Predicate mode substituted for its missing actual reference model.");
        FalloutActorPhysicalRegistrationPredicate.Validate(absent);
        owner = new(source, player, first) { ProcessPresent = false };
        var missingProcess = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, _ => { });
        Require(missingProcess is { Register: false, Motion: 6 } && owner.ActivityReads == 0,
            "Null current process became an invented signed-activity zero read.");
        FalloutActorPhysicalRegistrationPredicate.Validate(missingProcess);
        var orderedValues = new Queue<int>([0, 0, 0, 0, 5]);
        owner = new(source, player, first) { ActivityReader = () => first with { Value = orderedValues.Dequeue() } };
        var fifth = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, _ => { });
        Require(fifth is { Register: true, Motion: 1 } && fifth.Activities.Count == 5 && orderedValues.Count == 0,
            "Physical predicate cached the first activity read across later independent calls.");
        FalloutActorPhysicalRegistrationPredicate.Validate(fifth);
        owner = new(source, player, first) { ModeReader = () => new(4, "authored-independent-mode") };
        Reject(() => FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, value => retained = value));
        Require(retained is { Returned: false, Step: "active-effects", Failure: FalloutActorPhysicalRegistrationPredicate.EffectBoundary },
            "Mode-four effect selection was silently passed or its reached prefix disappeared.");
        FalloutActorPhysicalRegistrationPredicate.Validate(retained!);
        runtime.RetainUnownedLifeTransition(player, "authored-reached-life-writer");
        owner = new(source, player, first) { ModeReader = () => runtime.Life(player) };
        Reject(() => FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue, owner, value => retained = value));
        Require(retained is { Returned: false, Step: "caller-mode", Failure: "authored-reached-life-writer" } && retained.Modes.Count == 1,
            "Physical predicate discarded a failed genuine life observation or substituted constructor zero.");
        FalloutActorPhysicalRegistrationPredicate.Validate(retained!);
        Console.WriteLine("OPENNV_PLAYER_PHYSICAL_ADMISSION_PASS constructorSourceFields=true separateSignedProcessActivity=true orderedPredicate=true livingModelRequired=true coldHistoryOnly=true unownedWritersRefused=true gameplay=UNEXECUTED");
    }

    private sealed class PhysicalRegistrationFixture(FalloutActorPhysicalAdmissionSource source,
        FalloutFormKey actor, FalloutProcessPhysicalActivityQuery query) : IFalloutActorPhysicalRegistrationConsumers
    {
        public FalloutActorPhysicalAdmissionSource Source => source;
        public FalloutFormKey Actor => actor;
        public string Owner => "authored-physical-registration-consumers";
        internal Func<FalloutActorProcessFact<int>> ModeReader { get; init; } = () => new(0, "authored-independent-mode");
        internal Func<FalloutProcessPhysicalActivityQuery> ActivityReader { get; init; } = () => query;
        internal FalloutSourceSceneNode? Model { get; init; } = new(Guid.NewGuid(), Guid.NewGuid(), 1);
        internal bool ProcessPresent { get; init; } = true;
        internal int ModelReads { get; private set; }
        internal int ActivityReads { get; private set; }
        public void RequireCurrent() { }
        public FalloutActorProcessFact<int> ReadMode() => ModeReader();
        public FalloutSourceSceneNode? ReadReferenceModel() { ++ModelReads; return Model; }
        public bool HasCurrentProcess() => ProcessPresent;
        public FalloutProcessPhysicalActivityQuery ReadCurrentProcessActivity() { ++ActivityReads; return ActivityReader(); }
    }
}
