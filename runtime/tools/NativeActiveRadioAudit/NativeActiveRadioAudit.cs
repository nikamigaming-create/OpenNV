using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Tools;

// Selected native audio component fixture. The immutable checkpoint supplies
// source query state; it is never rewritten or presented as a campaign save.
public partial class NativeActiveRadioAudit : Node
{
    private readonly List<RuntimeNativeSpeech> _speeches = [];
    private readonly List<FalloutReferenceWorld> _worlds = [];
    private readonly List<(FalloutFormKey Info, bool Begin)> _results = [];
    private readonly List<FalloutFormKey> _notifications = [];
    private RuntimeConfiguration _configuration = null!;
    private FalloutNativeCampaignState _saved = null!;

    public override async void _Ready()
    {
        FalloutPluginStack? records = null;
        var previousPause = GetTree().Paused;
        var exitCode = 0;
        try
        {
            ProcessMode = ProcessModeEnum.Always;
            var assembly = typeof(RuntimeNativeSpeech).Assembly;
            GD.Print($"OPENNV_NATIVE_RADIO_ASSEMBLY path={assembly.Location} mvid={assembly.ManifestModule.ModuleVersionId}");
            var args = OS.GetCmdlineUserArgs();
            if (args is not [_, _, _, _, _, _])
                throw new ArgumentException("Expected owned Data root, campaign, immutable checkpoint, station plugin, station hex ID and topic editor ID or form key.");
            var bytes = File.ReadAllBytes(args[2]);
            _saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(bytes) ?? throw new InvalidDataException("Owned checkpoint is empty.");
            _configuration = RuntimeConfiguration.Load();
            RuntimeLiveContentSource.Configure(args[0], args[1]);
            records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var station = new FalloutFormKey(args[3], uint.Parse(args[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var topicParts = args[5].Split(':');
            var topic = topicParts.Length == 2
                ? new FalloutFormKey(topicParts[0], uint.Parse(topicParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                : FalloutDialogueTopic.Find(records, "DIAL", args[5]).FormKey;
            if (records.GetEffective(topic).Signature != "DIAL")
                throw new InvalidDataException("Owned radio audit topic has a different source signature.");
            var world = CreateWorld(records, _saved.References!, _saved.Scripts!.Values);
            var quests = new FalloutQuestState(records); quests.Restore(_saved.Quests!);
            var globals = FalloutGlobalState.Read(records);
            if (_saved.Globals is { } globalState) globals.Restore(globalState);
            var said = (_saved.Scripts.SaidInfos ?? []).ToHashSet();
            // Scripted mode is explicit fixture setup, independently of the
            // continuous scheduler. The original station still owns the voice.
            world.SetBroadcastState(station, 0);
            await CheckInitialDisabled(records, station, topic);
            var speech = CreateSpeech(records, world, quests, globals, said);
            GetTree().Paused = true;
            speech.StartRadioConversation(station, topic);
            Require(speech.CanCaptureState && speech.IsTalking(station) && speech.Error is null,
                "Selected owned radio did not enter a capturable source response.");
            TrackNativeRadioAudio(speech, station);
            GetTree().Paused = false;
            await WaitForNativeRadioProgress(speech, station);
            GetTree().Paused = true;
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            var active = Copy(speech.CaptureState());
            var voice = active.ActiveRadio!.Single();
            var resultPrefix = _results.Count;
            var notificationPrefix = _notifications.Count;
            var randomPrefix = JsonSerializer.Serialize(world.ScriptValues.Capture());
            GD.Print("OPENNV_NATIVE_RADIO_MIXER_CLOCK " + JsonSerializer.Serialize(new
            {
                voice.Samples,
                treePaused = GetTree().Paused,
                speech.ProcessMode,
                canProcess = speech.CanProcess(),
                nativePlayerPaused = Player(speech, station).StreamPaused,
                audioDriver = AudioServer.GetDriverName(),
                speech.Error
            }));
            Require(voice.Samples.Position > 0 && voice.Samples.Position < voice.Samples.Frames,
                "Owned native mixer did not advance inside the selected finite radio response.");
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Require(speech.CaptureState().ActiveRadio!.Single().Samples == voice.Samples,
                "Paused source radio advanced behind the menu.");
            CheckOpaqueRefusals(speech, station);
            Reject(() => speech.CaptureFinishedState(), "Active radio was accepted as ended speech history.");

            var coldReferences = _saved.References!.Select(reference => reference.Reference == station
                ? world.Retained(station).Capture() : reference).ToArray();
            var coldWorld = CreateWorld(records, coldReferences, Copy(world.ScriptValues.Capture()));
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture().ToArray()));
            var coldGlobals = FalloutGlobalState.Read(records); coldGlobals.Restore(Copy(globals.Capture()));
            var coldSaid = said.ToHashSet();
            var cold = CreateSpeech(records, coldWorld, coldQuests, coldGlobals, coldSaid);
            cold.RestoreState(active);
            TrackNativeRadioAudio(cold, station);
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Require(cold.CaptureState().ActiveRadio!.Single() == voice && _results.Count == resultPrefix &&
                _notifications.Count == notificationPrefix && coldSaid.SetEquals(said) &&
                JsonSerializer.Serialize(coldWorld.ScriptValues.Capture()) == randomPrefix,
                "Cold radio changed its source cursor/sample clock or replayed results, selection, notifications or RNG.");
            CheckNativeSuffix(speech, cold, station);
            CheckRejectedRestoration(records, coldWorld, coldQuests, coldGlobals, coldSaid, active);
            CheckMixerFailure(records, coldWorld, coldQuests, coldGlobals, coldSaid, active, station);
            DrainNativeResponse(cold, station);
            var finishedAudio = Copy(cold.CaptureState());
            var finishedSamples = finishedAudio.ActiveRadio!.Single().Samples;
            Require(!finishedSamples.Playing && finishedSamples.Position == voice.Samples.Frames,
                "The actual finite native sample suffix did not finish.");
            cold.Free(); _speeches.Remove(cold);
            var ending = CreateSpeech(records, coldWorld, coldQuests, coldGlobals, coldSaid);
            ending.RestoreState(finishedAudio);
            TrackNativeRadioAudio(ending, station);
            Require(_results.Count == resultPrefix && _notifications.Count == notificationPrefix,
                "Completed-audio restoration replayed its committed result prefix.");
            GetTree().Paused = false;
            ending._Process(0);
            GetTree().Paused = true;
            Require(ending.Error is null && _notifications.Count == notificationPrefix + 1 &&
                _notifications[^1] == voice.Conversation.Info,
                "Cold finite radio did not deliver its selected source completion exactly once.");
            var after = ending.CaptureState();
            var completedLines = after.ActiveRadio?.SingleOrDefault()?.Conversation.Identity.CompletedLines ??
                after.FinishedRadio!.Single(radio => radio.Station.Reference == station).CompletedLines;
            Require(completedLines == voice.Conversation.Identity.CompletedLines + 1,
                "Cold completion lost the retained source link/line generation.");
            GetTree().Paused = false; ending._Process(0); GetTree().Paused = true;
            Require(_notifications.Count == notificationPrefix + 1, "Repeated processing redelivered a finished source line.");
            Require(SHA256.HashData(File.ReadAllBytes(args[2])).SequenceEqual(SHA256.HashData(bytes)),
                "The selected native component check modified its immutable campaign checkpoint.");
            GD.Print($"OPENNV_NATIVE_ACTIVE_RADIO_PASS station={station} info={voice.Conversation.Info} " +
                "ownedDecoder=true sourceCursor=true nativeSuffix=true pause=true initialDisabled=true noPrefixReplay=true onceOnlyEnd=true " +
                "mediaDriftRefused=true opaqueCallbacksRefused=true mixerFaultRetained=true immutableInput=true framesRecorded=0 parity=unmeasured");
        }
        catch (Exception error) { GD.PushError(error.ToString()); exitCode = 1; }
        finally
        {
            GetTree().Paused = previousPause;
            foreach (var speech in _speeches) if (IsInstanceValid(speech)) speech.Free();
            foreach (var world in _worlds) world.Dispose();
            records?.Dispose(); RuntimeLiveContentSource.Clear();
            try { await RetireNativeRadioAudio(); }
            catch (Exception error) { GD.PushError(error.ToString()); exitCode = 1; }
        }
        GetTree().Quit(exitCode);
    }

    private FalloutReferenceWorld CreateWorld(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> references,
        FalloutScriptValueStoreSnapshot? values)
    {
        var world = new FalloutReferenceWorld(records); _worlds.Add(world);
        if (values is not null) world.ScriptValues.Restore(Copy(values));
        world.Restore(Copy(references.ToArray()));
        return world;
    }

    private RuntimeNativeSpeech CreateSpeech(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, FalloutGlobalState globals, HashSet<FalloutFormKey> said)
    {
        var speech = new RuntimeNativeSpeech(); _speeches.Add(speech);
        speech.Configure(records, _configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest),
            condition => condition.Function switch
            {
                53 => (float)world.ReadVariable(quests, condition.FormArgument1, condition.Argument2),
                74 => globals.Get(condition.FormArgument1),
                70 when condition.RunOn == 1 && condition.Reference == 0 && condition.Argument1 <= 1 =>
                    (condition.Argument1 == 1) == _saved.Character.Female ? 1 : 0,
                _ => throw new NotSupportedException($"Native radio fixture condition {condition.Function}/{condition.RunOn} is unbound.")
            }, saidInfos: said, templates: reference => world.Get(reference).Templates,
            quests: quests, playerFemale: () => _saved.Character.Female, references: world,
            dialogueRandom: world.ScriptValues.RandomBounded);
        speech.PrepareSubtitle = _ => { };
        speech.SayToCompleted += _ => throw new InvalidDataException("Radio invented a scripted SayToDone event.");
        var results = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            effect => throw new NotSupportedException($"Radio fixture native effect {effect.Kind} has no owner."), Globals: globals));
        speech.ExecuteResults = (info, reference, begin) =>
        {
            results.ExecuteResult(info, reference, begin);
            _results.Add((info.Record.FormKey, begin));
        };
        speech.InfoCompleted += info => _notifications.Add(info);
        speech.ReportDivergence = _ => { };
        AddChild(speech); speech.SetProcess(false);
        return speech;
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException(message);
    }
}
