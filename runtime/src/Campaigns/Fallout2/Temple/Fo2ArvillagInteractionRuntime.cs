using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2ArvillagInteractionRuntime : Control
{
    private readonly Fo2ArvillagPresentationCatalog _catalog;
    private readonly Fo2ArvillagIntRuntime _scripts;
    private readonly Fo2ArroyoCavesPlayerBody _player;
    private readonly string _lookAction;
    private readonly string _talkAction;
    private readonly Action _stateChanged;
    private readonly Label _reply;
    private readonly VBoxContainer _options;
    private string? _activeRole;
    private IReadOnlyList<ClassicIntDialogueOption> _availableOptions = [];

    private Fo2ArvillagInteractionRuntime(
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArvillagIntRuntime scripts,
        Fo2ArroyoCavesPlayerBody player,
        string lookAction,
        string talkAction,
        float widthPixels,
        int fontSizePixels,
        Action stateChanged)
    {
        _catalog = catalog;
        _scripts = scripts;
        _player = player;
        _lookAction = lookAction;
        _talkAction = talkAction;
        _stateChanged = stateChanged;
        Name = "FO2_ARVILLAG_SOURCE_DIALOGUE";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(widthPixels, 0.0f),
            Visible = false,
        };
        panel.SetMeta("owned_int_dialogue", true);
        center.AddChild(panel);
        var content = new VBoxContainer();
        panel.AddChild(content);
        _reply = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _reply.AddThemeFontSizeOverride("font_size", fontSizePixels);
        content.AddChild(_reply);
        _options = new VBoxContainer();
        content.AddChild(_options);
        DialoguePanel = panel;
    }

    internal PanelContainer DialoguePanel { get; }
    internal bool DialogueOpen => DialoguePanel.Visible;
    internal string ReplyText => _reply.Text;
    internal IReadOnlyList<ClassicIntDialogueOption> AvailableOptions =>
        _availableOptions;

    internal static Fo2ArvillagInteractionRuntime Build(
        Node parent,
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArvillagIntRuntime scripts,
        Fo2ArroyoCavesPlayerBody player,
        string lookAction,
        string talkAction,
        float widthPixels,
        int fontSizePixels,
        Action stateChanged)
    {
        if (string.IsNullOrWhiteSpace(lookAction) ||
            string.IsNullOrWhiteSpace(talkAction) || lookAction == talkAction ||
            widthPixels <= 0.0f || fontSizePixels <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG interaction input is invalid.");
        var result = new Fo2ArvillagInteractionRuntime(
            catalog, scripts, player, lookAction, talkAction,
            widthPixels, fontSizePixels, stateChanged);
        parent.AddChild(result);
        return result;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (DialogueOpen && _activeRole is not null)
            return;
        if (Input.IsActionJustPressed(_lookAction))
            Look();
        else if (Input.IsActionJustPressed(_talkAction))
            Talk();
    }

    internal bool Look()
    {
        var role = FacingRole();
        if (role is null)
            return false;
        var message = _scripts.LookAt(role);
        _activeRole = null;
        _availableOptions = [];
        ClearOptions();
        _reply.Text = message;
        DialoguePanel.Visible = true;
        MouseFilter = MouseFilterEnum.Ignore;
        return true;
    }

    internal bool Talk()
    {
        var role = FacingRole();
        if (role is null)
            return false;
        _activeRole = role;
        Show(_scripts.Talk(role));
        _stateChanged();
        return true;
    }

    internal bool Choose(int messageId)
    {
        if (!DialogueOpen || _activeRole is null)
            return false;
        var matches = _availableOptions.Where(row => row.MessageId == messageId)
            .ToArray();
        if (matches.Length != 1)
            return false;
        Show(_scripts.Choose(_activeRole, matches[0]));
        _stateChanged();
        return true;
    }

    internal void Close()
    {
        DialoguePanel.Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        _player.SetControlsEnabled(true);
        _activeRole = null;
        _availableOptions = [];
        ClearOptions();
    }

    private string? FacingRole()
    {
        var facingTile = Fo1HexMath.TileInDirection(
            _player.CurrentTile, _player.Presentation.Direction);
        var matches = _scripts.Roles.Values.Where(row =>
            row.WorldState.Objects[row.ActorHandle].Tile == facingTile &&
            row.WorldState.Objects[row.ActorHandle].Elevation ==
                _player.CurrentElevation).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Role,
            _ => throw new InvalidOperationException(
                "Fallout 2 ARVILLAG facing hex has ambiguous admitted actors."),
        };
    }

    private void Show(ClassicIntProcedureResult execution)
    {
        if (_activeRole is null)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG dialogue has no source role.");
        var role = _catalog.IntRoles[_activeRole];
        var dialogue = execution.WorldObjects;
        if (!dialogue.DialogueReady || dialogue.DialogueStart is null ||
            dialogue.DialogueReplies.Count == 0 ||
            dialogue.DialogueReplies.Any(row =>
                row.MessageList != role.MessageListId ||
                !role.Messages.ContainsKey(row.MessageId)) ||
            dialogue.DialogueOptions.Any(row =>
                row.MessageList != role.MessageListId ||
                !role.Messages.ContainsKey(row.MessageId)))
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG {_activeRole} dialogue result drifted.");
        _reply.Text = string.Join(" ", dialogue.DialogueReplies.Select(row =>
            role.Messages[row.MessageId]));
        var intelligence = _scripts.PlayerIntelligence;
        _availableOptions = dialogue.DialogueOptions.Where(row =>
            row.Intelligence >= 0
                ? intelligence >= row.Intelligence
                : intelligence <= -row.Intelligence).ToArray();
        ClearOptions();
        foreach (var option in _availableOptions)
        {
            var button = new Button { Text = role.Messages[option.MessageId] };
            button.Pressed += () => Choose(option.MessageId);
            _options.AddChild(button);
        }
        DialoguePanel.Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        _player.SetControlsEnabled(false);
        (_options.GetChildCount() == 0 ? DialoguePanel : _options.GetChild(0) as Control)
            ?.GrabFocus();
    }

    private void ClearOptions()
    {
        foreach (var child in _options.GetChildren())
        {
            _options.RemoveChild(child);
            child.QueueFree();
        }
    }
}
