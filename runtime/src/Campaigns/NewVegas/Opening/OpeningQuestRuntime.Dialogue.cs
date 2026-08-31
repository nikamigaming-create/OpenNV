using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private void PlayTopicEditor(string editorId, Action completed, int generation)
    {
        if (!_flow.TopicsByEditorId.TryGetValue(editorId, out var topic))
            throw new InvalidOperationException($"Owned dialogue topic is absent: {editorId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopicForm(string formId, Action completed, int generation)
    {
        if (!_flow.TopicsByFormId.TryGetValue(formId, out var topic))
            throw new InvalidOperationException($"Owned dialogue topic is absent: {formId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopic(OpeningDialogueTopic topic, Action completed, int generation)
    {
        var cursor = _topicCursors.GetValueOrDefault(topic.FormId);
        OpeningDialogueInfo? selected = null;
        while (cursor < topic.Infos.Count)
        {
            var candidate = topic.Infos[cursor++];
            if (candidate.SayOnce && _saidOnce.Contains(candidate.FormId))
                continue;
            if (!candidate.Conditions.All(EvaluateCondition))
                continue;
            selected = candidate;
            break;
        }
        _topicCursors[topic.FormId] = cursor;
        if (selected is null)
        {
            CloseModal();
            completed();
            return;
        }
        if (selected.SayOnce)
            _saidOnce.Add(selected.FormId);
        PlayInfo(selected, topic, completed, generation);
    }

    private void PlayInfo(
        OpeningDialogueInfo info,
        OpeningDialogueTopic? topic,
        Action completed,
        int generation,
        int lineIndex = 0)
    {
        if (generation != _generation)
            return;
        if (lineIndex >= info.Responses.Count)
        {
            ExecuteInfoCommands(info, topic, completed, generation, 0);
            return;
        }
        var response = info.Responses[lineIndex];
        var content = OpenPanel(MenuRect("name"));
        var guide = NewLabel(
            _flow.SceneRoles[_flow.DialogueVoice.SpeakerRole].DisplayName);
        guide.HorizontalAlignment = HorizontalAlignment.Right;
        content.AddChild(guide);
        var line = NewButton(response.Text);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.Alignment = HorizontalAlignment.Left;
        line.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        line.Pressed += CompleteDialogueVoice;
        content.AddChild(line);
        StartDialogueVoice(
            response,
            info.FormId,
            generation,
            () => PlayInfo(
                info,
                topic,
                completed,
                generation,
                lineIndex + 1));
        Callable.From(line.GrabFocus).CallDeferred();
    }

    private void StartDialogueVoice(
        OpeningDialogueResponse response,
        string infoFormId,
        int flowGeneration,
        Action completed)
    {
        StopDialogueVoice();
        var stream = AudioStreamOggVorbis.LoadFromFile(response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Owned dialogue voice could not be decoded: {response.Voice.LogicalPath}");
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                $"Owned dialogue voice has no duration: {response.Voice.LogicalPath}");
        var lip = FaceGenLipAnimation.Load(
            response.Lip.SourcePath,
            _configuration.ActorCompiler.FaceGenAnimation.Lip);
        var playbackGeneration = ++_dialoguePlaybackGeneration;
        _dialogueVoice.Stream = stream;
        _activeDialogueLip = lip;
        _activeDialogueInfoFormId = infoFormId;
        _activeDialogueResponseIndex = response.Index;
        _dialogueLipSampleLogged = false;
        _dialogueVoiceCompletion = () =>
        {
            if (playbackGeneration != _dialoguePlaybackGeneration ||
                flowGeneration != _generation)
                return;
            StopDialogueVoice();
            completed();
        };
        _dialogueVoice.Play();
        GD.Print(
            $"OPENNV_NEW_GAME_DIALOGUE_VOICE info={infoFormId} " +
            $"line={response.Index} duration={durationSeconds:F3} " +
            $"voice={response.Voice.LogicalPath} lip={response.Lip.LogicalPath}");
        GD.Print(
            $"OPENNV_NEW_GAME_DIALOGUE_LIP_LOADED info={infoFormId} " +
            $"line={response.Index} frames={lip.FrameCount} startFrame={lip.StartFrame} " +
            $"metadata=0x{lip.MetadataWord:x8}");
    }

    private void UpdateDialogueVoice()
    {
        if (_dialogueVoiceCompletion is null ||
            _activeDialogueLip is null ||
            !_dialogueVoice.Playing)
            return;
        var seconds = _dialogueVoice.GetPlaybackPosition();
        var dominant = _dialogueFace.Apply(_activeDialogueLip, seconds);
        if (!_dialogueLipSampleLogged && dominant.Value != 0.0f)
        {
            _dialogueLipSampleLogged = true;
            GD.Print(
                $"OPENNV_NEW_GAME_DIALOGUE_LIP_SAMPLE info={_activeDialogueInfoFormId} " +
                $"line={_activeDialogueResponseIndex} seconds={seconds:F3} " +
                $"target={dominant.Target} value={dominant.Value:F6}");
        }
    }

    private void CompleteDialogueVoice()
    {
        var completed = _dialogueVoiceCompletion;
        _dialogueVoiceCompletion = null;
        completed?.Invoke();
    }

    private void StopDialogueVoice()
    {
        _dialogueVoiceCompletion = null;
        _dialogueFace?.Clear();
        _activeDialogueLip = null;
        _activeDialogueInfoFormId = null;
        _activeDialogueResponseIndex = 0;
        _dialogueLipSampleLogged = false;
        _dialoguePlaybackGeneration++;
        if (_dialogueVoice is not null && _dialogueVoice.Playing)
            _dialogueVoice.Stop();
    }
}
