using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerMoverDeltaContracts()
    {
        RunReferenceModelCold();
        using (var fixture = new MoverDeltaFixture())
        {
            var returned = fixture.Frame(1);
            Require(returned is { Returned: true, MovementReturned: true, Data.Data: null,
                SelectedAnimation: null, Activities.Count: 1, Controllers.Count: 2, Parent.Cell: null } &&
                fixture.Data.Capture().Calls.Count == 1 && fixture.Mover.Capture().PlayerFrame!.Vector!.Returned,
                "Actual null-data movement lost its ordered getters or allocated a model/data object from frame delivery.");
            var unchanged = JsonSerializer.Serialize(fixture.Delta.Capture());
            Reject(() => fixture.Delta.Update(returned.Invocation));
            Require(JsonSerializer.Serialize(fixture.Delta.Capture()) == unchanged,
                "A replayed frame mutated the bounded mover-delta receipt.");
            for (var ordinal = 2; ordinal <= 8; ++ordinal) fixture.Frame(ordinal);
            var saved = JsonSerializer.Deserialize<FalloutPlayerMoverDeltaSnapshot>(JsonSerializer.Serialize(fixture.Delta.Capture()))!;
            Require(saved.Calls == 8 && saved.LastCall!.Ordinal == 8 && saved.LastCall.Controllers.Count == 2,
                "Repeated mover delta accumulated frame journals or changed its actual current getter order.");
            FalloutPlayerMoverDeltaState.Validate(saved);
            var lastCall = saved.LastCall ?? throw new InvalidDataException("Missing returned mover delta.");
            Reject(() => FalloutPlayerMoverDeltaState.Validate(saved with { LastCall = lastCall with { Data = null } }));
            Reject(() => FalloutPlayerMoverDeltaState.Validate(saved with { LastCall = lastCall with { Parent = null } }));
            Reject(() => FalloutPlayerMoverDeltaState.Validate(saved with { LastCall = lastCall with { Activities = [] } }));
            Reject(() => FalloutPlayerMoverDeltaState.Validate(saved with { LastCall = lastCall with { Life = null } }));
        }
        using (var fixture = new MoverDeltaFixture())
        {
            fixture.AllocateData();
            var data = fixture.Data.Capture();
            Require(data.Data is not null && data.Fields is { Model: null, OtherModel: null, FirstWord: 0,
                SecondWord: 0, ThirdWord: 0, WaterBits: FalloutPlayerMoverDeltaSource.UninitializedWater,
                ScalarBits: FalloutPlayerMoverDeltaSource.UninitializedScalar } && data.Calls[^1].Stores.Count == 6,
                "The real data allocation did not retain its independent model-null/scalar constructor stores.");
            Reject(() => fixture.Frame(1));
            Require(fixture.Delta.LastCall is { Returned: false, FailedAt: FalloutPlayerMoverDeltaStep.ActorMovement,
                Failure: FalloutPlayerMoverDeltaState.ActorMovementBoundary, Data.Data: not null, Data.Fields.Model: null } &&
                fixture.Delta.LastCall.Parent is null,
                "Nonnull data with null model silently returned Actor movement or crossed its unreturned child.");
            var history = JsonSerializer.Serialize(data.Calls);
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var cold = new MoverDeltaFixture(data);
                var rebound = cold.Data.Capture();
                Require(rebound.Process != data.Process && rebound.CommonProcess != data.CommonProcess &&
                    rebound.Factory != data.Factory && rebound.Data != data.Data && rebound.Fields == data.Fields &&
                    JsonSerializer.Serialize(rebound.Calls) == history && rebound.Publications.Count == data.Publications.Count + 1,
                    "Cold reference data replayed an allocation/setter or reused historical object/process identities.");
                data = JsonSerializer.Deserialize<FalloutReferenceModelDataSnapshot>(JsonSerializer.Serialize(rebound))!;
            }
            var stores = data.Calls[^1].Stores.ToArray(); (stores[1], stores[2]) = (stores[2], stores[1]);
            var calls = data.Calls.ToArray(); calls[^1] = calls[^1] with { Stores = stores };
            Reject(() => FalloutReferenceModelDataState.Validate(data with { Calls = calls }));
        }
        using (var fixture = new MoverDeltaFixture())
        {
            fixture.AllocateData(water: true, expectedFailure: true);
            var partial = fixture.Data.Capture();
            Require(partial.Calls[^1] is { Returned: false, Stores.Count: 3 } && partial.Data is not null &&
                partial.Fields is { FirstWord: 0, SecondWord: 0, WaterBits: null, ScalarBits: null, ThirdWord: null },
                "Unowned water getter erased its actual allocated data/word prefix or fabricated later scalar stores.");
            Reject(() => fixture.Frame(1));
        }
        using (var fixture = new MoverDeltaFixture())
        {
            fixture.DataWriter(); Reject(() => fixture.Frame(1));
            Require(fixture.Data.SaveBlocker == "authored-entered-reference-model-copy" && fixture.Data.Capture().Data is null,
                "An entered data/model writer was hidden by a historical constructor-null pointer.");
        }
        VerifyMoverLoadedValues();
        Console.WriteLine("OPENNV_PLAYER_MOVER_DELTA_PASS authoredScopeOnly=true orderedNullDataReturn=true " +
            "realDataAllocation=true nonnullDataNullModelRefused=true waterFailedPrefix=true currentWriterBarrier=true " +
            "boundedFrameHistory=true dataColdCycles=3 gameplay=UNEXECUTED nonnullActorMovement=UNEXECUTED");
    }

    private sealed class UnreachedDeltaPosition : IFalloutReferencePositionConsumers
    {
        public Guid Factory { get; } = Guid.NewGuid();
        private static Exception Unreached() => new InvalidOperationException("Authored null-data arm reached an unrelated position child.");
        public void RequireCurrent(FalloutReferencePositionInvocation invocation) => throw Unreached();
        public FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation) => throw Unreached();
        public bool ReadPersistentReference(FalloutReferencePositionInvocation invocation, FalloutReferenceFlagRead flags) => throw Unreached();
        public byte ReadBaseFormKind(FalloutReferencePositionInvocation invocation) => throw Unreached();
        public FalloutReferenceParentCellRead ReadCurrentParentCell(FalloutReferencePositionInvocation invocation) => throw Unreached();
        public FalloutFormKey? ReadCellWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey cell) => throw Unreached();
        public void DetachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace) => throw Unreached();
        public void AttachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace) => throw Unreached();
        public FalloutReferenceChangeCall ReferenceChanged(FalloutReferencePositionInvocation invocation, uint mask) => throw Unreached();
        public void ResetCurrentControllerAndModel(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition position) => throw Unreached();
        public FalloutPackageRawPosition SelectInitialPlacement(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition supplied) => throw Unreached();
        public void SetInitialPlacementYaw(FalloutReferencePositionInvocation invocation) => throw Unreached();
        public void ClearStartupChangeSuppression(FalloutReferencePositionInvocation invocation) => throw Unreached();
    }
    private sealed class MoverDeltaFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNV-MoverDelta-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _worldProcess = Guid.NewGuid(), _worldFactory = Guid.NewGuid();
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private FalloutReferenceModelDataInvocation? _dataCall;
        private FalloutReferenceScriptExtraInitializationInvocation? _script;
        private FalloutPlayerLateFrameInvocation? _frame;
        private bool _alive = true;
        internal readonly FalloutPluginStack Records;
        internal readonly FalloutCombatActorIdentity Actor;
        internal readonly string Stack = new('7', 64);
        internal readonly FalloutPlayerMoverDeltaSource Source;
        internal readonly FalloutActorProcessCommonState Common;
        internal readonly FalloutActorProcessRuntimeState Runtime;
        internal readonly FalloutStandalonePlayerSceneState Scene;
        internal readonly FalloutActorPackageCommandFields Fields;
        internal readonly FalloutReferenceScriptExtraInitializer Flags;
        internal readonly FalloutActorMoverState Mover;
        internal readonly FalloutReferenceModelDataState Data;
        internal readonly FalloutPlayerMoverDeltaState Delta;
        internal readonly FalloutStandaloneInterfaceState Interface;
        private readonly FalloutSourceReferencePositionState _position;
        internal MoverDeltaFixture(FalloutReferenceModelDataSnapshot? saved = null)
        {
            Directory.CreateDirectory(_directory);
            var acbs = new byte[24]; BinaryPrimitives.WriteInt16LittleEndian(acbs.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(14), 100);
            File.WriteAllBytes(Path.Combine(_directory, "MoverDeltaFixture.esm"), Merge(
                Row("TES4", 0, Field("HEDR", new byte[12])),
                Row("NPC_", 7, Field("ACBS", acbs), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
                Row("CELL", 0x400, Field("DATA", [3])),
                Setting(0x500, "fAVDDefaultBodyPartCondition", 100), Setting(0x501, "fMoveBaseSpeed", 85),
                Setting(0x502, "fMoveRunMult", 4), Setting(0x503, "fMoveWeightMin", 0),
                Setting(0x504, "fMoveWeightMax", 150), Setting(0x505, "fMoveEncumEffect", .4f),
                Setting(0x506, "fMoveOneCrippledLegSpeedMult", .85f), Setting(0x507, "fMoveTwoCrippledLegsSpeedMult", .75f)));
            Records = FalloutPluginStack.Load(_directory, ["MoverDeltaFixture.esm"]);
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            var basis = Records.GetEffective(Records.RuntimeFormKey(7));
            Actor = new(Records.RuntimeFormKey(0x14), basis.FormKey, "ENGINE_PLAYER", 0, main.EngineSha256,
                basis.Signature, basis.Flags, Convert.ToHexString(SHA256.HashData(basis.ReadData())).ToLowerInvariant(), true);
            var declaration = FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256);
            Common = new(declaration, Stack, _ => Actor, _ => throw new InvalidOperationException("Delta fixture cannot duplicate gameplay."),
                _ => throw new InvalidOperationException("Delta fixture cannot invent a controller."));
            Common.Construct(Actor.Reference, 1, FalloutDetectionProcessLevel.High);
            Runtime = new(declaration, Stack, Actor.Reference, _ => Actor);
            Scene = new(FalloutStandalonePlayerSceneSource.Read(main), Stack, Guid.NewGuid());
            var fieldSource = FalloutPackageCommandFieldsSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            var current = Common.ReadSceneQueries(Actor.Reference, 1);
            Fields = new(fieldSource, Stack, Actor, current.Process, 1, Scene.Constructor,
                _ => throw new InvalidOperationException("A late frame cannot impersonate a package command."),
                new("authored-actual-Player-field-constructor", ClosedLanes: [FalloutPackageCommandFieldLane.Animation]));
            Source = FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(fieldSource, Scene.ThreadContext.Source));
            Flags = new(FalloutReferenceScriptExtraInitializerSource.Reviewed(fieldSource), Records, Fields, Scene.ThreadContext,
                invocation => { Lifetime(); if (invocation != _script) throw new InvalidOperationException("Wrong authored initializer scope."); });
            _script = new(Guid.NewGuid(), Actor, current.Process, 1, Scene.Constructor, _thread);
            try { Flags.Initialize(_script); } finally { _script = null; }
            Mover = Scene.ActorMover;
            Mover.BindPlayerFrame(Actor, invocation =>
            { Guard(invocation.Caller, invocation.PlayerProcess); if (invocation.Common != Current()) throw new InvalidOperationException("Wrong authored current Common."); });
            Data = new(Source, Records, Stack, Scene.Process, current.Process, Scene.Constructor, Actor, 1,
                invocation => { Lifetime(); if (invocation != _dataCall) throw new InvalidOperationException("Wrong authored reference-data scope."); }, Lifetime, saved);
            if (saved is null) DataCall(FalloutReferenceModelDataIngress.ReferenceConstructor, Data.ConstructReferenceField);
            Interface = new(FalloutStandaloneInterfaceSource.Read(main), Stack, Guid.NewGuid());
            _position = new(FalloutReferencePositionSource.Reviewed(fieldSource), Records, Stack, Actor, Scene.Process,
                Scene.Constructor, _ => throw new InvalidOperationException("Null-data arm cannot enter a position writer."));
            Delta = new(Source, Records, Stack, Scene.Process, Scene.Constructor, current,
                new(Mover, Common, Runtime, Fields, Flags, Data, Interface, View, Parent,
                    (_, _) => throw new InvalidOperationException("Null-data arm cannot fabricate a position scope."), _position, new UnreachedDeltaPosition()),
                RequireFrame, Lifetime);
        }
        private FalloutProcessSceneQuery Current() => Common.ReadSceneQueries(Actor.Reference, 1);
        internal FalloutPlayerMoverDeltaCall Frame(long ordinal)
        {
            _frame = new(new(Guid.NewGuid(), ordinal, Guid.NewGuid()), Scene.Process, Scene.Constructor, Guid.NewGuid(), Current(), _thread, 0x3c888889);
            try
            {
                var call = new FalloutActorMoverFrameInvocation(_frame.Caller, Scene.Process, _frame.Common, _thread);
                Mover.StorePlayerFrameFlags(call, 0); Mover.StorePlayerFrameDefaultVector(call);
                return Delta.Update(_frame);
            }
            finally { _frame = null; }
        }
        internal void AllocateData(bool water = false, bool expectedFailure = false)
        {
            Action action = () => DataCall(FalloutReferenceModelDataIngress.ThreeDLoad,
                invocation => Data.EnsureForThreeDLoad(invocation, ParentRead(water ? Records.RuntimeFormKey(0x400) : null)));
            if (expectedFailure) Reject(action); else action();
        }
        internal void DataWriter() => DataCall(FalloutReferenceModelDataIngress.UnownedWriter,
            invocation => Data.ObserveUnownedWriter(invocation, "authored-entered-reference-model-copy"));
        internal void DataCall(FalloutReferenceModelDataIngress ingress, Action<FalloutReferenceModelDataInvocation> body)
        {
            _dataCall = new(Guid.NewGuid(), Actor, Scene.Process, Scene.Constructor, Current().Process, 1, _thread, ingress);
            try { body(_dataCall); } finally { _dataCall = null; }
        }
        private FalloutPlayerMoverDeltaViewRead View(FalloutPlayerLateFrameInvocation invocation)
        {
            RequireFrame(invocation);
            // The authored leaf borrows the same real Scene field. Production
            // calls its guarded ReadMoverDeltaView while the actual prelude is entered.
            var scene = Scene.Capture();
            return new(Scene.Source.Identity, Stack, Scene.Process, Scene.Constructor, invocation.Caller, scene.Sequence, scene.PlayerView);
        }
        private FalloutReferenceParentCellRead Parent(FalloutPlayerLateFrameInvocation invocation)
        { RequireFrame(invocation); return ParentRead(null); }
        private FalloutReferenceParentCellRead ParentRead(FalloutFormKey? cell) => new("actual-authored-World-reference-ParentCELL", Stack,
            _worldProcess, _worldFactory, Scene.Constructor, Actor, 0, cell, Source.Controller.Fields.Controls.Main.EngineSha256);
        internal void RequireLoad(FalloutReferenceModelDataInvocation invocation)
        { Lifetime(); if (invocation != _dataCall) throw new InvalidOperationException("Wrong authored loaded Player scope."); }
        internal void RequireLife() => Lifetime();
        internal T ReadFrameLeaf<T>(Func<FalloutPlayerLateFrameInvocation, T> body)
        {
            _frame = new(new(Guid.NewGuid(), 1, Guid.NewGuid()), Scene.Process, Scene.Constructor, Guid.NewGuid(), Current(), _thread, 0x3c888889);
            try { return body(_frame); } finally { _frame = null; }
        }
        internal void RequireFrame(FalloutPlayerLateFrameInvocation invocation)
        { Lifetime(); if (invocation != _frame || invocation.Common != Current()) throw new InvalidOperationException("Wrong authored entered frame."); }
        private void Guard(FalloutPlayerSourceCaller caller, Guid process)
        { Lifetime(); if (_frame?.Caller != caller || process != Scene.Process) throw new InvalidOperationException("Wrong authored mover frame."); }
        private void Lifetime() { if (!_alive || Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Retired/foreign authored delta thread."); }
        public void Dispose()
        {
            try { Delta.Retire(); Data.Retire(); Mover.Retire(); _position.Retire(); Fields.Retire(); Interface.Retire(); Scene.Retire(); Common.Dispose(); Runtime.Dispose(); }
            finally { _alive = false; Records.Dispose(); Directory.Delete(_directory, true); }
        }
        private static byte[] Setting(uint id, string name, float value) => Row("GMST", id, Field("EDID", Encoding.ASCII.GetBytes(name + "\0")), Field("DATA", BitConverter.GetBytes(value)));
        private static byte[] Merge(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
        private static byte[] Field(string signature, byte[] data)
        { var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result; }
        private static byte[] Row(string signature, uint id, params byte[][] fields)
        { var data = Merge(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result; }
    }
}
