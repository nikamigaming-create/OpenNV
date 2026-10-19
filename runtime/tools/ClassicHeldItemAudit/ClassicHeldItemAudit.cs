using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

/// <summary>Owned humanoid/held-pose component proof; no campaign items are granted.</summary>
public sealed partial class ClassicHeldItemAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Expected owned FO1 installation and FNV Data directory.");
            using var classic = Fallout1OwnedContentSource.LoadInstall(args[0]);
            using var catalog = new ClassicMapCatalog(classic, "fallout-1");
            using var source = RuntimeLiveContentSource.Open(args[1], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var humans = new ClassicOwnedHumanoids(source);
            var recipe = JsonSerializer.Deserialize<ClassicHumanRecipe>(Godot.FileAccess.GetFileAsString("res://config/classic-humanoids-v1.json"))!;
            var count = 0;
            foreach (var premade in ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _)))
            {
                var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, catalog.Campaign, catalog.ProfileId,
                    premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
                foreach (var held in recipe.HeldItems!)
                {
                    var art = premade.Character.Female ? "art/critters/hfjmpsaa.frm" : "art/critters/hmjmpsaa.frm";
                    var body = humans.Create(art, choice) ?? throw new InvalidDataException("Source humanoid was not admitted.");
                    try
                    {
                        AddChild(body); body.BindHeldItem(source, held);
                        var socket = body.Actor.Skeleton.Node.GetChildren().OfType<BoneAttachment3D>()
                            .Single(row => row.Name == "ClassicHeldItemSocket");
                        if (socket.BoneIdx != body.Actor.Skeleton.BoneIndex(held.Socket) || socket.GetChildCount() != 1)
                            throw new InvalidDataException("Held geometry left its authored hand socket.");
                        if (socket.FindChildren("*", "", true, false).OfType<CollisionObject3D>().Any())
                            throw new InvalidDataException("Dropped-item donor physics entered the held socket.");
                        var skeleton = body.Actor.Skeleton.Node;
                        Transform3D[] Pose() => Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
                        body.Publish(true, .1, Vector3.Forward); var first = Pose();
                        body.Publish(true, .6, Vector3.Right); var second = Pose();
                        if (first.Zip(second).All(pair => pair.First.IsEqualApprox(pair.Second)))
                            throw new InvalidDataException("Held pose suppressed every locomotion channel.");
                        body.Publish(false, 0, Vector3.Right);
                        if (body.GetMeta("classic_pose_moving").AsBool() || body.GetMeta("classic_pose_facing").AsVector3() != Vector3.Right)
                            throw new InvalidDataException("Held actor lost stop/direction publication.");
                        var targets = body.GetMeta("held_source_targets").AsStringArray();
                        var absent = body.GetMeta("held_absent_source_targets").AsStringArray();
                        if (targets.Length == 0 || targets.Intersect(absent, StringComparer.Ordinal).Any())
                            throw new InvalidDataException("A selected model target was classified as absent.");
                        GD.Print(JsonSerializer.Serialize(new
                        {
                            premade.Id,
                            held.Pid,
                            held.Model,
                            held.Pose,
                            held.Grip,
                            targets = targets.Length,
                            absent = absent.Length,
                            socket = held.Socket,
                            movementSamples = 2,
                            fixture = true,
                            gameplay = false,
                            visualAcceptance = false
                        }));
                        count++;
                    }
                    finally { body.Free(); }
                }
            }
            GC.Collect(); GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"OPENNV_CLASSIC_HELD_ITEM_AUDIT_PASS components={count} sourceTargets=true socket=true motion=true gameplay=false");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
