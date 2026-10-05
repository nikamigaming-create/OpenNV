using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private void OwnedDisabledSpeech(string baseRoot, string mod, string root, string savedPath,
        string speakerEditorId, string topicEditorId, string[] dependencies)
    {
        RuntimeNativeSpeech? speech = null;
        try
        {
            var savedBytes = File.ReadAllBytes(savedPath);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(savedBytes)!;
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records); world.Restore(saved.References!);
            var speaker = FalloutDialogueTopic.Find(records, "ACHR", speakerEditorId);
            var topic = FalloutDialogueTopic.Find(records, "DIAL", topicEditorId);
            var sourceHash = SHA256.HashData(speaker.ReadData());
            if (world.IsEnabled(speaker.FormKey)) throw new InvalidDataException("Owned speech fixture is not disabled in its retained state.");
            var configuration = RuntimeConfiguration.Load();
            speech = new RuntimeNativeSpeech();
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, _ => 0, references: world);
            speech.SayToCompleted += _ => throw new InvalidDataException("Disabled owned command invented SayToDone.");
            speech.InfoCompleted += _ => throw new InvalidDataException("Disabled owned command invented INFO completion.");
            speech.PrepareSubtitle = _ => throw new InvalidDataException("Disabled owned command invented subtitles.");
            speech.ExecuteResults = (_, _, _) => throw new InvalidDataException("Disabled owned command invented results.");
            AddChild(speech); speech.SetProcess(false);
            var before = JsonSerializer.Serialize(world.Capture());
            speech.SayTo(speaker.FormKey, records.RuntimeFormKey(0x14), topic.FormKey);
            speech.Say(speaker.FormKey, topic.FormKey); speech._Process(1);
            var state = JsonSerializer.SerializeToElement(speech.State);
            if (speech.Error is not null || speech.Active || speech.Subtitle is not null ||
                state.GetProperty("disabledCommands").GetInt64() != 2 || state.GetProperty("channels").GetArrayLength() != 0 ||
                state.GetProperty("completedCommands").GetInt64() != 0 || before != JsonSerializer.Serialize(world.Capture()) ||
                !savedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(savedPath)) ||
                !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(speaker.ReadData())))
                throw new InvalidDataException("Disabled owned command changed state, emitted speech or mutated source/save input.");
            GD.Print($"OPENNV_OWNED_DISABLED_SPEECH_PASS reference={speaker.FormKey} noVoice=true noCompletion=true sourceReadonly=true saveReadonly=true recording=false campaign=unverified");
        }
        finally { speech?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
