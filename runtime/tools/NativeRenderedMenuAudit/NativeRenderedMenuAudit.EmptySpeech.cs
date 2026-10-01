using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    // Isolated source/body/audio fixture. The reached save supplies selection
    // history only; this does not resume, repair or advance a campaign save.
    private async Task EmptySpeech(string baseRoot, string mod, string root, string savedPath, string[] dependencies)
    {
        Node3D? scene = null;
        RuntimeNativeSpeech? speech = null;
        try
        {
            var savedBytes = File.ReadAllBytes(savedPath);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(savedBytes) ?? throw new InvalidDataException("Reached speech history is absent.");
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var dad = FalloutDialogueTopic.Find(records, "ACHR", "CG00DadREF");
            var doctor = FalloutDialogueTopic.Find(records, "ACHR", "CG00DoctorLiREF");
            var cg00 = FalloutDialogueTopic.Find(records, "QUST", "CG00").FormKey;
            var topic = FalloutDialogueTopic.Read(records, "CG00DadSpeech");
            var sourceHash = SHA256.HashData(topic.Topic.ReadData());
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            var cell = FalloutCellSceneReader.Read(records, world.Get(dad.FormKey).Cell);
            world.LoadCell(cell);
            scene = new Node3D(); AddChild(scene);
            foreach (var reference in new[] { dad, doctor })
            {
                var placed = cell.References.Single(item => item.FormKey == reference.FormKey);
                var templates = world.InitializeActorTemplates(reference.FormKey, 1);
                var body = RuntimeNativeNpc.Create(records, source, placed, units,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
                    world.EquippedArmor(reference.FormKey, 1), templates);
                body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                    GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
                scene.AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            }
            var stage = 70;
            var said = saved.Scripts!.SaidInfos!.ToHashSet(); var beforeSaid = said.ToHashSet();
            speech = new RuntimeNativeSpeech();
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip,
                quest => quest == cg00 ? stage : quests.Stage(quest), condition =>
                {
                    if (condition is { RunOn: 1, Function: 70, Reference: 0, Argument1: <= 1 })
                        return (condition.Argument1 == 1) == saved.Character.Female ? 1 : 0;
                    throw new NotSupportedException($"Isolated speech query {condition.Function}/{condition.RunOn} is unbound.");
                }, saidInfos: said,
                templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => saved.Character.Female);
            var subtitleRequests = 0; speech.PrepareSubtitle = _ => ++subtitleRequests;
            var events = new List<FalloutFormKey>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ =>
                throw new InvalidOperationException("Empty speech completion invented a source effect.")));
            speech.SayToCompleted += (actor, topics) =>
            {
                var result = scripts.DispatchFrame(actor, [new("SayToDone", Topics: topics)], 0).Single();
                if (result.Error is not null) throw new InvalidDataException(result.Error);
                events.Add(actor);
            };
            AddChild(speech);
            var player = records.RuntimeFormKey(0x14);
            speech.SayTo(dad.FormKey, player, topic.Topic.FormKey, true);
            speech.SayTo(dad.FormKey, player, topic.Topic.FormKey, true);
            if (!speech.Active || speech.IsTalking(dad.FormKey) || events.Count != 0 || speech.Subtitle is not null ||
                speech.Error is not null || subtitleRequests != 0 || !said.SetEquals(beforeSaid))
                throw new InvalidDataException("Empty owned selection invented a voice, subtitle, result, immediate event or SayOnce entry.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (speech.Active || speech.Error is not null || !events.SequenceEqual([dad.FormKey]))
                throw new InvalidDataException("Empty owned speech did not defer and coalesce typed completion.");
            stage = 80;
            speech.SayTo(dad.FormKey, player, topic.Topic.FormKey, true);
            var active = JsonSerializer.SerializeToElement(speech.State);
            var subtitle = speech.Subtitle;
            if (!speech.IsTalking(dad.FormKey) || speech.Error is not null || subtitle is null || subtitleRequests != 1)
                throw new InvalidDataException("The subsequent eligible owned line did not start its actual actor/voice binding.");
            speech.SayTo(doctor.FormKey, player, topic.Topic.FormKey);
            var after = JsonSerializer.SerializeToElement(speech.State);
            foreach (var field in new[] { "info", "speakerReference", "voiceBinding", "audioSha256", "lipSha256" })
                if (active.GetProperty(field).GetRawText() != after.GetProperty(field).GetRawText())
                    throw new InvalidDataException("Another actor's empty selection changed the active " + field + ".");
            if (speech.Subtitle != subtitle || !speech.IsTalking(dad.FormKey) || speech.IsTalking(doctor.FormKey) ||
                speech.Error is not null || events.Count != 1 || subtitleRequests != 1)
                throw new InvalidDataException("Another actor's empty speech disturbed the active line or completed inline.");
            var deadline = Time.GetTicksMsec() + 15000;
            while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var completed = JsonSerializer.SerializeToElement(speech.State);
            if (speech.Active || speech.Error is not null || !events.SequenceEqual([dad.FormKey, doctor.FormKey, dad.FormKey]) ||
                completed.GetProperty("completedCommands").GetInt64() != 1 ||
                completed.GetProperty("emptyCompletions").GetProperty("completedTopics").GetInt64() != 2)
                throw new InvalidDataException("Empty speech completion interrupted the actual voice or inflated spoken-command counts.");
            if (!savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(savedPath)) ||
                !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(topic.Topic.ReadData())))
                throw new InvalidDataException("The isolated speech fixture modified owned input or the reached save.");
            GD.Print("OPENNV_NATIVE_EMPTY_SPEECH_PASS deferred=true coalesced=true actualActorBinding=true voiceContinued=true " +
                "subtitleRetained=true saidUnchangedOnEmpty=true spokenCommands=1 emptyTopics=2 sourceReadonly=true recording=false " +
                "boundary=isolated-owned-selection-body-audio-fixture coldContinuation=unverified eventFrameParity=unverified campaign=unverified");
        }
        finally { speech?.Free(); scene?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
