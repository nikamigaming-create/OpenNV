using Godot;


using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed class OpeningRaceSexMenuHost
{
    private readonly OpeningRaceSexMenuTiles _source;
    private readonly FontFile _font;
    private readonly Color _systemColor;
    private readonly OwnedUiStyle _style;
    private readonly Control _root;
    private readonly Control _content;
    private readonly Button _back;
    private readonly Button _next;
    private readonly TextureButton _scrollUp;
    private readonly TextureButton _scrollDown;
    private readonly string _sliderLeftLabel;
    private readonly string _sliderRightLabel;
    private readonly Action<string> _activeListChanged;
    private int _scrollOffset;
    private int _visibleEntryCount;
    private string _activeList = "";
    private IReadOnlyList<OpeningRaceSexListEntry> _listEntries = [];
    private IReadOnlyList<OpeningRaceSexSliderEntry> _sliderEntries = [];
    private Action? _backAction;
    private Action? _nextAction;

    internal string ActiveList => _activeList;
    internal int ActiveEntryCount =>
        _listEntries.Count > 0 ? _listEntries.Count : _sliderEntries.Count;
    internal int VisibleEntryCount => _visibleEntryCount;

    internal OpeningRaceSexMenuHost(
        OpeningRaceSexMenuTiles source,
        Color systemColor,
        OwnedUiStyle style,
        Control root,
        string sliderLeftLabel,
        string sliderRightLabel,
        Action<string> activeListChanged)
    {
        if (source.Background.Texture.Texture is null ||
            source.Scroll.Up.Texture.Texture is null ||
            source.Scroll.Down.Texture.Texture is null ||
            source.ListItem.SelectionIndicator.Texture.Texture is null ||
            source.Scroll.Up.Rect is not { } upRect ||
            source.Scroll.Down.Rect is not { } downRect ||
            string.IsNullOrEmpty(sliderLeftLabel) ||
            string.IsNullOrEmpty(sliderRightLabel) ||
            activeListChanged is null)
            throw new InvalidOperationException(
                "Owned RaceSexMenu render contract is incomplete.");
        _source = source;
        _font = OwnedUiTheme.BuildFont(source.Font);
        _systemColor = systemColor;
        _style = style;
        _root = root;
        _sliderLeftLabel = sliderLeftLabel;
        _sliderRightLabel = sliderRightLabel;
        _activeListChanged = activeListChanged;

        var backgroundTexture = new TextureRect
        {
            Texture = LoadTexture(source.Background.Texture),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = OwnedUiTheme.Brightness(
                systemColor,
                source.Background.Brightness),
        };
        OwnedGamebryoTileRuntime.ApplyAbsolute(
            backgroundTexture,
            Layout(source.Background.Tile, source.Background.Rect));
        root.AddChild(backgroundTexture);
        _content = new Control
        {
            Name = "OwnedRaceSexActiveList",
            Position = source.Background.Rect.Position,
            Size = source.Background.Rect.Size,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        root.AddChild(_content);

        _scrollUp = NewTextureButton(source.Scroll.Up, upRect);
        _scrollDown = NewTextureButton(source.Scroll.Down, downRect);
        _scrollUp.Pressed += () => Scroll(-1);
        _scrollDown.Pressed += () => Scroll(1);
        _content.AddChild(_scrollUp);
        _content.AddChild(_scrollDown);

        _back = NewNavigationButton(source.SharedControls.Back);
        _next = NewNavigationButton(source.SharedControls.Next);
        _back.Pressed += () => _backAction?.Invoke();
        _next.Pressed += () => _nextAction?.Invoke();
        _content.AddChild(_back);
        _content.AddChild(_next);
    }

    internal Control FaceGrabHost()
    {
        var result = new Control
        {
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        OwnedGamebryoTileRuntime.ApplyAbsolute(
            result,
            Layout(_source.FaceGrab.Tile, _source.FaceGrab.Rect));
        _root.AddChild(result);
        return result;
    }

    internal void ShowList(
        string activeList,
        IReadOnlyList<OpeningRaceSexListEntry> entries,
        Action? back,
        Action next)
    {
        if (string.IsNullOrWhiteSpace(activeList) || entries.Count == 0)
            throw new InvalidOperationException(
                "Owned RaceSexMenu list state is incomplete.");
        var preserveScroll = _activeList == activeList && _listEntries.Count > 0;
        _activeList = activeList;
        _activeListChanged(activeList);
        _listEntries = entries;
        _sliderEntries = [];
        if (!preserveScroll)
            _scrollOffset = 0;
        BindNavigation(back, next);
        RenderActiveList();
    }

    internal void ShowSliders(
        string activeList,
        IReadOnlyList<OpeningRaceSexSliderEntry> entries,
        Action back,
        Action next)
    {
        if (string.IsNullOrWhiteSpace(activeList) || entries.Count == 0)
            throw new InvalidOperationException(
                "Owned RaceSexMenu slider state is incomplete.");
        var preserveScroll = _activeList == activeList && _sliderEntries.Count > 0;
        _activeList = activeList;
        _activeListChanged(activeList);
        _listEntries = [];
        _sliderEntries = entries;
        if (!preserveScroll)
            _scrollOffset = 0;
        BindNavigation(back, next);
        RenderActiveList();
    }

    internal void ActivateListEntry(string key)
    {
        var entry = _listEntries.SingleOrDefault(value => value.Key == key) ??
            throw new InvalidOperationException(
                $"Owned RaceSexMenu list entry is unavailable: {key}");
        entry.Activate();
    }

    internal void SetSliderValue(string key, float value)
    {
        var entry = _sliderEntries.SingleOrDefault(item => item.Key == key) ??
            throw new InvalidOperationException(
                $"Owned RaceSexMenu slider entry is unavailable: {key}");
        entry.SetValue(Mathf.Clamp(value, entry.Minimum, entry.Maximum));
    }

    internal void PressBack() => _back.EmitSignal(BaseButton.SignalName.Pressed);

    internal void PressNext() => _next.EmitSignal(BaseButton.SignalName.Pressed);

    private void BindNavigation(Action? back, Action next)
    {
        _backAction = back;
        _nextAction = next;
        _back.Visible = back is not null;
    }

    private void Scroll(int direction)
    {
        var count = _listEntries.Count > 0 ? _listEntries.Count : _sliderEntries.Count;
        var capacity = VisibleCapacity();
        _scrollOffset = Mathf.Clamp(
            _scrollOffset + direction,
            0,
            Math.Max(0, count - capacity));
        RenderActiveList();
    }

    private int VisibleCapacity()
    {
        var rowHeight = _listEntries.Count > 0
            ? _source.SharedControls.List.Rect.Size.Y
            : _source.SharedControls.Slider.Rect.Size.Y;
        return Math.Max(
            1,
            Mathf.FloorToInt(
                (_source.SharedControls.BottomBound - _source.SharedControls.TopBound) /
                rowHeight));
    }

    private void RenderActiveList()
    {
        foreach (var child in _content.GetChildren())
        {
            if (child != _back && child != _next &&
                child != _scrollUp && child != _scrollDown)
                child.Free();
        }
        var count = _listEntries.Count > 0 ? _listEntries.Count : _sliderEntries.Count;
        var capacity = VisibleCapacity();
        _scrollOffset = Mathf.Clamp(
            _scrollOffset,
            0,
            Math.Max(0, count - capacity));
        _scrollUp.Visible = _scrollOffset > 0;
        _scrollDown.Visible = _scrollOffset + capacity < count;
        if (_listEntries.Count > 0)
            RenderListRows(capacity);
        else
            RenderSliderRows(capacity);
        _visibleEntryCount = Math.Min(capacity, count - _scrollOffset);
        GD.Print(
            "OPENNV_NEW_GAME_RACESEX_ACTIVE_LIST " +
            $"user0={_activeList} first={_scrollOffset} " +
            $"visible={_visibleEntryCount} total={count} " +
            $"rowHeight={(_listEntries.Count > 0 ? _source.ListItem.Rect.Size.Y : _source.Slider.Rect.Size.Y):R}");
    }

    private void RenderListRows(int capacity)
    {
        var template = _source.ListItem;
        var indicator = template.SelectionIndicator;
        for (var slot = 0; slot < capacity && _scrollOffset + slot < _listEntries.Count; slot++)
        {
            var entry = _listEntries[_scrollOffset + slot];
            var row = NewRowButton(
                $"OwnedRaceSexList_{entry.Key}",
                    _source.SharedControls.List.Rect.Position.X,
                    _source.SharedControls.TopBound +
                        slot * _source.SharedControls.List.Rect.Size.Y,
                    _source.SharedControls.List.Rect.Size);
            if (entry.Selectable)
                row.Pressed += entry.Activate;
            else
            {
                row.FocusMode = Control.FocusModeEnum.None;
                row.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            _content.AddChild(row);
            if (entry.Selected)
            {
                row.AddChild(new TextureRect
                {
                    Name = indicator.Tile,
                    Texture = LoadTexture(indicator.Texture),
                    Position = indicator.Rect.Position,
                    Size = indicator.Rect.Size,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    SelfModulate = SourceBrightness(template.Brightness),
                });
            }
            var textX = entry.Selectable
                ? template.Text.SelectableX!.Value
                : template.Text.NotSelectableX!.Value;
            var label = NewText(entry.Label, template.Brightness);
            label.Position = new Vector2(textX, template.Text.Y);
            label.Size = TextSize(entry.Label);
            row.AddChild(label);
        }
    }

    private void RenderSliderRows(int capacity)
    {
        var template = _source.Slider;
        for (var slot = 0; slot < capacity && _scrollOffset + slot < _sliderEntries.Count; slot++)
        {
            var entry = _sliderEntries[_scrollOffset + slot];
            var row = new Control
            {
                Name = $"OwnedRaceSexSlider_{entry.Key}",
                Position = new Vector2(
                    _source.SharedControls.Slider.Rect.Position.X,
                    _source.SharedControls.TopBound +
                        slot * _source.SharedControls.Slider.Rect.Size.Y),
                Size = _source.SharedControls.Slider.Rect.Size,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            _content.AddChild(row);
            var label = NewText(entry.Label, template.Brightness);
            label.Position = new Vector2(template.Label.X, template.Label.Y);
            label.Size = TextSize(entry.Label);
            row.AddChild(label);
            var valueText = entry.Display(entry.Value);
            var value = NewText(valueText, template.Brightness);
            value.Position = new Vector2(
                label.Position.X + label.Size.X + template.Value.LabelGap,
                template.Value.Y);
            value.Size = TextSize(valueText);
            row.AddChild(value);

            row.AddChild(new ColorRect
            {
                Name = template.Bar.Tile,
                Position = new Vector2(template.Bar.X, template.Bar.Y),
                Size = new Vector2(template.Bar.Width, _style.LineThicknessPixels),
                Color = SourceBrightness(template.Brightness),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            var range = entry.Maximum - entry.Minimum;
            if (!float.IsFinite(range) || range <= 0.0f)
                throw new InvalidOperationException(
                    $"Owned RaceSexMenu slider range is invalid: {entry.Key}");
            var normalized = Mathf.Clamp(
                (entry.Value - entry.Minimum) / range,
                template.Marker.Clamp.X,
                template.Marker.Clamp.Y);
            var marker = new Control
            {
                Name = template.Marker.Tile,
                Position = new Vector2(
                    template.Marker.BarX + template.Marker.BarWidth * normalized -
                        template.Marker.Width * OwnedUiTheme.CenteringFactor,
                    template.Marker.Y),
                Size = new Vector2(template.Marker.Width, template.Marker.Height),
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            var markerText = NewText(template.Marker.Glyph, template.Brightness);
            var markerTextSize = TextSize(template.Marker.Glyph);
            markerText.Name = template.Marker.TextTile;
            markerText.Position = new Vector2(
                (template.Marker.Width - markerTextSize.X) *
                    template.Marker.GlyphXMultiplier,
                template.Marker.GlyphY);
            markerText.Size = markerTextSize;
            marker.AddChild(markerText);
            row.AddChild(marker);

            var left = NewSliderArrow(
                template.LeftArrow,
                _sliderLeftLabel,
                template.LeftArrow.XAnchor!.Value,
                rightAnchored: true,
                brightness: template.Brightness);
            var right = NewSliderArrow(
                template.RightArrow,
                _sliderRightLabel,
                template.RightArrow.X!.Value,
                rightAnchored: false,
                brightness: template.Brightness);
            left.Pressed += () =>
            {
                entry.SetValue(Mathf.Max(entry.Minimum, entry.Value - entry.Increment));
            };
            right.Pressed += () =>
            {
                entry.SetValue(Mathf.Min(entry.Maximum, entry.Value + entry.Increment));
            };
            row.AddChild(left);
            row.AddChild(right);
        }
    }

    private Button NewRowButton(string name, float x, float y, Vector2 size)
    {
        var button = new Button
        {
            Name = name,
            Position = new Vector2(x, y),
            Size = size,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        OwnedUiTheme.ApplyButton(button, _font, _systemColor, _style);
        return button;
    }

    private Button NewNavigationButton(OwnedGamebryoRaceSexNavigation source)
    {
        var size = TextSize(source.Text.Text);
        var rect = OwnedGamebryoTileRuntime.NavigationRect(source, size);
        var button = NewRowButton(
            source.Tile,
            rect.Position.X,
            rect.Position.Y,
            rect.Size);
        OwnedGamebryoTileRuntime.ApplyAbsolute(
            button,
            Layout(
                source.Tile,
                rect));
        button.Text = "";
        var empty = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("hover", empty);
        button.AddThemeStyleboxOverride("pressed", empty);
        button.AddThemeStyleboxOverride("focus", empty);
        var label = NewText(
            "",
            source.Brightness);
        OwnedGamebryoTileRuntime.BindText(label, source.Text);
        label.Position = new Vector2(
            source.Buffer.X * OwnedUiTheme.CenteringFactor,
            (rect.Size.Y - size.Y) / source.VerticalCenterDivisor +
                source.BaseTextYOffset + source.TextYAdjust);
        label.Size = size;
        button.AddChild(label);
        return button;
    }

    private OwnedGamebryoTileLayout Layout(string tile, Rect2 rect) => new(
        _source.Document,
        _source.DocumentSha256,
        tile,
        rect,
        OwnedGamebryoTileVisibility.Inherited);

    private TextureButton NewTextureButton(
        OpeningRaceSexScrollTarget source,
        Rect2 rect)
    {
        var texture = LoadTexture(source.Texture);
        var button = new TextureButton
        {
            Name = source.Tile,
            TextureHover = texture,
            TexturePressed = texture,
            Position = rect.Position,
            Size = rect.Size,
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.SelfModulate = SourceBrightness(source.Brightness);
        return button;
    }

    private Button NewSliderArrow(
        OpeningRaceSexSliderArrow source,
        string text,
        float x,
        bool rightAnchored,
        float brightness)
    {
        var textSize = TextSize(text);
        var button = NewRowButton(
            source.Tile,
            rightAnchored ? x - textSize.X : x,
            source.Y,
            new Vector2(textSize.X, source.Height));
        button.Text = text;
        button.Alignment = rightAnchored
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        ApplyButtonTextBrightness(button, brightness);
        return button;
    }

    private Label NewText(string text, float brightness)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", _font.FixedSize);
        label.AddThemeColorOverride(
            "font_color",
            SourceBrightness(brightness));
        return label;
    }

    private void ApplyButtonTextBrightness(Button button, float brightness)
    {
        var color = SourceBrightness(brightness);
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", color);
        button.AddThemeColorOverride("font_focus_color", color);
        button.AddThemeColorOverride("font_pressed_color", color);
    }

    private Color SourceBrightness(float brightness)
    {
        var neutral = _source.Navigation.Next.Brightness;
        if (!float.IsFinite(neutral) || neutral <= 0.0f ||
            !float.IsFinite(brightness) || brightness < 0.0f)
            throw new InvalidOperationException(
                "Owned RaceSexMenu brightness contract is invalid.");
        var scale = brightness / neutral;
        return new Color(
            _systemColor.R * scale,
            _systemColor.G * scale,
            _systemColor.B * scale,
            _systemColor.A);
    }

    private Vector2 TextSize(string text) => new(
        _font.GetStringSize(
            text,
            HorizontalAlignment.Left,
            -1.0f,
            _font.FixedSize).X,
        _font.GetHeight(_font.FixedSize));

    private static Texture2D LoadTexture(OpeningRaceSexTexture source)
    {
        if (source.Texture is null)
            throw new InvalidOperationException(
                "Owned RaceSexMenu texture was not prepared.");
        var texture = OwnedUiTheme.LoadTexture(source.Texture.Path);
        if (source.AtlasContract is not { } atlas)
            return texture;
        return new AtlasTexture
        {
            Atlas = texture,
            Region = new Rect2(
                atlas.UvRect.Position * new Vector2(source.Texture.Size.X, source.Texture.Size.Y),
                atlas.UvRect.Size * new Vector2(source.Texture.Size.X, source.Texture.Size.Y)),
        };
    }
}

internal sealed record OpeningRaceSexListEntry(
    string Key,
    string Label,
    bool Selected,
    bool Selectable,
    Action Activate);

internal sealed record OpeningRaceSexSliderEntry(
    string Key,
    string Label,
    float Value,
    float Minimum,
    float Maximum,
    float Increment,
    float Jump,
    Func<float, string> Display,
    Action<float> SetValue);
