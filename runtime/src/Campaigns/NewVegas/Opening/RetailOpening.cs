using Godot;


using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RetailOpening : CanvasLayer
{
    private const string ContinueAction = "continue";
    private const string LoadAction = "load";
    private const string NewGameAction = "new-game";
    private const string QuitAction = "quit";

    private OpeningManifest _manifest = null!;
    private Control _viewport = null!;
    private Control _canvas = null!;
    private AudioStreamPlayer _music = null!;
    private VideoStreamPlayer? _video;
    private Func<Task>? _introFinished;
    private Func<string, Task>? _menuActionRequested;
    private string _cancelAction = "";
    private bool _introCompleted;
    private bool _transitionStarted;
    private readonly Dictionary<string, Button> _buttonsByAction =
        new(StringComparer.Ordinal);

    internal void Configure(
        OpeningManifest manifest,
        bool hasSave,
        string cancelAction,
        Func<Task> introFinished,
        Func<string, Task> menuActionRequested)
    {
        _manifest = manifest;
        _introFinished = introFinished;
        _menuActionRequested = menuActionRequested;
        _cancelAction = cancelAction;
        Name = "RetailOpening";

        _viewport = new Control { Name = "Viewport" };
        _viewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _viewport.Resized += ScaleReferenceCanvas;
        AddChild(_viewport);

        var letterbox = new ColorRect
        {
            Name = "Letterbox",
            Color = Colors.Black,
        };
        letterbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        letterbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _viewport.AddChild(letterbox);

        _canvas = new Control
        {
            Name = "RetailCanvas",
            Size = manifest.CanvasSize,
        };
        _viewport.AddChild(_canvas);

        var background = new TextureRect
        {
            Name = "MainMenuBackground",
            Position = Vector2.Zero,
            Size = manifest.CanvasSize,
            Texture = OwnedUiTheme.LoadTexture(manifest.BackgroundTexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(background);

        var title = new TextureRect
        {
            Name = "MainMenuTitle",
            Position = manifest.TitleRect.Position,
            Size = manifest.TitleRect.Size,
            Texture = OwnedUiTheme.LoadTexture(manifest.TitleTexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = manifest.MainMenuColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(title);

        var font = OwnedUiTheme.BuildFont(manifest.Font);
        Button? initialFocus = null;
        foreach (var authored in manifest.Buttons)
        {
            var button = BuildButton(authored, font);
            if (!hasSave && authored.Action is ContinueAction or LoadAction)
                button.Disabled = true;
            button.Pressed += () => _ = Dispatch(authored.Action);
            _canvas.AddChild(button);
            if (!_buttonsByAction.TryAdd(authored.Action, button))
                throw new InvalidOperationException(
                    $"Owned main menu duplicates action: {authored.Action}");
            if (initialFocus is null && !button.Disabled)
                initialFocus = button;
            if (authored.Action == NewGameAction && !hasSave)
                initialFocus = button;
        }

        _music = new AudioStreamPlayer
        {
            Name = "MainTitleMusic",
            Stream = AudioStreamMP3.LoadFromFile(manifest.MainMenuMusicPath),
            VolumeLinear = manifest.MainMenuMusicVolume,
        };
        if (_music.Stream is AudioStreamMP3 mp3)
            mp3.Loop = true;
        AddChild(_music);
        _music.Play();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ScaleReferenceCanvas();
        if (initialFocus is not null)
            Callable.From(initialFocus.GrabFocus).CallDeferred();
    }

    internal void PressActionForAcceptance(string action)
    {
        if (!_buttonsByAction.TryGetValue(action, out var button) || button.Disabled)
            throw new InvalidOperationException(
                $"Owned main-menu action is unavailable: {action}");
        button.EmitSignal(Button.SignalName.Pressed);
    }

    public override void _Input(InputEvent @event)
    {
        if (_video is null)
            return;
        var configuredCancel = !string.IsNullOrWhiteSpace(_cancelAction) &&
            @event.IsActionPressed(_cancelAction);
        var escape = @event is InputEventKey key &&
            key.Pressed &&
            !key.Echo &&
            (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape);
        if (!configuredCancel && !escape)
            return;
        GetViewport().SetInputAsHandled();
        _ = CompleteIntro();
    }

    private async Task Dispatch(string action)
    {
        if (_transitionStarted)
            return;
        if (action == NewGameAction)
        {
            PlayIntro();
            return;
        }
        if (action == QuitAction)
        {
            GetTree().Quit();
            return;
        }
        if (action is not (ContinueAction or LoadAction))
        {
            if (_menuActionRequested is not null)
                await _menuActionRequested(action);
            return;
        }
        _transitionStarted = true;
        SetButtonsDisabled();
        try
        {
            if (_menuActionRequested is not null)
                await _menuActionRequested(action);
            QueueFree();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_OWNED_MENU_TRANSITION_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void PlayIntro()
    {
        if (_video is not null)
            return;
        _music.Stop();
        _canvas.Visible = false;
        _video = new VideoStreamPlayer
        {
            Name = "FNVIntro",
            Stream = new VideoStreamTheora { File = _manifest.IntroVideoPath },
            Expand = true,
            Loop = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => _ = CompleteIntro();
        _viewport.AddChild(_video);
        _video.Play();
    }

    private async Task CompleteIntro()
    {
        if (_introCompleted || _transitionStarted)
            return;
        _introCompleted = true;
        _transitionStarted = true;
        _video?.Stop();
        _music.Stop();
        try
        {
            if (_introFinished is not null)
                await _introFinished();
            QueueFree();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_OWNED_INTRO_TRANSITION_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void SetButtonsDisabled()
    {
        foreach (var button in _buttonsByAction.Values)
            button.Disabled = true;
    }

    private void ScaleReferenceCanvas()
    {
        if (_canvas is null || _manifest is null)
            return;
        var viewportSize = _viewport.Size;
        var scale = Mathf.Min(
            viewportSize.X / _manifest.CanvasSize.X,
            viewportSize.Y / _manifest.CanvasSize.Y);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position =
            (viewportSize - _manifest.CanvasSize * scale) * OwnedUiTheme.CenteringFactor;
    }

    private Button BuildButton(OpeningMenuButton authored, FontFile font)
    {
        var button = new Button
        {
            Name = authored.Tile,
            Text = authored.Label,
            Position = authored.Rect.Position,
            Size = authored.Rect.Size,
            Flat = false,
            Alignment = HorizontalAlignment.Center,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        OwnedUiTheme.ApplyButton(
            button,
            font,
            _manifest.MainMenuColor,
            _manifest.Style);
        return button;
    }
}
