using System.Text.Json;
using Godot;
using OpenNV.Runtime.Gameplay.Bots;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private readonly ReactiveCampaignBot _campaignBot = new();
    private Func<IReadOnlyList<BotCampaignControl>, string, BotCampaignObservation>? _observeCampaign;
    private string _campaignEntry = "continue", _campaignSaveAction = "", _campaignPauseAction = "";
    private float _campaignElapsed;
    private string? _lastCampaignPhase;

    internal void ConfigureCampaign(
        Func<IReadOnlyList<BotCampaignControl>, string, BotCampaignObservation> observe,
        string saveAction, string pauseAction)
    {
        if (string.IsNullOrWhiteSpace(saveAction) || string.IsNullOrWhiteSpace(pauseAction))
            throw new ArgumentException("Campaign input requires the actual save and pause bindings.");
        _observeCampaign = observe; _campaignSaveAction = saveAction; _campaignPauseAction = pauseAction;
    }

    private void DispatchCampaign(JsonElement command)
    {
        var action = command.GetProperty("action").GetString();
        if (action == "stop")
        {
            _campaignBot.Stop(); _bot?.Stop(); ReleaseAll();
            return;
        }
        if (action != "start") throw new ArgumentException("Campaign action must be start or stop.");
        if (_observeCampaign is null || _bot is null)
            throw new NotSupportedException("Campaign observation/input adapters are unavailable.");
        _campaignEntry = command.TryGetProperty("entry", out var entry) ? entry.GetString()! : "continue";
        if (_campaignEntry is not ("new" or "continue"))
            throw new ArgumentException("Campaign entry must be new or continue.");
        EnsureBotSkillLibrary(); _bot.Stop(); _campaignBot.Start();
        _campaignElapsed = 0; _lastCampaignPhase = null;
        var resume = _controls.SingleOrDefault(button => button.Name.ToString() == "Resume" &&
            button.IsVisibleInTree() && !button.Disabled);
        if (resume is not null)
        {
            using var path = resume.GetPath();
            DeliverObservedButton(path.ToString());
        }
    }

    private BotCampaignControl[] ObserveCampaignControls() => _controls
        .Where(button => button.IsInsideTree() && button.IsVisibleInTree() && !button.Disabled)
        .Select(button =>
        {
            using var path = button.GetPath();
            return new BotCampaignControl(path.ToString(), ButtonText(button), button.Name.ToString(),
            button.HasMeta("opennv_source_choice_kind") ? button.GetMeta("opennv_source_choice_kind").AsString() : null,
            button.HasMeta("opennv_source_choice_id") ? button.GetMeta("opennv_source_choice_id").ToString() : null,
            button.HasMeta("opennv_source_choice_menu") ? button.GetMeta("opennv_source_choice_menu").AsString() : null,
            button.HasMeta("opennv_source_choice_goodbye") && button.GetMeta("opennv_source_choice_goodbye").AsBool());
        })
        .ToArray();

    private void TickCampaign(float seconds)
    {
        if (!_campaignBot.Active) return;
        _campaignElapsed += seconds;
        if (_campaignElapsed < .2f) return;
        var elapsed = _campaignElapsed; _campaignElapsed = 0;
        try
        {
            var observation = (_observeCampaign ??
                throw new NotSupportedException("Campaign observation adapter retired."))(ObserveCampaignControls(), _campaignEntry);
            var skill = _bot?.CampaignSkill ?? throw new NotSupportedException("Campaign skill owner retired.");
            var command = _campaignBot.Tick(observation, skill, elapsed);
            if (command.Kind == BotCampaignCommandKind.Reference)
                _bot!.Start(command.Reference, command.Mode!, 1.5f, combatTakeover: true, pauseAfter: false);
            else if (command.Kind == BotCampaignCommandKind.Choice)
                DeliverObservedButton(command.Path!);
            else if (command.Kind == BotCampaignCommandKind.Save)
                PulseBotAction(_campaignSaveAction);
            else if (command.Kind == BotCampaignCommandKind.Pause)
            {
                _bot?.Stop(); ReleaseAll();
                if (!observation.Paused && !observation.ModalInput && !observation.Loading && !observation.Defeated)
                    PulseBotAction(_campaignPauseAction);
            }
            if (_lastCampaignPhase != _campaignBot.Phase)
            {
                _lastCampaignPhase = _campaignBot.Phase;
                GD.Print("OPENNV_CAMPAIGN_BOT " + JsonSerializer.Serialize(_campaignBot.State, Json));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException or
            KeyNotFoundException or JsonException)
        {
            _campaignBot.Fail("engine-owner", error.Message, protect: false);
            _bot?.Stop(); ReleaseAll();
            GD.PushError("OPENNV_CAMPAIGN_BOT_BLOCKED " + error.Message);
        }
    }

    private void DeliverObservedButton(string path, string? expectedCaption = null)
    {
        var button = GetTree().Root.GetNodeOrNull<BaseButton>(path);
        if (button is null || !button.IsVisibleInTree() || button.Disabled)
            throw new InvalidOperationException("Observed button is no longer visible and enabled.");
        var caption = ButtonText(button);
        if (expectedCaption is not null && caption != expectedCaption)
            throw new InvalidOperationException("Recorded button caption differs from the currently observed action.");
        var stateKey = _captureIdentity().StateKey;
        var center = button.GetGlobalTransformWithCanvas() * (button.Size / 2);
        if (!button.GetViewport().GetVisibleRect().HasPoint(center))
            throw new InvalidOperationException("Observed button has no clickable center inside its viewport.");
        PushHarnessInput(button.GetViewport(), new InputEventMouseButton
        { Position = center, GlobalPosition = center, ButtonIndex = MouseButton.Left, Pressed = true });
        PushHarnessInput(button.GetViewport(), new InputEventMouseButton
        { Position = center, GlobalPosition = center, ButtonIndex = MouseButton.Left, Pressed = false });
        RecordInput(JsonSerializer.SerializeToElement(new { op = "button", path, text = caption }, Json), stateKey);
    }
}
