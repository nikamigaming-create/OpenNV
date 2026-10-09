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
    private async Task EmptySpeech(string baseRoot, string mod, string root, string savedPath, string[] dependencies,
        bool concurrent = false)
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
            var mom = concurrent ? FalloutDialogueTopic.Find(records, "ACHR", "CG00MomREF") : null;
            var cg00 = FalloutDialogueTopic.Find(records, "QUST", "CG00").FormKey;
            var topic = FalloutDialogueTopic.Read(records, "CG00DadSpeech");
            var momTopic = concurrent ? FalloutDialogueTopic.Read(records, "CG00MomSpeech") : null;
            var sourceHash = SHA256.HashData(topic.Topic.ReadData());
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            var cell = FalloutCellSceneReader.Read(records, world.Get(dad.FormKey).Cell);
            world.LoadCell(cell);
            scene = new Node3D(); AddChild(scene);
            foreach (var reference in new[] { dad, doctor, mom }.OfType<FalloutPluginRecord>())
            {
                // This isolated body/audio fixture owns its active participants.
                world.SetEnabled(reference.FormKey, true);
                world.AdvanceEnableChanges(0, new(1, 1), _ => false);
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
                templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => saved.Character.Female,
                references: world);
            var subtitleRequests = 0; speech.PrepareSubtitle = _ => ++subtitleRequests;
            var events = new List<FalloutFormKey>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ =>
                throw new InvalidOperationException("Empty speech completion invented a source effect.")));
            var results = new List<(FalloutFormKey Actor, FalloutFormKey Info, bool Begin)>();
            var expectedResults = new HashSet<(FalloutFormKey Actor, FalloutFormKey Info, bool Begin)>();
            void ExpectResults(FalloutFormKey actor, FalloutDialogueInfo info)
            {
                if (FalloutScriptResultReceipt.HasProgram(FalloutScriptScope.Dialogue(info.Record, true))) expectedResults.Add((actor, info.Record.FormKey, true));
                if (FalloutScriptResultReceipt.HasProgram(FalloutScriptScope.Dialogue(info.Record, false))) expectedResults.Add((actor, info.Record.FormKey, false));
            }
            speech.ExecuteOwnedResults = (info, actor, begin) =>
            {
                var result = scripts.ExecuteResultOwned(info, actor, begin);
                results.Add((actor, info.Record.FormKey, begin));
                return result;
            };
            var completedInfos = new List<FalloutFormKey>();
            var committedInfos = new Dictionary<FalloutFormKey, int>();
            speech.InfoCompleted += info =>
            {
                if (committedInfos.GetValueOrDefault(info) <= completedInfos.Count(completed => completed == info))
                    throw new InvalidDataException("INFO settled notification preceded its successful source completion.");
                completedInfos.Add(info);
            };
            speech.SayToCompleted += receipt =>
            {
                var result = scripts.DispatchSpeechCompletion(receipt);
                if (result.Error is not null) throw new InvalidDataException(result.Error);
                events.Add(receipt.Speaker);
                if (receipt.Info is { } info) committedInfos[info] = committedInfos.GetValueOrDefault(info) + 1;
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
            ExpectResults(dad.FormKey, topic.Infos.Single(info => info.Record.FormKey.ToString() == active.GetProperty("info").GetString()));
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
            if (concurrent)
            {
                speech.Say(mom!.FormKey, momTopic!.Topic.FormKey, true);
                var overlap = JsonSerializer.SerializeToElement(speech.State);
                var channels = overlap.GetProperty("channels").EnumerateArray().Where(channel => channel.GetProperty("active").GetBoolean()).ToArray();
                var momInfo = channels.Single(channel => channel.GetProperty("speakerReference").GetString() == mom.FormKey.ToString()).GetProperty("info").GetString();
                ExpectResults(mom.FormKey, momTopic.Infos.Single(info => info.Record.FormKey.ToString() == momInfo));
                if (!speech.IsTalking(dad.FormKey) || !speech.IsTalking(mom.FormKey) || speech.IsTalking(doctor.FormKey) ||
                    speech.Error is not null || channels.Length != 2 || subtitleRequests != 2 ||
                    overlap.GetProperty("subtitleCandidates").GetArrayLength() != 2 || speech.Subtitle is not null ||
                    channels.Any(channel => !channel.GetProperty("playing").GetBoolean() || channel.GetProperty("lipSha256").ValueKind != JsonValueKind.String))
                    throw new InvalidDataException("Concurrent owned speech did not retain two actual actor/audio/lip/subtitle channels.");
                foreach (var field in new[] { "info", "speakerReference", "voiceBinding", "audioSha256", "lipSha256" })
                    if (active.GetProperty(field).GetRawText() != overlap.GetProperty(field).GetRawText())
                        throw new InvalidDataException("Mom's actual voice changed Dad's " + field + ".");
                speech.SkipResponse();
                if (JsonSerializer.SerializeToElement(speech.State).GetProperty("channels").EnumerateArray()
                    .Any(channel => channel.GetProperty("active").GetBoolean() && !channel.GetProperty("playing").GetBoolean()))
                    throw new InvalidDataException("Conversation skip interrupted a scripted actor channel.");
                GetTree().Paused = true;
                try
                {
                    var positions = channels.ToDictionary(channel => channel.GetProperty("speakerReference").GetString()!,
                        channel => channel.GetProperty("positionSeconds").GetDouble());
                    await ToSignal(GetTree().CreateTimer(.25, processAlways: true), SceneTreeTimer.SignalName.Timeout);
                    var paused = JsonSerializer.SerializeToElement(speech.State).GetProperty("channels").EnumerateArray()
                        .Where(channel => channel.GetProperty("active").GetBoolean()).ToArray();
                    if (paused.Length != 2 || paused.Any(channel => Math.Abs(channel.GetProperty("positionSeconds").GetDouble() -
                        positions[channel.GetProperty("speakerReference").GetString()!]) > .05) || completedInfos.Count != 0)
                        throw new InvalidDataException("Pausing advanced or completed an actor voice.");
                }
                finally { GetTree().Paused = false; }
            }
            var deadline = Time.GetTicksMsec() + 20000;
            while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var completed = JsonSerializer.SerializeToElement(speech.State);
            var spoken = concurrent ? 2 : 1;
            if (speech.Active || speech.Error is not null ||
                (concurrent ? events.Count != 4 || events.Count(actor => actor == dad.FormKey) != 2 ||
                    events.Count(actor => actor == doctor.FormKey) != 1 || events.Count(actor => actor == mom!.FormKey) != 1 ||
                    completedInfos.Count != 2 :
                    !events.SequenceEqual([dad.FormKey, doctor.FormKey, dad.FormKey])) ||
                results.Count != expectedResults.Count || !expectedResults.SetEquals(results) ||
                completed.GetProperty("completedCommands").GetInt64() != spoken ||
                completed.GetProperty("emptyCompletions").GetProperty("completedTopics").GetInt64() != 2)
                throw new InvalidDataException("Empty speech completion interrupted the actual voice or inflated spoken-command counts.");
            if (!savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(savedPath)) ||
                !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(topic.Topic.ReadData())))
                throw new InvalidDataException("The isolated speech fixture modified owned input or the reached save.");
            if (concurrent)
            {
                speech.Free(); speech = new RuntimeNativeSpeech();
                speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip,
                    quest => quest == cg00 ? stage : quests.Stage(quest),
                    condition => condition is { RunOn: 1, Function: 70, Reference: 0, Argument1: <= 1 } ?
                        (condition.Argument1 == 1) == saved.Character.Female ? 1 : 0 :
                        throw new NotSupportedException($"Isolated speech query {condition.Function}/{condition.RunOn} is unbound."),
                    saidInfos: beforeSaid.ToHashSet(), templates: actor => world.Get(actor).Templates,
                    quests: quests, playerFemale: () => saved.Character.Female, references: world);
                speech.PrepareSubtitle = _ => { };
                var prefix = 0;
                speech.ExecuteOwnedResults = (info, actor, begin) =>
                {
                    _ = scripts.ExecuteResultOwned(info, actor, begin); ++prefix;
                    throw new NotSupportedException("Isolated INFO prefix divergence.");
                };
                AddChild(speech);
                speech.SayTo(dad.FormKey, player, topic.Topic.FormKey, true);
                try { speech.Say(mom!.FormKey, momTopic!.Topic.FormKey, true); }
                catch (NotSupportedException error) when (error.Message == "Isolated INFO prefix divergence.") { }
                try { speech.Say(mom!.FormKey, momTopic!.Topic.FormKey, true); }
                catch (InvalidOperationException error) when (error.Message == "Isolated INFO prefix divergence.") { }
                var failed = JsonSerializer.SerializeToElement(speech.State);
                if (prefix != 1 || speech.Error != "Isolated INFO prefix divergence." || !speech.Active ||
                    failed.GetProperty("completedCommands").GetInt64() != 0 ||
                    failed.GetProperty("channels").EnumerateArray().Any(channel => channel.GetProperty("playing").GetBoolean()))
                    throw new InvalidDataException("A failed INFO prefix replayed, completed, retired or left another actor's audio running.");
            }
            GD.Print((concurrent ? "OPENNV_NATIVE_CONCURRENT_SPEECH_PASS actorChannels=2 actualOverlap=true independentLip=true paused=true skipIsolated=true results=true " :
                "OPENNV_NATIVE_EMPTY_SPEECH_PASS subtitleRetained=true ") +
                $"deferred=true coalesced=true actualActorBinding=true voiceContinued=true saidUnchangedOnEmpty=true spokenCommands={spoken} emptyTopics=2 sourceReadonly=true recording=false " +
                "boundary=isolated-owned-selection-body-audio-fixture coldContinuation=unverified eventFrameParity=unverified campaign=unverified");
        }
        finally { speech?.Free(); scene?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
