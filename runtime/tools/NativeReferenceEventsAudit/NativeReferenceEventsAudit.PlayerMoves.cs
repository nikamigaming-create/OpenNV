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
        var destinationCell = FalloutCellSceneReader.Read(records, Key(0x801));
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
            executor.ExecuteProgram(records.GetEffective(Key(0x600)), script, program, 0);
            Require(player.GlobalPosition == Vector3.Zero && world.Get(Key(0x901)).Read(9) == 1 && world.PlayerMoves.Pending,
                "Source MoveTo executed immediately or skipped its following statement.");
            var activeCell = sourceCell.Cell.FormKey;
            RuntimeNativePlayerMoves.ApplyNext(world, player, activeCell, (placement, transform) =>
            {
                Require(placement.Cell == destinationCell.Cell.FormKey, "Native MoveTo selected the wrong source CELL.");
                world.LoadCell(destinationCell);
                world.UnloadCell(sourceCell.Cell.FormKey);
                player.Teleport(transform);
                activeCell = placement.Cell;
            });
            var expected = GamebryoCoordinate.ConvertVector(new(34, 61, 100)) * player.UnitsToMeters;
            var expectedBasis = GamebryoCoordinate.ConvertReferenceEuler(new(.2f, -.35f, .7f), 1);
            Require(player.GlobalPosition.IsEqualApprox(expected) && player.GlobalBasis.IsEqualApprox(expectedBasis) &&
                world.IsResident(Key(0x903)) && !world.IsResident(Key(0x901)) && !world.PlayerMoves.Pending,
                "Native MoveTo lost offsets, source rotation, residency or request completion.");
            foreach (var angles in new[] { new Vector3(.2f, -.35f, .7f), new Vector3(-1.1f, .9f, 2.5f),
                new Vector3(.4f, Mathf.Pi / 2, -.8f), new Vector3(.4f, -Mathf.Pi / 2, -.8f) })
            {
                var basis = GamebryoCoordinate.ConvertReferenceEuler(angles, 1);
                Require(GamebryoCoordinate.ConvertReferenceEuler(GamebryoCoordinate.ReferenceEuler(basis), 1).IsEqualApprox(basis),
                    "Native source rotation did not survive coordinate inversion.");
            }
            world.QueuePlayerMoveTo(Key(0x530), Key(0x14), 1, 2, 3);
            RuntimeNativePlayerMoves.ApplyNext(world, player, activeCell,
                (_, _) => throw new InvalidDataException("Same-cell self movement streamed a different world."));
            Require(player.GlobalPosition.IsEqualApprox(expected + GamebryoCoordinate.ConvertVector(new(1, 2, 3)) * player.UnitsToMeters) &&
                player.GlobalBasis.IsEqualApprox(expectedBasis), "Self movement changed its live rotation or ignored offsets.");
            var previous = player.GlobalTransform;
            world.QueuePlayerMoveTo(Key(0x530), Key(0x901));
            try
            {
                RuntimeNativePlayerMoves.ApplyNext(world, player, activeCell,
                    (_, _) => throw new InvalidDataException("Destination loading failed."));
                throw new InvalidOperationException("Failed native movement was accepted.");
            }
            catch (InvalidDataException error) when (error.Message == "Destination loading failed.") { }
            Require(player.GlobalTransform == previous && world.PlayerMoves.Next is null && world.PlayerMoves.Pending &&
                world.PlayerMoves.Error is not null && RuntimeNativePlayerMoves.ApplyNext(world, player, activeCell,
                    (_, _) => throw new InvalidOperationException("Failed movement retried.")) is null,
                "Failed native MoveTo moved the body, discarded evidence or retried.");
            GD.Print("OPENNV_NATIVE_PLAYER_MOVES_PASS source=true queued=true suffix=true offsets=true rotation=true residency=true self=true failureRetained=true recording=false fixture=true parity=unverified");
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
