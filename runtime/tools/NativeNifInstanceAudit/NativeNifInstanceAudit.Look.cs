using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

public partial class NativeNifInstanceAudit
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private void ExerciseHeadTracking()
    {
        var skeleton = new Skeleton3D();
        AddChild(skeleton);
        try
        {
            skeleton.AddBone("Parent"); skeleton.AddBone("Head"); skeleton.SetBoneParent(1, 0);
            skeleton.SetBoneRest(0, Transform3D.Identity); skeleton.SetBoneRest(1, new(Basis.Identity, Vector3.Up));
            skeleton.ResetBonePoses();
            var settings = new FalloutLookSettings(1, 1000, 4, 2, 0.2f);
            var part = new FalloutBodyPartLook(new("Synthetic.esm", 15), 2, "Head", 30);
            var pose = new NativeHeadTrackingPose(skeleton, 1, part, settings, 0.01f);
            var target = new Vector3(4, 1, -1);
            var previous = Quaternion.Identity;
            for (var frame = 0; frame < 30; frame++)
            {
                pose.RestoreAuthoredPose();
                Require(skeleton.GetBonePoseRotation(1).AngleTo(Quaternion.Identity) < 0.001f, "Procedural head pose contaminated source animation.");
                pose.Publish(target, 0);
                var current = skeleton.GetBonePoseRotation(1);
                Require(previous.AngleTo(current) <= Mathf.DegToRad(4.01f), "Head movement exceeded source publication limit.");
                previous = current;
            }
            var direction = skeleton.GetBoneGlobalPose(1).Basis * Vector3.Forward;
            Require(MathF.Abs(Vector3.Forward.AngleTo(direction) - Mathf.DegToRad(30)) < 0.001f && direction.X > 0,
                "Source head cone did not clamp toward the target.");
            for (var frame = 0; frame < 30; frame++) { pose.RestoreAuthoredPose(); pose.Publish(target, 90); }
            Require(skeleton.GetBonePoseRotation(1).AngleTo(Quaternion.Identity) < 0.001f, "Authored float override did not release the procedural pose.");
            var authored = new Quaternion(Vector3.Up, -0.13f);
            pose.RestoreAuthoredPose(); skeleton.SetBonePoseRotation(1, authored);
            pose.Publish(new Vector3(0, 1, -100), 0);
            Require(skeleton.GetBonePoseRotation(1).AngleTo(authored) < 0.001f, "Out-of-range target overwrote the source pose.");
            GD.Print("OPENNV_HEAD_POSE_CONTRACT_PASS cone=true publicationLimit=true authoredRestore=true override=true distanceGate=true");
        }
        finally { skeleton.Free(); }
    }

    private void ExerciseOwnedHeadTracking(string dataRoot, string actorHex, string questEditorId)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var reference = records.GetEffective(records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16)));
        var actorBase = FalloutDialogueTopic.RequiredForm(reference, "NAME");
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, actorBase, reference.FormKey);
        var actor = RuntimeNativeNpc.Create(appearance, source, 0.0142875f,
            (npc, part, nif, geometry) => NativeNpcMaterial.Resolve(npc, part, nif, geometry, records, Colors.Black));
        AddChild(actor);
        try
        {
            var part = FalloutBodyPartLook.Read(records.GetEffective(records.RuntimeFormKey(0x1d)))!;
            var target = Vector3.Zero;
            actor.ConfigureHeadTracking(records, source, _ => target);
            target = actor.HeadTargetPoint!.Value + new Vector3(2, 0, -2);
            actor.ApplyHeadTrackingCommand(records.RuntimeFormKey(0x14));
            var bone = actor.Skeleton.BoneIndex(part.TargetNode);
            var original = actor.Skeleton.Node.GetBonePoseRotation(bone);
            var pose = new NativeHeadTrackingPose(actor.Skeleton.Node, bone, part,
                FalloutLookSettings.Read(FalloutInstallationSettings.Read(source)), actor.Skeleton.UnitsToMetres);
            for (var frame = 0; frame < 40; frame++) { pose.RestoreAuthoredPose(); pose.Publish(target, 0); }
            Require(actor.Skeleton.Node.GetBonePoseRotation(bone).AngleTo(original) > 0.1f,
                "Owned head bone did not physically turn.");
            var quests = FalloutOpeningPlayerControlResolver.Resolve(records, questEditorId.Split(',')).Quests;
            var stageCommands = quests.Values.SelectMany(stages => stages.Values)
                .SelectMany(stage => FalloutHeadTrackingPrograms.Stage(records, stage)).ToArray();
            var activeCell = FalloutCellSceneReader.ParentCell(reference)!.Value;
            foreach (var command in stageCommands)
                Require(FalloutHeadTrackingPrograms.RequiresProcess(records.GetEffective(command.Actor), activeCell,
                    command.Actor == reference.FormKey) == (command.Actor == reference.FormKey), "Loaded/dormant command lifetime differs.");
            try
            {
                _ = FalloutHeadTrackingPrograms.RequiresProcess(reference, activeCell, false);
                throw new InvalidOperationException("Missing loaded actor was treated as dormant.");
            }
            catch (NotSupportedException) { }
            var packageCommands = new List<FalloutBoundLookCommand>();
            var owner = FalloutAiPackages.TemplateOwner(records, records.GetEffective(actorBase), 32);
            foreach (var field in owner.ReadSubrecords().Where(field => field.Signature == "PKID"))
            {
                var package = FalloutScriptPackage.Read(records.GetEffective(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))));
                foreach (var program in package.EventPrograms.Values)
                {
                    var commands = FalloutHeadTrackingPrograms.Bind(program.Source,
                        new(records, program.Package, program.Package, program.Fields), reference.FormKey);
                    packageCommands.AddRange(commands);
                    var cursor = 0;
                    program.ExecuteScript(_ => actor.ApplyBoundHeadTrackingCommand(commands.Single(command => command.Line == cursor++)));
                }
            }
            if (stageCommands.Length == 0 || packageCommands.Count == 0)
                throw new InvalidDataException("Selected owned scripts exercised no Look command.");
            // A source spelling alone must never bind the player/global EDID.
            try
            {
                _ = FalloutHeadTrackingPrograms.Bind("Look player", new(records, owner, owner, []), reference.FormKey);
                throw new InvalidOperationException("Missing compiled player reference was admitted.");
            }
            catch (NotSupportedException) { }
            pose.RestoreAuthoredPose();
            for (var frame = 0; frame < 40; frame++) actor._Process(1.0 / 60);
            Require(actor.AnimationError is null, "Actor head owner failed without a KF overlay.");
            GD.Print("OPENNV_OWNED_HEAD_TRACKING " + JsonSerializer.Serialize(new
            {
                head = actor.HeadTrackingState,
                pose = pose.State,
                stageCommands,
                packageCommands,
                scope = "owned-source-binding-and-physical-bone-component-not-gameplay-parity",
            }));
            GD.Print($"OPENNV_OWNED_HEAD_TRACKING_PASS stages={stageCommands.Length} packageCommands={packageCommands.Count}");
        }
        finally { actor.Free(); }
    }
}
