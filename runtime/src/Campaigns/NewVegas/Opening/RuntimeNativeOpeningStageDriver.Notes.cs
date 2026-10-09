using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal void RewardExperience(double amount) => _experience.Reward(amount);

    private Node? _pipBoyNoteOwner;
    private FalloutDialogueVoiceIndex? _pipBoyNoteVoices;
    private long _pipBoyNoteSoundOccurrence;

    internal void PlayPipBoyNote(FalloutNote note)
    {
        if (_inventory.Item(note.Record.FormKey) is not { Count: > 0 })
            throw new InvalidOperationException("This note is not in your inventory.");
        var source = RuntimeLiveContentSource.Current!;
        var prepared = new Queue<AudioStreamPlayer>();
        Node? owner = null;
        try
        {
            if (note.Kind == FalloutNoteKind.Sound && note.Sound is { } sound)
                prepared.Enqueue(NativeOwnedSoundPlayback.CreateMenu(FalloutSoundRecordReader.Read(_pluginStack, sound),
                    _pluginStack, NativeOwnedSoundPlayback.MenuCall(this, checked(++_pipBoyNoteSoundOccurrence), sound)));
            else if (note.Kind == FalloutNoteKind.Voice && note.Topic is { } topic && note.Actor is { } actor)
            {
                var speaker = FalloutDialogueSpeaker.Read(_pluginStack, actor);
                var conditions = new FalloutDialogueConditions(_pluginStack, _quests, actor, speaker, EvaluateMessageCondition);
                var info = FalloutDialogueTopic.Read(_pluginStack, topic).Select(actor, new HashSet<FalloutFormKey>(),
                    quest => _quests.Stage(quest), conditions.Evaluate)
                    ?? throw new InvalidDataException("The note has no available voice response.");
                if (!string.IsNullOrWhiteSpace(info.BeginScript) || !string.IsNullOrWhiteSpace(info.EndScript))
                    throw new NotSupportedException("The note's response script is not connected to note playback.");
                _pipBoyNoteVoices ??= new(source.ResourcePathsUnder("sound/voice"));
                for (var index = 0; index < info.Responses.Count; index++)
                {
                    if (info.Responses[index].Sound is { } responseSound)
                        prepared.Enqueue(NativeOwnedSoundPlayback.CreateMenu(FalloutSoundRecordReader.Read(_pluginStack, responseSound),
                            _pluginStack, NativeOwnedSoundPlayback.MenuCall(this, checked(++_pipBoyNoteSoundOccurrence), responseSound)));
                    else
                    {
                        var binding = _pipBoyNoteVoices.Resolve(speaker, info, index);
                        prepared.Enqueue(new AudioStreamPlayer
                        {
                            Stream = NativeOwnedMediaLoader.LoadAudio(binding.AudioPath),
                            ProcessMode = ProcessModeEnum.Always
                        });
                    }
                }
            }
            else throw new InvalidDataException("The note has no playable sound or voice payload.");
            if (IsInstanceValid(_pipBoyNoteOwner)) _pipBoyNoteOwner!.QueueFree();
            owner = new Node { Name = "PipBoyNotePlayback", ProcessMode = ProcessModeEnum.Always };
            AddChild(owner);
            _pipBoyNoteOwner = owner;
            // Prepared voices share a single owner; replacing a note releases
            // every response, including ones that have not started yet.
            foreach (var voice in prepared) owner.AddChild(voice);
            void PlayNext()
            {
                if (!IsInstanceValid(owner) || owner.IsQueuedForDeletion()) return;
                if (!prepared.TryDequeue(out var next))
                {
                    owner.QueueFree();
                    if (_pipBoyNoteOwner == owner) _pipBoyNoteOwner = null;
                    return;
                }
                next.ProcessMode = ProcessModeEnum.Always;
                next.Finished += () => { next.QueueFree(); PlayNext(); };
                next.Play();
            }
            PlayNext();
        }
        catch
        {
            if (IsInstanceValid(owner)) owner!.QueueFree();
            if (_pipBoyNoteOwner == owner) _pipBoyNoteOwner = null;
            foreach (var voice in prepared) if (voice.GetParent() is null) voice.Free();
            throw;
        }
    }
}
