using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class OwnedGamebryoDialogueMenuRuntime : Control
{
    private readonly OwnedGamebryoDialogueMenu _source;
    private readonly ColorRect _background;
    private readonly Button _click;
    private readonly Label _speakerName;
    private readonly Button _speakerText;
    private readonly VBoxContainer _topics;
    private readonly Color _systemColor;
    private readonly float _backgroundAlpha;
    private Action? _advance;

    internal Button SpeakerTextControl => _speakerText;

    internal OwnedGamebryoDialogueMenuRuntime(
        OwnedGamebryoDialogueMenu source,
        Color systemColor,
        float backgroundAlpha,
        Font? speakerNameFont = null,
        Font? bodyFont = null)
    {
        if (!float.IsFinite(backgroundAlpha) || backgroundAlpha < 0.0f ||
            backgroundAlpha > 1.0f)
            throw new InvalidOperationException(
                "Owned DialogueMenu background alpha is invalid.");
        _source = source;
        _systemColor = systemColor;
        _backgroundAlpha = backgroundAlpha;
        Name = "DialogMenu";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        _background = new ColorRect { Name = source.BackgroundTile, MouseFilter = MouseFilterEnum.Ignore };
        _background.Color = new Color(
            systemColor.R * source.BackgroundBrightness / byte.MaxValue,
            systemColor.G * source.BackgroundBrightness / byte.MaxValue,
            systemColor.B * source.BackgroundBrightness / byte.MaxValue,
            backgroundAlpha);
        AddChild(_background);

        _click = new Button
        {
            Name = source.ClickTile,
            Flat = true,
            FocusMode = FocusModeEnum.None,
        };
        _click.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _click.Pressed += Advance;
        AddChild(_click);

        _speakerName = new Label
        {
            Name = source.SpeakerNameTile,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (speakerNameFont is not null)
            _speakerName.AddThemeFontOverride("font", speakerNameFont);
        AddChild(_speakerName);

        _speakerText = new Button
        {
            Name = source.SpeakerTextTile,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (bodyFont is not null)
            _speakerText.AddThemeFontOverride("font", bodyFont);
        _speakerText.Pressed += Advance;
        AddChild(_speakerText);

        _topics = new VBoxContainer { Name = source.TopicListTile };
        AddChild(_topics);
        Resized += LayoutFromSource;
        LayoutFromSource();
        HideMenu();
    }

    internal void ShowLine(string speaker, string text, Action advance)
    {
        if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Owned DialogueMenu line is incomplete.");
        ClearTopics();
        _speakerName.Text = speaker;
        _speakerText.Text = text;
        _speakerName.Visible = true;
        _speakerText.Visible = true;
        _topics.Visible = false;
        _background.Visible = true;
        _advance = advance;
        Visible = true;
        Callable.From(_speakerText.GrabFocus).CallDeferred();
        LayoutFromSource();
    }

    internal void ShowTopics(
        string speaker,
        IReadOnlyList<(string Identity, string Text, Action Selected)> topics)
    {
        if (string.IsNullOrWhiteSpace(speaker) || topics.Count == 0 ||
            topics.Any(topic => string.IsNullOrWhiteSpace(topic.Identity) ||
                string.IsNullOrWhiteSpace(topic.Text)))
            throw new InvalidOperationException("Owned DialogueMenu topics are incomplete.");
        ClearTopics();
        _speakerName.Text = speaker;
        _speakerName.Visible = true;
        _speakerText.Visible = false;
        _topics.Visible = true;
        _background.Visible = true;
        foreach (var topic in topics)
        {
            var button = new Button
            {
                Name = $"{_source.TopicTile}_{topic.Identity}",
                Text = topic.Text,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(
                    0.0f,
                    _source.TopicVerticalSpacing),
            };
            button.AddThemeColorOverride("font_color", _systemColor);
            button.AddThemeConstantOverride("outline_size", 0);
            button.Pressed += topic.Selected;
            _topics.AddChild(button);
        }
        Visible = true;
        Callable.From(((Button)_topics.GetChild(0)).GrabFocus).CallDeferred();
        LayoutFromSource();
    }

    internal void HideMenu()
    {
        Visible = false;
        _background.Visible = false;
        _speakerName.Visible = false;
        _speakerText.Visible = false;
        _topics.Visible = false;
        _advance = null;
        ClearTopics();
    }

    private void LayoutFromSource()
    {
        if (Size.X <= 0.0f || Size.Y <= 0.0f)
            return;
        var scale = Mathf.Min(
            Size.X / _source.CanvasSize.X,
            Size.Y / _source.CanvasSize.Y);
        var width = Mathf.Min(_source.BackgroundWidth * scale, Size.X);
        var x = (Size.X - width) / 2.0f;
        _speakerName.Position = new Vector2(0.0f, _source.SpeakerNameTopInset * scale);
        _speakerName.Size = new Vector2(
            Size.X - _source.SpeakerNameRightInset * scale,
            _speakerName.GetCombinedMinimumSize().Y);

        var bodyWidth = width - _source.SpeakerWrapInset * scale;
        var bodyX = x + _source.SpeakerLeftInset * scale;
        if (_speakerText.Visible)
        {
            _speakerText.Size = new Vector2(bodyWidth, 0.0f);
            _speakerText.ResetSize();
            _speakerText.Size = new Vector2(bodyWidth, _speakerText.GetCombinedMinimumSize().Y);
            var centered = Size.Y * _source.CenterHeightFactor - _speakerText.Size.Y / 2.0f;
            var safe = Size.Y - _source.SafeBottomInset * scale - _speakerText.Size.Y;
            _speakerText.Position = new Vector2(bodyX, Mathf.Min(centered, safe));
            _background.Position = new Vector2(
                x,
                _speakerText.Position.Y +
                    (_source.BackgroundTopInset - _source.BackgroundVerticalInset) * scale);
            _background.Size = new Vector2(
                width,
                _speakerText.Size.Y + _source.BackgroundHeightPadding * scale);
        }
        else if (_topics.Visible)
        {
            var topicWidth = width - _source.TopicWidthInset * scale;
            _topics.Size = new Vector2(topicWidth, 0.0f);
            _topics.ResetSize();
            _topics.Size = new Vector2(
                topicWidth,
                Mathf.Max(_source.TopicMinimumHeight * scale, _topics.GetCombinedMinimumSize().Y));
            var centered = Size.Y * _source.CenterHeightFactor - _topics.Size.Y / 2.0f;
            var safe = Size.Y - _source.SafeBottomInset * scale - _topics.Size.Y;
            _topics.Position = new Vector2(
                x + _source.TopicLeftInset * scale,
                Mathf.Min(centered, safe));
            _background.Position = new Vector2(
                x,
                _topics.Position.Y - _source.BackgroundVerticalInset * scale);
            _background.Size = new Vector2(
                width,
                _topics.Size.Y + _source.TopicBackgroundHeightPadding * scale);
        }
        _background.Color = new Color(_background.Color, _backgroundAlpha);
    }

    private void ClearTopics()
    {
        foreach (var child in _topics.GetChildren())
        {
            _topics.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void Advance() => _advance?.Invoke();
}
