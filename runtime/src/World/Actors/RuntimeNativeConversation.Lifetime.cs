using Godot;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation
{
    private Action? _dialogueInput;
    private NativeConversationVoiceLease? _nativeResponse;
    private bool _retired, _closing;
    private string? _retirementFailure;

    private void OpenConversationPresentation()
    {
        if (_retired || _layer is not null || _menu is not null || _dialogueInput is not null)
            throw new InvalidOperationException("Conversation cannot replace another living native presentation/input owner.");
        _speech.BeginPlayerDialogue(_speaker);
        _layer = new CanvasLayer { Layer = 95 }; AddChild(_layer);
        _menu = new(SkipPresentedResponse, Fail, _standaloneNativeInterface); _layer.AddChild(_menu);
        PublishStandaloneDialog(); PublishSourceMainDialog();
        _dialogueInput = _player.AcquireModalInput();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
    private void StartPresentedResponse()
    {
        if (_nativeResponse is not null) throw new InvalidOperationException("Conversation already owns a returned native response start.");
        var response = _conversation.EnterResponse();
        _nativeResponse = _speech.StartConversationResponse(_speaker, _dialogueSubject, _identity,
            _conversation, response, actual => Guard(() =>
            {
                if (_retired || _nativeResponse != actual)
                    throw new InvalidOperationException("Dialogue completion lost its actual living conversation owner.");
                _nativeResponse = null;
                _conversation.CompleteResponse(actual.Response);
                Present();
            }));
    }
    private void SkipPresentedResponse()
    {
        if (_retired || _nativeResponse is not { } response)
            throw new InvalidOperationException("Skip has no actual current conversation response owner.");
        _requireGameplayAdmission();
        _speech.SkipConversationResponse(response, _conversation);
    }
    private void CloseConversationPresentation()
    {
        if (_closing) return;
        _closing = true;
        var errors = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
        try
        {
            if (_nativeResponse is { } response)
            {
                _nativeResponse = null;
                Attempt(() => _speech.AbandonConversationResponse(response));
            }
            Attempt(CloseStandaloneDialog); Attempt(CloseSourceMainDialog); Attempt(ReleaseFacing);
            var layer = _layer; _layer = null; _menu = null;
            if (GodotObject.IsInstanceValid(layer)) Attempt(layer!.QueueFree);
            var input = _dialogueInput; _dialogueInput = null;
            if (input is not null) Attempt(input);
            if (GodotObject.IsInstanceValid(_player))
                Attempt(() => Input.MouseMode = _player.ModalInput ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured);
        }
        finally { _closing = false; }
        if (errors.Count != 0)
        {
            _retirementFailure ??= string.Join(" | ", errors.Select(error => error.Message));
            throw new AggregateException("Conversation presentation/input retirement failed.", errors);
        }
    }
    private void RetainConversationFailure(Exception error)
    {
        if (Error is not null) return;
        Error = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        _conversation?.RetainExternalFailure(error);
        try { CloseConversationPresentation(); }
        catch (Exception cleanup) { _retirementFailure ??= cleanup.Message; }
        GD.PushError($"OPENNV_NATIVE_CONVERSATION_DIVERGENCE speaker={_speaker}: {Error}; retirement={_retirementFailure}");
    }
    private void RetireConversation()
    {
        if (_retired) return;
        _retired = true;
        try { CloseConversationPresentation(); }
        catch (Exception error) { RetainConversationFailure(error); }
    }
}
