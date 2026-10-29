using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeVatsMenu : Control
{
    internal const uint MenuId = 1056;
    private readonly FalloutVatsSession _session;
    private readonly Func<IReadOnlyList<FalloutVatsSession.Target>> _targets;
    private readonly Func<bool> _preparing;
    private readonly Action<FalloutVatsSession.Target> _queue;
    private readonly Action _execute, _cancel;
    private readonly VBoxContainer _content;
    private readonly ColorRect _backdrop;
    private string? _message;
    private bool _closed;
    private NativeViewportLayout? _layout;

    internal NativeVatsMenu(FalloutVatsSession session, Func<IReadOnlyList<FalloutVatsSession.Target>> targets, Func<bool> preparing,
        Action<FalloutVatsSession.Target> queue, Action execute, Action cancel)
    {
        _session = session; _targets = targets; _preparing = preparing; _queue = queue; _execute = execute; _cancel = cancel;
        Name = "VatsMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backdrop = new ColorRect { Color = new(.01f, .025f, .015f, .70f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 };
        AddChild(_backdrop);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 26);
        AddChild(margin);
        _content = new VBoxContainer(); _content.AddThemeConstantOverride("separation", 12); margin.AddChild(_content);
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var font = NativeBitmapFontAsset.Read(settings, 1).CreateFontFile();
        var sourceColor = FalloutUiSystemColors.Read(settings, "entity_HUDMain");
        var ink = new Color(sourceColor.X, sourceColor.Y, sourceColor.Z);
        Theme = new Theme { DefaultFont = font, DefaultFontSize = font.FixedSize };
        Theme.SetColor("font_color", "Label", ink);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            Theme.SetColor(state, "Button", ink);
        Theme.SetColor("font_disabled_color", "Button", new(ink.R, ink.G, ink.B, .45f));
        Theme.SetStylebox("normal", "Button", new StyleBoxEmpty());
        Theme.SetStylebox("disabled", "Button", new StyleBoxEmpty());
        SetMeta("opennv_menu_id", MenuId);
        SetMeta("opennv_ui_source", "source-VATS-control;source-BPTD-BPNT;source-weapon-AP;shared-weapon-projectile-events");
        SetMeta("opennv_ui_unverified", "matched-pixels;native-cinematic-camera;partial-visibility-sampling;physical-XR");
        try { Refresh(); }
        catch { Free(); throw; }
    }

    private static Button Button(string name, string text, Action callback)
    {
        var button = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.All, CustomMinimumSize = new(0, 42), Alignment = HorizontalAlignment.Left };
        button.Pressed += callback; return button;
    }

    internal void Refresh()
    {
        foreach (var child in _content.GetChildren()) { _content.RemoveChild(child); child.QueueFree(); }
        var title = new Label
        {
            Text = $"V.A.T.S.    AP {_session.AvailableActionPoints - _session.ReservedActionPoints:0.##} / {_session.AvailableActionPoints:0.##}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28); _content.AddChild(title);
        if (_session.CurrentPhase == FalloutVatsSession.Phase.Selecting)
        {
            _content.AddChild(new Label
            {
                Text = "Choose an actor's body part to queue an attack. Tab / arrows move focus; Enter selects.\n" +
                    "Back removes the last attack; Attack fires; Esc cancels.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
            var lists = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; lists.AddThemeConstantOverride("separation", 24); _content.AddChild(lists);
            var targetScroll = new ScrollContainer { FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            lists.AddChild(targetScroll);
            var targets = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; targetScroll.AddChild(targets);
            var choices = _targets();
            foreach (var actor in choices.GroupBy(target => (target.Reference, target.ActorName)))
            {
                targets.AddChild(new Label { Text = actor.Key.ActorName });
                foreach (var part in actor)
                {
                    var button = Button($"VatsTarget_{part.Reference.ObjectId:x6}_{part.Part}",
                        $"{part.PartName}    {part.Chance:0}%    {part.ActionPointCost:0.##} AP" + (part.Visible ? "" : "  [Obstructed]"), () => Run(() => _queue(part)));
                    button.Disabled = !part.Visible || part.Chance <= 0 || part.ActionPointCost + _session.ReservedActionPoints > _session.AvailableActionPoints;
                    button.SetMeta("opennv_vats_reference", part.Reference.ToString()); button.SetMeta("opennv_vats_part", part.Part);
                    targets.AddChild(button);
                }
            }
            if (choices.Count == 0) targets.AddChild(new Label { Text = _preparing() ? "Scanning targets…" : "No targets in range." });
            var queueScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(450, 0) };
            lists.AddChild(queueScroll);
            var queued = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; queueScroll.AddChild(queued);
            queued.AddChild(new Label { Text = "ATTACKS" });
            foreach (var attack in _session.Queue)
                queued.AddChild(new Label
                {
                    Text = $"{attack.Ordinal}. {attack.Target.ActorName} — {attack.Target.PartName}\n{attack.Target.Chance:0}% / {attack.Target.ActionPointCost:0.##} AP",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                });
            var actions = new HBoxContainer(); _content.AddChild(actions);
            actions.AddChild(Button("VatsRemoveAttack", "Back — remove last", () => Run(_session.RemoveLast)));
            var execute = Button("VatsExecute", "Attack", () => Run(_execute));
            execute.Disabled = _session.Queue.Count == 0; actions.AddChild(execute);
        }
        else
        {
            _backdrop.Color = new(0, 0, 0, 0);
            MouseFilter = MouseFilterEnum.Ignore;
            _content.AddChild(new Label
            {
                Text = _session.CurrentPhase == FalloutVatsSession.Phase.Executing ?
                    $"Executing attack {_session.CompletedAttacks + 1} / {_session.Queue.Count}" :
                    _session.CurrentPhase == FalloutVatsSession.Phase.Faulted ? "VATS stopped: " + _session.Error : "VATS complete.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
        }
        if (_message is not null) _content.AddChild(new Label { Text = _message, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var cancel = Button("VatsCancel", _session.CurrentPhase == FalloutVatsSession.Phase.Selecting ? "Cancel [Esc]" : "Stop queued attacks [Esc]", Cancel);
        _content.AddChild(cancel);
        if (IsInsideTree()) Callable.From(() => { if (IsInstanceValid(cancel) && cancel.IsInsideTree() && GetViewport().GuiGetFocusOwner() is null) cancel.GrabFocus(); }).CallDeferred();
    }

    private void Run(Action callback)
    {
        try { callback(); _message = null; Refresh(); }
        catch (Exception error) { _message = error.Message; Refresh(); }
    }
    private void Cancel()
    {
        if (_closed) return;
        _closed = true; _cancel();
    }
    public override void _EnterTree() => _layout = new(this, Layout);
    public override void _Ready() { Layout(); _content.GetChildren().OfType<Button>().LastOrDefault()?.GrabFocus(); }
    public override void _ExitTree()
    {
        _layout?.Dispose();
        Callable.From(() => { if (!IsInstanceValid(this) || !IsInsideTree()) Cancel(); }).CallDeferred();
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var scale = GetViewportRect().Size.Y / 900;
        Scale = Vector2.One * scale; Size = GetViewportRect().Size / scale;
    }
    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Cancel(); GetViewport().SetInputAsHandled(); }
    }
}
