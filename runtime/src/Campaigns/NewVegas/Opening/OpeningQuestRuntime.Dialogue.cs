using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;


using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private void PlayTopicEditor(string editorId, Action completed, int generation)
    {
        var topic = _flow.TopicsByEditorId.GetValueOrDefault(editorId) ??
            _flow.OrdinaryActors.SelectMany(actor => actor.Topics.Values)
                .SingleOrDefault(value => value.EditorId.Equals(
                    editorId, StringComparison.OrdinalIgnoreCase));
        if (topic is null)
            throw new InvalidOperationException($"Owned dialogue topic is absent: {editorId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopicForm(string formId, Action completed, int generation)
    {
        var topic = _flow.TopicsByFormId.GetValueOrDefault(formId) ??
            _flow.OrdinaryActors.SelectMany(actor => actor.Topics.Values)
                .SingleOrDefault(value => value.FormId.Equals(
                    formId, StringComparison.OrdinalIgnoreCase));
        if (topic is null)
            throw new InvalidOperationException($"Owned dialogue topic is absent: {formId}");
        PlayTopic(topic, completed, generation);
    }

    private void PlayTopic(OpeningDialogueTopic topic, Action completed, int generation)
    {
        var cursor = _topicCursors.GetValueOrDefault(topic.FormId);
        var selection = GamebryoDialoguePlayback.SelectFirstInfo(
            topic.Infos.Select(info =>
                new SourceDialogueInfoCandidate<OpeningDialogueInfo, OpeningDialogueCondition>(
                    info.FormId,
                    info.SourceOrder,
                    info.SayOnce,
                    info.Conditions,
                    info)).ToArray(),
            cursor,
            _saidOnce,
            EvaluateCondition);
        _topicCursors[topic.FormId] = selection?.NextCursor ?? topic.Infos.Count;
        if (selection is null)
        {
            CloseModal();
            completed();
            return;
        }
        var selected = selection.Value;
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
        if (lineIndex == 0)
        {
            GD.Print(
                $"OPENNV_NEW_GAME_DIALOGUE_INFO info={info.FormId} " +
                $"responses={info.Responses.Count} choices={info.NextTopicFormIds.Count}");
            GamebryoDialoguePlayback.ValidateOrderedLines(
                info.Responses.Select(response => SourceLine(info.FormId, response)).ToArray());
        }
        if (lineIndex >= info.Responses.Count)
        {
            ExecuteInfoCommands(info, topic, completed, generation, 0);
            return;
        }
        var response = info.Responses[lineIndex];
        var binding = ResolveDialogueBinding(info.FormId);
        var menu = OpenDialogueMenu();
        menu.ShowLine(
            _flow.SceneRoles[binding.Role].DisplayName,
            response.Text,
            CompleteDialogueVoice);
        StartDialogueVoice(
            response,
            info.FormId,
            binding,
            generation,
            () => PlayInfo(
                info,
                topic,
                completed,
                generation,
                lineIndex + 1));
    }

    private OwnedGamebryoDialogueMenuRuntime OpenDialogueMenu()
    {
        if (!_flow.Menus.TryGetValue("dialogue", out var source) ||
            source.DialogueMenu is null)
            throw new InvalidOperationException(
                "Owned DialogueMenu tile contract is unavailable.");
        var root = OpenModalRoot("dialogue");
        var fonts = OwnedGamebryoTileRuntime.RequireDialogueFonts(
            source.DialogueMenu,
            _opening.GameplayUi.Fonts);
        var menu = new OwnedGamebryoDialogueMenuRuntime(
            source.DialogueMenu,
            _opening.MainMenuColor,
            OwnedUiTheme.NormalizeByteChannel(_opening.Style.BackgroundFillAlpha),
            OwnedUiTheme.BuildFont(fonts.SpeakerName),
            OwnedUiTheme.BuildFont(fonts.Body));
        menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(menu);
        return menu;
    }

    private void StartDialogueVoice(
        OpeningDialogueResponse response,
        string infoFormId,
        DialogueBinding binding,
        int flowGeneration,
        Action completed)
    {
        _activeDialogueInfoFormId = infoFormId;
        _activeDialogueResponseIndex = response.Index;
        _dialoguePlayback.Start(
            SourceLine(infoFormId, response, binding.VoiceTypeFormId),
            binding.Face,
            () =>
            {
                if (flowGeneration != _generation)
                    return;
                _activeDialogueInfoFormId = null;
                _activeDialogueResponseIndex = 0;
                completed();
            });
    }

    private SourceDialogueLine SourceLine(
        string infoFormId,
        OpeningDialogueResponse response,
        string? voiceTypeFormId = null) =>
        new(
            infoFormId,
            response.Index,
            voiceTypeFormId ?? _flow.DialogueVoice.VoiceTypeFormId,
            response.Text,
            new SourceDialogueAsset(
                response.Voice.LogicalPath,
                response.Voice.SourcePath,
                response.Voice.Sha256),
            new SourceDialogueAsset(
                response.Lip.LogicalPath,
                response.Lip.SourcePath,
                response.Lip.Sha256));

    private DialogueBinding ResolveDialogueBinding(string infoFormId)
    {
        var actor = _flow.OrdinaryActors.SingleOrDefault(candidate =>
            candidate.Topics.Values.SelectMany(topic => topic.Infos).Any(info =>
                info.FormId.Equals(infoFormId, StringComparison.OrdinalIgnoreCase)));
        return actor is null
            ? new DialogueBinding(
                _flow.DialogueVoice.SpeakerRole,
                _flow.DialogueVoice.VoiceTypeFormId,
                _dialogueFace)
            : new DialogueBinding(
                actor.Role,
                actor.Voice.VoiceTypeFormId,
                _ordinaryDialogueFaces[actor.Role]);
    }

    private sealed record DialogueBinding(
        string Role,
        string VoiceTypeFormId,
        FaceGenMorphController Face);

    private void UpdateDialogueVoice(double deltaSeconds)
    {
        _dialoguePlayback.Update(deltaSeconds);
    }

    private void CompleteDialogueVoice()
    {
        _dialoguePlayback.Complete();
    }

    private void StopDialogueVoice()
    {
        _dialoguePlayback?.Stop();
        _activeDialogueInfoFormId = null;
        _activeDialogueResponseIndex = 0;
    }
}
