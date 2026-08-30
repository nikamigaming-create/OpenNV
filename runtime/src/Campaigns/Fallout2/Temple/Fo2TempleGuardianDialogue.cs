using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2TempleGuardianDialogue : Control
{
    private readonly Fo2TempleGuardianScript _script;
    private readonly string _playerName;
    private readonly int _playerIntelligence;
    private readonly Label _reply;
    private readonly VBoxContainer _options;
    private readonly List<string> _visitedNodes = [];

    internal Fo2TempleGuardianDialogue(
        Fo2TempleGuardianScript script,
        string playerName,
        int playerIntelligence,
        string playerArtFid,
        float widthPixels,
        int fontSizePixels)
    {
        if (!script.PreTrialPlayerArtFids.Contains(playerArtFid) ||
            string.IsNullOrWhiteSpace(playerName) ||
            playerIntelligence is < 1 or > 10 || widthPixels <= 0.0f ||
            fontSizePixels <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ACKlint dialogue requires the admitted pre-trial player identity.");
        _script = script;
        _playerName = playerName;
        _playerIntelligence = playerIntelligence;
        Name = "FO2_ACKLINT_SOURCE_DIALOGUE";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        SetMeta("script_schema", script.Schema);
        SetMeta("script_sha256", script.ProgramSha256);
        SetMeta("dialogue_sha256", script.ContractSha256);
        SetMeta("message_catalog_sha256", script.MessageSha256);
        SetMeta("presentation_parity", false);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer
        {
            Name = "FO2_ACKLINT_DIALOGUE_PANEL",
            CustomMinimumSize = new Vector2(widthPixels, 0.0f),
        };
        center.AddChild(panel);
        var content = new VBoxContainer();
        panel.AddChild(content);
        var title = Label("KLINT — owned ACKlint.msg / bounded non-parity dialogue", fontSizePixels);
        content.AddChild(title);
        _reply = Label("", fontSizePixels);
        _reply.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_reply);
        _options = new VBoxContainer { Name = "FO2_ACKLINT_DIALOGUE_OPTIONS" };
        content.AddChild(_options);
    }

    internal bool IsOpen => Visible;
    internal string CurrentNodeId { get; private set; } = "";
    internal string ReplyText => _reply.Text;
    internal IReadOnlyList<string> VisitedNodes => _visitedNodes;
    internal IReadOnlyList<Fo2TempleGuardianDialogueOption> AvailableOptions =>
        string.IsNullOrEmpty(CurrentNodeId)
            ? []
            : _script.Nodes[CurrentNodeId].Options
                .Where(Eligible)
                .ToArray();

    internal void Open()
    {
        if (IsOpen)
            return;
        Visible = true;
        _visitedNodes.Clear();
        ShowNode(_script.InitialNode);
    }

    internal bool Choose(int messageId)
    {
        if (!IsOpen || string.IsNullOrEmpty(CurrentNodeId))
            return false;
        var matches = AvailableOptions.Where(option => option.MessageId == messageId).ToArray();
        if (matches.Length != 1)
            return false;
        var option = matches[0];
        if (option.Target == _script.TerminalNode)
        {
            Visible = false;
            CurrentNodeId = "";
            ClearOptions();
            return true;
        }
        ShowNode(option.Target);
        return true;
    }

    internal bool Close()
    {
        if (!IsOpen)
            return false;
        Visible = false;
        CurrentNodeId = "";
        ClearOptions();
        return true;
    }

    private void ShowNode(string nodeId)
    {
        if (!_script.Nodes.TryGetValue(nodeId, out var node))
            throw new InvalidOperationException(
                $"Fallout 2 ACKlint dialogue node is unavailable: {nodeId}");
        CurrentNodeId = nodeId;
        _visitedNodes.Add(nodeId);
        _reply.Text = string.Concat(node.Reply.Select(segment =>
            segment.PlayerName ? _playerName : segment.Text));
        ClearOptions();
        foreach (var option in node.Options.Where(Eligible))
        {
            var button = new Button
            {
                Name = $"FO2_ACKLINT_OPTION_{option.MessageId}",
                Text = option.Text,
            };
            button.Pressed += () => Choose(option.MessageId);
            _options.AddChild(button);
        }
        if (_options.GetChildCount() == 0)
            throw new InvalidOperationException(
                $"Fallout 2 ACKlint dialogue node has no eligible options: {nodeId}");
        (_options.GetChild(0) as Control)?.GrabFocus();
    }

    private bool Eligible(Fo2TempleGuardianDialogueOption option) =>
        (option.MinimumIntelligence is null ||
            _playerIntelligence >= option.MinimumIntelligence) &&
        (option.MaximumIntelligence is null ||
            _playerIntelligence <= option.MaximumIntelligence);

    private void ClearOptions()
    {
        foreach (var child in _options.GetChildren())
        {
            _options.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Label Label(string text, int fontSizePixels)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSizePixels);
        return label;
    }
}
