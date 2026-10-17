using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    // Real owned bodies, INFOs and audio in a disposable speech-owner fixture.
    // The failure variant stops the terminal callback after one consumed prefix.
    private async Task NpcSpeechSettled(string game, string mod, string root, string speakerId,
        string listenerId, string topicId, string questId, short stage, string[] dependencies, bool failCompletion)
    {
        Node3D? fixture = null;
        try
        {
            var selection = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                selection.ContentRoots.Skip(1).ToArray(), selection.ActivePlugins, selection.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var speaker = FalloutDialogueTopic.Find(records, "ACHR", speakerId);
            var listener = FalloutDialogueTopic.Find(records, "ACHR", listenerId);
            if (speaker.FormKey == listener.FormKey || FalloutCellSceneReader.ParentCell(speaker) != FalloutCellSceneReader.ParentCell(listener))
                throw new InvalidDataException("NPC speech fixture requires two distinct actual references in one source cell.");
            var topic = FalloutDialogueTopic.Read(records, topicId);
            if (FalloutDialogueTopic.Type(records, topic.Topic.FormKey) != 1)
                throw new InvalidDataException("NPC speech fixture requires a source conversation topic.");
            var sourceHash = SHA256.HashData(topic.Topic.ReadData());
            var quests = new FalloutQuestState(records); var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            quests.SetRunning(quest, true); quests.EnterStage(quest, stage);
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(speaker)!.Value);
            world.LoadCell(cell); var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            fixture = new Node3D(); AddChild(fixture);
            foreach (var reference in new[] { speaker, listener })
            {
                var enableRoot = reference.FormKey; var rootEnabled = true; var visited = new HashSet<FalloutFormKey>();
                while (world.Get(enableRoot).EnableParent is { } parent)
                {
                    if (!visited.Add(enableRoot) || parent.Reference == records.RuntimeFormKey(0x14))
                        throw new InvalidDataException("NPC speech fixture has an unsupported enable-parent chain.");
                    rootEnabled ^= parent.Opposite; enableRoot = parent.Reference;
                }
                world.SetEnabled(enableRoot, rootEnabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
                if (!world.IsEnabled(reference.FormKey)) throw new InvalidDataException("NPC fixture source enable chain did not settle.");
                var placed = cell.References.Single(value => value.FormKey == reference.FormKey);
                var templates = world.InitializeActorTemplates(reference.FormKey, 1);
                var body = RuntimeNativeNpc.Create(records, content, placed, units,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
                    world.EquippedArmor(reference.FormKey, 1), templates);
                body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                    GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
                fixture.AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
                if (body.AnimationError is not null || body.AppearanceError is not null)
                    throw new InvalidDataException(body.AnimationError ?? body.AppearanceError);
            }
            var speech = new RuntimeNativeSpeech(); var divergences = new List<string>();
            speech.ReportDivergence = divergences.Add;
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, key => quests.Stage(key),
                condition => quests.Evaluate(condition), templates: reference => world.Retained(reference).Templates,
                quests: quests, references: world, playerFemale: () => false);
            speech.PrepareSubtitle = _ => { };
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new NotSupportedException("Selected NPC fixture reached an unbound source effect.")));
            speech.ExecuteResults = scripts.ExecuteResult;
            speech.SayToCompleted += _ => throw new InvalidDataException("Package NPC speech invented scripted SayToDone.");
            fixture.AddChild(speech);
            var terminalPrefix = 0; var settled = 0; var linkedNotifications = 0;
            speech.InfoCompleted += _ =>
            {
                if (speech.IsNpcDialogueActive(speaker.FormKey) || speech.IsNpcDialogueActive(listener.FormKey))
                {
                    if (!speech.Active || terminalPrefix != 0) throw new InvalidDataException("Linked speech was announced settled before its next source turn.");
                    ++linkedNotifications;
                }
                else
                {
                    if (speech.Active || speech.Error is not null || terminalPrefix != 1 || failCompletion)
                        throw new InvalidDataException("NPC settled notification preceded participant retirement and successful terminal completion.");
                    ++settled;
                }
            };
            speech.StartNpcConversation(speaker.FormKey, listener.FormKey, topic.Topic.FormKey, () =>
            {
                if (speech.IsNpcDialogueActive(speaker.FormKey) || speech.IsNpcDialogueActive(listener.FormKey))
                    throw new InvalidDataException("NPC source terminal callback retained active participants.");
                ++terminalPrefix;
                if (failCompletion) throw new NotSupportedException("NPC terminal completion fixture stopped after its prefix.");
            });
            if (!speech.Active || !speech.IsNpcDialogueActive(speaker.FormKey) || terminalPrefix != 0 || settled != 0)
                throw new InvalidDataException("NPC source audio/exchange did not begin its actual continuation.");
            var deadline = Time.GetTicksMsec() + 60000;
            while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (failCompletion)
            {
                if (speech.Error != "NPC terminal completion fixture stopped after its prefix." || terminalPrefix != 1 || settled != 0 || !speech.Active)
                    throw new InvalidDataException("Failed NPC terminal prefix replayed or announced settled speech.");
            }
            else if (speech.Active || speech.Error is not null || terminalPrefix != 1 || settled != 1 || divergences.Count != 0)
                throw new InvalidDataException(speech.Error ?? "NPC speech missed or repeated its terminal settled notification.");
            speech._Process(0); speech._Process(0);
            if (terminalPrefix != 1 || settled != (failCompletion ? 0 : 1) ||
                !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(topic.Topic.ReadData())))
                throw new InvalidDataException("Settled/failed NPC speech replayed or changed owned source bytes.");
            GD.Print($"OPENNV_NATIVE_NPC_SPEECH_SETTLED_PASS sourceBodies=true sourceAudio=true linkedNotifications={linkedNotifications} " +
                $"terminalPrefixOnce=true notificationAfterRetirement=true failureFixture={failCompletion} noReplay=true sourceUnchanged=true recording=false campaignAndParity=unverified");
        }
        finally { fixture?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
