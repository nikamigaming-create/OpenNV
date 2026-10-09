using System.Buffers.Binary;
using System.Text;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void PlayerMoves(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var sourceCell = FalloutCellSceneReader.Read(records, Key(0x800));
        _ = FalloutCellSceneReader.Read(records, Key(0x801));
        world.LoadCell(sourceCell);
        var configuration = RuntimeConfiguration.Load();
        var player = new RuntimeNativePlayer();
        try
        {
            player.Configure(configuration, Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
            AddChild(player);
            player.SetPhysicsProcess(false);
            player.SetProcessUnhandledInput(false);
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new InvalidDataException("Movement fixture invented an effect.")));
            var script = records.GetEffective(Key(0x530));
            var program = FalloutGameModeProgram.Read(script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
            void MissingSource(Action action)
            {
                try { action(); }
                catch (NotSupportedException error) when (error.Message == "The campaign player runtime source is absent.") { return; }
                throw new InvalidDataException("A declaration-only movement fixture acquired absent Main/Player source authority.");
            }
            MissingSource(() => executor.ExecuteProgram(records.GetEffective(Key(0x600)), script, program, 0));
            Require(player.GlobalTransform == Transform3D.Identity && world.Get(Key(0x901)).Read(9) == 0 &&
                !world.PlayerMoves.Pending && world.PlayerMoves.SourcePending.Capture().Revision == 0,
                "Rejected source MoveTo moved the body, executed its suffix or mutated the actual pending slot.");
            Require(RuntimeNativePlayerMoves.ApplyNext(world, player, sourceCell.Cell.FormKey,
                (_, _) => throw new InvalidDataException("Missing source authority transferred a real body.")) is null &&
                world.IsResident(Key(0x901)) && !world.IsResident(Key(0x903)),
                "A refused MoveTo fabricated native transfer or changed residency.");
            foreach (var angles in new[] { new Vector3(.2f, -.35f, .7f), new Vector3(-1.1f, .9f, 2.5f),
                new Vector3(.4f, Mathf.Pi / 2, -.8f), new Vector3(.4f, -Mathf.Pi / 2, -.8f) })
            {
                var basis = GamebryoCoordinate.ConvertReferenceEuler(angles, 1);
                Require(GamebryoCoordinate.ConvertReferenceEuler(GamebryoCoordinate.ReferenceEuler(basis), 1).IsEqualApprox(basis),
                    "Native source rotation did not survive coordinate inversion.");
            }
            MissingSource(() => world.QueuePlayerMoveTo(Key(0x530), Key(0x14), 1, 2, 3));
            MissingSource(() => world.QueuePlayerMoveTo(Key(0x530), Key(0x901)));
            Require(player.GlobalTransform == Transform3D.Identity && !world.PlayerMoves.Pending &&
                world.PlayerMoves.SourcePending.Capture().Revision == 0,
                "A repeated or self-target source refusal moved the body or acquired a pending allocation.");
            GD.Print("OPENNV_NATIVE_PLAYER_MOVES_PASS sourceFactoryAbsentRefused=true nativeBodyUnchanged=true suffixStopped=true " +
                "pendingSlotUnchanged=true residencyUnchanged=true rotationConversion=true recording=false fixture=true nativeTransfer=unexecuted parity=unverified");
        }
        finally { player.Free(); }
    }

    private static byte[] PlayerMoveFixture()
    {
        var body = Record("REFR", 0x903, Field("EDID", Encoding.ASCII.GetBytes("ArrivalREF\0")),
            Field("NAME", BitConverter.GetBytes(0x706u)),
            Field("DATA", new[] { 32f, 64f, 96f, .2f, -.35f, .7f }.SelectMany(BitConverter.GetBytes).ToArray()));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x801); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24);
        return Record("SCPT", 0x530, Field("SCHR", new byte[20]), Field("SCRO", BitConverter.GetBytes(0x903u)),
            Field("SCRO", BitConverter.GetBytes(0x901u)), Field("SCRO", BitConverter.GetBytes(0x14u)),
            Field("SCTX", Encoding.ASCII.GetBytes("begin GameMode\nPlayer.MoveTo ArrivalREF 2 -3 4\nAuditRef.frames += 1\nend")))
            .Concat(Record("STAT", 0x706)).Concat(Record("CELL", 0x801, Field("DATA", [1]))).Concat(group).ToArray();
    }
}
