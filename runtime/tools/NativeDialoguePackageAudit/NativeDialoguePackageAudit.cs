using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeDialoguePackageAudit : Node3D
{
    public override void _Ready()
    {
        RuntimeNativeNpc? actor = null;
        RuntimeNativePlayer? player = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 1) throw new ArgumentException("Dialogue package audit needs one owned root.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
            var source = cell.References.Single(reference => reference.EditorId == "DocMitchellREF");
            Transform3D Placement(FalloutPlacedReference reference) => new(new Basis(Vector3.Up, -reference.RotationRadians[2]),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var quests = new FalloutQuestState(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", "VCG01").FormKey;
            quests.EnterStage(quest, 80);
            actor = RuntimeNativeNpc.Create(records, content, source, units, (_, _, _, _) => new StandardMaterial3D());
            AddChild(actor); actor.ConfigureAi(records, quests, cell, Placement);
            player = new(); AddChild(player);
            var package = FalloutDialogueTopic.Find(records, "PACK", "VCG01DocMitchellFarewellDialogueStart");
            var dialogue = FalloutDialoguePackage.Read(package);
            var wait = package.ReadSubrecords().Single(field => field.Signature == "PLDT").Data;
            var waitRef = package.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(wait.Span[4..]));
            var destination = Placement(cell.References.Single(reference => reference.FormKey == waitRef));
            player.Configure(configuration, destination); player.SetPhysicsProcess(false);
            Action? complete = null; var requests = 0;
            actor.BeginPackageDialogue = (request, completed) =>
            {
                if (request != dialogue || actor.Traveling || actor.SittingState != 0 ||
                    actor.GlobalPosition.DistanceTo(player.GlobalPosition) > request.ActivationDistance * units)
                    throw new InvalidOperationException("Dialogue package did not reach its source location and target range.");
                requests++; complete = completed;
            };
            quests.EnterStage(quest, 115); actor.EvaluatePackages(false);
            if (actor.SittingState != 4) throw new InvalidOperationException("Dialogue package skipped the chair exit.");
            for (var frame = 0; frame < 7200 && requests == 0; frame++)
            {
                actor._Process(1d / 60);
                if (actor.AiError is not null || actor.AnimationError is not null) throw new InvalidOperationException(actor.AiError ?? actor.AnimationError);
            }
            bool Done() => JsonSerializer.SerializeToElement(actor.AiState).GetProperty("packageEvents").GetProperty("Done").GetBoolean();
            if (requests != 1 || complete is null || Done()) throw new InvalidOperationException("Dialogue package completed before its conversation.");
            complete();
            for (var frame = 0; frame < 120; frame++) actor._Process(1d / 60);
            if (!Done() || requests != 1) throw new InvalidOperationException("Dialogue package lost or repeated completion.");
            GD.Print("OPENNV_DIALOGUE_PACKAGE_AUDIT_PASS furnitureExit=true ownedNavm=true targetRange=true requestOnce=true completionAfterConversation=true pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { actor?.Free(); player?.Free(); }
    }
}
