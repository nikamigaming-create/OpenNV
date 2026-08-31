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
            GamebryoDialoguePlayback.ValidateOrderedLines(
                info.Responses.Select(response => SourceLine(info.FormId, response)).ToArray());
        if (lineIndex >= info.Responses.Count)
        {
            ExecuteInfoCommands(info, topic, completed, generation, 0);
            return;
        }
        var response = info.Responses[lineIndex];
        var menu = OpenDialogueMenu();
        menu.ShowLine(
            _flow.SceneRoles[_flow.DialogueVoice.SpeakerRole].DisplayName,
            response.Text,
            CompleteDialogueVoice);
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
            _opening.Style.BackgroundFillAlpha,
            OwnedUiTheme.BuildFont(fonts.SpeakerName),
            OwnedUiTheme.BuildFont(fonts.Body));
        menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(menu);
        return menu;
    }

    private void StartDialogueVoice(
        OpeningDialogueResponse response,
        string infoFormId,
        int flowGeneration,
        Action completed)
    {
        _activeDialogueInfoFormId = infoFormId;
        _activeDialogueResponseIndex = response.Index;
        _dialoguePlayback.Start(
            SourceLine(infoFormId, response),
            _dialogueFace,
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
        OpeningDialogueResponse response) =>
        new(
            infoFormId,
            response.Index,
            _flow.DialogueVoice.VoiceTypeFormId,
            response.Text,
            new SourceDialogueAsset(
                response.Voice.LogicalPath,
                response.Voice.SourcePath,
                response.Voice.Sha256),
            new SourceDialogueAsset(
                response.Lip.LogicalPath,
                response.Lip.SourcePath,
                response.Lip.Sha256));

    private void UpdateDialogueVoice()
    {
        _dialoguePlayback.Update();
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
