using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeEarlyControllerAudit
{
    // This is an authored entered-child fixture with actual native resources.
    // Source qualification comes from the separate owned declaration audit;
    // it does not claim the enclosing Actor/Player frame returned.
    private static void ExerciseMoverDelta(FalloutEarlyControllerSource early, FalloutPhysicalConstructorInputs inputs)
    {
        using var records = new MoverDeltaNativeRecords(early.Fields.Controls.Main);
        var source = FalloutPlayerMoverDeltaSource.Reviewed(early);
        using var fixture = new NativeFixture(early, inputs, records.Actor, records.Stack);
        using var caller = fixture.Construct();
        var controller = caller.Controller;
        fixture.Common.PublishEarlyController(fixture.Invocation, caller, fixture.RequireConstruction);
        controller.SetHelperFlags(fixture.Invocation, fixture.Factory.Shared.ReadHelperFlag());
        var values = new FalloutPlayerActorValues(records.Records, statusSource:
            FalloutDefaultStatusActorValues.Create(FalloutSourceMainFamily.Fallout3, [new(47, "Paralysis")]));
        values.BindConstantModifiers((_, _) => []); // Authored NPC has no SPLO/inventory/perk rows.
        FalloutReferenceModelDataInvocation? load = new(Guid.NewGuid(), records.Actor, fixture.Scene.Process,
            fixture.Scene.Constructor, fixture.Invocation.CommonProcess, 1, System.Environment.CurrentManagedThreadId,
            FalloutReferenceModelDataIngress.ThreeDLoad);
        var cache = new FalloutPlayerMoverSpeedCacheState(source, records.Records, records.Stack, fixture.Runtime,
            fixture.Scene.Constructor, records.Actor, RequireLoad, () => fixture.Factory.RequireLifetime());
        cache.PopulateFromLoadedPlayer(load, values, records.Records.GetEffective(records.Actor.Base)); load = null;
        var before = controller.Capture();
        FalloutControllerMoverDeltaInvocation? entered = null;
        for (var ordinal = 1; ordinal <= 12; ++ordinal)
        {
            var common = fixture.Common.ReadSceneQueries(records.Actor.Reference, 1);
            var frame = new FalloutPlayerLateFrameInvocation(new(Guid.NewGuid(), ordinal, Guid.NewGuid()), fixture.Scene.Process,
                fixture.Scene.Constructor, Guid.NewGuid(), common, System.Environment.CurrentManagedThreadId, 0x3c888889);
            entered = new(Guid.NewGuid(), frame, fixture.Common.ReadSceneContext(records.Actor.Reference, 1),
                fixture.Runtime.CharacterControllerSourceProcess, default, FalloutActorMoverVector.Default, 0);
            var returned = controller.UpdateMoverDelta(source, entered, cache, RequireDelta); entered = null;
            var current = controller.Capture();
            Require(returned is { Returned: true, Stores.Count: 3, WorldRead: true, World: null, Speed: not null } &&
                returned.Stores.Select(store => store.Field).SequenceEqual(["mover-delta-flags", "mover-delta-independent-word", "mover-delta-speed-scalar"]) &&
                current.MoverDelta!.Calls == ordinal && current.MoverDelta.Sequence == ordinal * 3 &&
                current.Phantom.Identity == before.Phantom.Identity && current.Phantom.NativeId == before.Phantom.NativeId &&
                current.Phantom.Transform == before.Phantom.Transform && current.Proxy.Velocity == before.Proxy.Velocity &&
                current.State == before.State && current.Contact == before.Contact &&
                !PhysicsServer3D.AreaGetSpace(controller.Phantom.NativeResource).IsValid &&
                JsonSerializer.Serialize(current.Stores) == JsonSerializer.Serialize(before.Stores),
                "Actual detached controller delta skipped stores, entered a fabricated world/solver or journaled every frame.");
            FalloutEarlyController.ValidateMoverDelta(current.MoverDelta!);
        }
        var saved = controller.Capture().MoverDelta!;
        Reject(() => FalloutEarlyController.ValidateMoverDelta(saved with { LastCall = saved.LastCall! with { Speed = null } }));
        Reject(() => FalloutEarlyController.ValidateMoverDelta(saved with { Fields = saved.Fields with { ScalarBits = 0x3f800000 } }));
        var changed = saved.LastCall!.Stores.ToArray(); (changed[1], changed[2]) = (changed[2], changed[1]);
        Reject(() => FalloutEarlyController.ValidateMoverDelta(saved with { LastCall = saved.LastCall with { Stores = changed } }));
        cache.Retire();
        GD.Print("OPENNV_NATIVE_MOVER_DELTA_PASS actualArea=true detachedWorldGetter=true sharedFlagsMutation=true " +
            "realLoadedCache=true committedWordScalar=true repeatedFrames=12 boundedHistory=true " +
            "originalActorMovement=UNEXECUTED sourceMain=UNEXECUTED gameplay=UNEXECUTED recording=off");
        void RequireLoad(FalloutReferenceModelDataInvocation current)
        { if (load != current) throw new InvalidOperationException("Native delta fixture changed its authored loaded-Player scope."); fixture.Factory.RequireLifetime(); }
        void RequireDelta(FalloutControllerMoverDeltaInvocation current)
        {
            if (entered != current || fixture.Common.ReadSceneQueries(records.Actor.Reference, 1) != current.Frame.Common)
                throw new InvalidOperationException("Native delta fixture changed its actual entered controller/Common pointer.");
            fixture.Common.RequireControllerMoverDelta(current, controller);
            fixture.Factory.RequireLifetime();
        }
    }
    private sealed class MoverDeltaNativeRecords : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNV-NativeMover-" + Guid.NewGuid().ToString("N"));
        internal readonly FalloutPluginStack Records;
        internal readonly FalloutCombatActorIdentity Actor;
        internal readonly string Stack = new('6', 64);
        internal MoverDeltaNativeRecords(FalloutMainScriptCallerSource main)
        {
            Directory.CreateDirectory(_directory); var acbs = new byte[24]; acbs[8] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(14), 100);
            File.WriteAllBytes(Path.Combine(_directory, "NativeMoverFixture.esm"), Merge(
                Row("TES4", 0, Field("HEDR", new byte[12])),
                Row("NPC_", 7, Field("ACBS", acbs), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
                Setting(0x500, "fAVDDefaultBodyPartCondition", 100), Setting(0x501, "fMoveBaseSpeed", 85),
                Setting(0x502, "fMoveRunMult", 4), Setting(0x503, "fMoveWeightMin", 0), Setting(0x504, "fMoveWeightMax", 150),
                Setting(0x505, "fMoveEncumEffect", .4f), Setting(0x506, "fMoveOneCrippledLegSpeedMult", .85f), Setting(0x507, "fMoveTwoCrippledLegsSpeedMult", .75f)));
            Records = FalloutPluginStack.Load(_directory, ["NativeMoverFixture.esm"]);
            var basis = Records.GetEffective(Records.RuntimeFormKey(7));
            Actor = new(Records.RuntimeFormKey(0x14), basis.FormKey, "ENGINE_PLAYER", 0, main.EngineSha256,
                basis.Signature, basis.Flags, Convert.ToHexString(SHA256.HashData(basis.ReadData())).ToLowerInvariant(), true);
        }
        public void Dispose() { try { Records.Dispose(); } finally { Directory.Delete(_directory, true); } }
        private static byte[] Merge(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
        private static byte[] Setting(uint id, string name, float value) => Row("GMST", id, Field("EDID", Encoding.ASCII.GetBytes(name + "\0")), Field("DATA", BitConverter.GetBytes(value)));
        private static byte[] Field(string signature, byte[] data)
        { var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result; }
        private static byte[] Row(string signature, uint id, params byte[][] fields)
        { var data = Merge(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result; }
    }
}
