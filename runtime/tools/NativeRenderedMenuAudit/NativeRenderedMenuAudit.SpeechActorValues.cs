using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task SpeechActorValues(string baseRoot, string mod, string root, string speakerId, string topicId,
        string questId, short stage, string[] dependencies)
    {
        RuntimeNativeNpc? body = null;
        RuntimeNativeSpeech? speech = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var actor = FalloutDialogueTopic.Find(records, "ACHR", speakerId);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var hash = SHA256.HashData(topic.Topic.ReadData());
            var quests = new FalloutQuestState(records);
            quests.EnterStage(FalloutDialogueTopic.Find(records, "QUST", questId).FormKey, stage);
            var cell = FalloutCellSceneReader.Read(records, world.Get(actor.FormKey).Cell);
            world.LoadCell(cell);
            var placed = cell.References.Single(item => item.FormKey == actor.FormKey);
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            var templates = world.InitializeActorTemplates(actor.FormKey, 1);
            body = RuntimeNativeNpc.Create(records, content, placed, units,
                (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
                world.EquippedArmor(actor.FormKey, 1), templates);
            body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            if (body.AnimationError is not null || body.AppearanceError is not null)
                throw new InvalidDataException(body.AnimationError ?? body.AppearanceError);
            // This isolated fixture selects the authored user-value responses;
            // prior SayOnce lines are retained as already said, not executed.
            var said = topic.Infos.Where(info => info.Conditions.Select(data => FalloutCondition.Read(info.Record, data))
                .All(condition => condition.Function != 14)).Select(info => info.Record.FormKey).ToHashSet();
            var queries = new List<(FalloutFormKey Actor, int Value)>();
            speech = new();
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest),
                saidInfos: said, templates: reference => world.Get(reference).Templates, quests: quests,
                playerFemale: () => false, actorValue: (reference, value) =>
                {
                    queries.Add((reference, value));
                    return world.ActorValue(reference, FalloutActorValue.UserSlot(value));
                }, dialogueRandom: _ => 0);
            speech.PrepareSubtitle = _ => { };
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("The isolated user-value response invented an unrelated stage effect.")));
            speech.ExecuteResults = scripts.ExecuteResult;
            var completions = 0;
            speech.SayToCompleted += (reference, topics) =>
            {
                if (reference != actor.FormKey || !topics.SetEquals([topic.Topic.FormKey]))
                    throw new InvalidDataException("Scripted completion crossed actor or topic ownership.");
                ++completions;
            };
            AddChild(speech);
            var completed = new List<string>();
            for (var iteration = 0; iteration < 2; ++iteration)
            {
                speech.SayTo(actor.FormKey, records.RuntimeFormKey(0x14), topic.Topic.FormKey, true);
                var active = JsonSerializer.SerializeToElement(speech.State);
                var info = topic.Infos.Single(info => info.Record.FormKey.ToString() == active.GetProperty("info").GetString());
                if (!speech.IsTalking(actor.FormKey) || speech.Error is not null ||
                    active.GetProperty("audioSha256").ValueKind != JsonValueKind.String ||
                    info.Conditions.Select(data => FalloutCondition.Read(info.Record, data)).All(condition => condition.Function != 14))
                    throw new InvalidDataException("Scripted actor-value selection did not start its owned actor/audio response.");
                var deadline = Time.GetTicksMsec() + 20000;
                while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (speech.Active || speech.Error is not null || world.ActorValue(actor.FormKey, "variable05") != iteration + 1)
                    throw new InvalidDataException(speech.Error ?? "Owned response did not complete its source user-value write.");
                if (body.AnimationError is not null || body.AppearanceError is not null)
                    throw new InvalidDataException(body.AnimationError ?? body.AppearanceError);
                completed.Add(info.Record.FormKey.ToString());
            }
            if (completed.Distinct().Count() != 2 || completions != 2 || queries.Count == 0 || queries.Any(query => query.Actor != actor.FormKey || query.Value != 66))
                throw new InvalidDataException("Scripted speech lost the actor/slot owner or repeated a now-ineligible response.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            if (cold.ActorValue(actor.FormKey, "variable05") != 2 || !SHA256.HashData(topic.Topic.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Source user-value state did not restore cold or owned input changed.");
            GD.Print("OPENNV_NATIVE_SPEECH_ACTOR_VALUES_PASS actor=" + actor.FormKey +
                " responses=2 sourceResults=true numericSlot=true nativeAudio=true coldActorValue=true sourceReadonly=true recording=false campaign=unverified");
        }
        finally { speech?.Free(); body?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
