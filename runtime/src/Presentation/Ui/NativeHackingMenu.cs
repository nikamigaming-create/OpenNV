using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// First-party input/presentation for an already prepared source transaction.
// Admission, player input leasing and world consequences belong to the caller.
internal sealed partial class NativeHackingMenu : Control
{
    private const int Columns = 2, Rows = 17, Characters = 12;
    private readonly FalloutHackingTransaction _session;
    private readonly Action<FalloutHackingOutcome> _selected;
    private readonly Action _cancelled;
    private readonly Action<Exception> _failed;
    private readonly Button[] _cells = new Button[Columns * Rows * Characters];
    private readonly List<(Button Button, Action Handler)> _connections = [];
    private readonly Label _attempts, _status;
    private readonly Button _cancel;
    private bool _busy, _stopped, _leaving, _failureReported;
    private string _message = "Choose a word or a matching bracket pair.";
    internal string? Error { get; private set; }

    internal NativeHackingMenu(FalloutHackingTransaction session, Action<FalloutHackingOutcome> selected,
        Action cancelled, Action<Exception> failed)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(selected);
            ArgumentNullException.ThrowIfNull(cancelled);
            ArgumentNullException.ThrowIfNull(failed);
            _session = session; _selected = selected; _cancelled = cancelled; _failed = failed;
            Name = "HackingMenu";
            ProcessMode = ProcessModeEnum.Always;
            MouseFilter = MouseFilterEnum.Stop;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            Theme = new Theme
            {
                DefaultFont = new SystemFont { FontNames = ["Consolas", "DejaVu Sans Mono", "Liberation Mono"] },
                DefaultFontSize = 18
            };
            var ink = new Color(.35f, 1, .55f);
            AddThemeColorOverride("font_color", ink);
            var background = new ColorRect { Color = new(0, .025f, .01f, .98f), MouseFilter = MouseFilterEnum.Ignore };
            background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
            var margin = new MarginContainer();
            margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
            AddChild(margin);
            var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); margin.AddChild(content);
            var title = new Label { Text = "TERMINAL ACCESS" }; title.AddThemeFontSizeOverride("font_size", 24); content.AddChild(title);
            content.AddChild(new Label
            {
                Text = "Click a character to select its word or bracket pair.\nArrow keys / Tab move focus; Enter / Space select; Esc exits.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
            _attempts = new Label(); content.AddChild(_attempts);
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, FollowFocus = true };
            content.AddChild(scroll);
            var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            columns.AddThemeConstantOverride("separation", 32); scroll.AddChild(columns);
            var normal = Style(new(0, 0, 0, 0));
            var hover = Style(new(.08f, .24f, .12f));
            var pressed = Style(new(.16f, .35f, .2f));
            var focus = Style(new(0, 0, 0, 0));
            focus.BorderColor = ink;
            focus.SetBorderWidthAll(1);
            for (var column = 0; column < Columns; ++column)
            {
                var grid = new GridContainer { Name = "Column" + column, Columns = Characters };
                grid.AddThemeConstantOverride("h_separation", 0); grid.AddThemeConstantOverride("v_separation", 0);
                columns.AddChild(grid);
                for (var row = 0; row < Rows; ++row)
                    for (var character = 0; character < Characters; ++character)
                    {
                        var selectedColumn = column; var selectedRow = row; var selectedCharacter = character;
                        var button = new Button
                        {
                            Name = $"Character_{row}_{character}",
                            CustomMinimumSize = new(20, 24),
                            FocusMode = FocusModeEnum.All
                        };
                        foreach (var state in new[] { "normal", "disabled" }) button.AddThemeStyleboxOverride(state, normal);
                        button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", pressed);
                        button.AddThemeStyleboxOverride("focus", focus);
                        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color" })
                            button.AddThemeColorOverride(state, ink);
                        _cells[Index(column, row, character)] = button;
                        BindPressed(button, () => Select(selectedColumn, selectedRow, selectedCharacter));
                        grid.AddChild(button);
                    }
            }
            _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; content.AddChild(_status);
            _cancel = new Button { Name = "Cancel", Text = "Exit [Esc]", FocusMode = FocusModeEnum.All };
            BindPressed(_cancel, Cancel); content.AddChild(_cancel);
            Refresh();
            if (_session.Stage != FalloutHackingStage.Choosing) StopInteraction();
        }
        catch { DisconnectButtons(); Free(); throw; }
    }

    private static StyleBoxFlat Style(Color color) => new()
    {
        BgColor = color,
        ContentMarginLeft = 0,
        ContentMarginRight = 0,
        ContentMarginTop = 0,
        ContentMarginBottom = 0
    };

    private static int Index(int column, int row, int character) => (column * Rows + row) * Characters + character;

    private void BindPressed(Button button, Action handler)
    {
        button.Pressed += handler; _connections.Add((button, handler));
    }

    private void DisconnectButtons()
    {
        foreach (var (button, handler) in _connections)
            if (GodotObject.IsInstanceValid(button)) button.Pressed -= handler;
        _connections.Clear();
    }

    public override void _Ready()
    {
        for (var column = 0; column < Columns; ++column)
            for (var row = 0; row < Rows; ++row)
                for (var character = 0; character < Characters; ++character)
                {
                    var index = Index(column, row, character); var button = _cells[index];
                    button.FocusNeighborTop = _cells[Index(column, Math.Max(0, row - 1), character)].GetPath();
                    button.FocusNeighborBottom = row + 1 < Rows ? _cells[Index(column, row + 1, character)].GetPath() : _cancel.GetPath();
                    button.FocusNeighborLeft = _cells[character > 0 ? index - 1 : column > 0 ? Index(column - 1, row, Characters - 1) : index].GetPath();
                    button.FocusNeighborRight = _cells[character + 1 < Characters ? index + 1 : column + 1 < Columns ? Index(column + 1, row, 0) : index].GetPath();
                    button.FocusNext = index + 1 < _cells.Length ? _cells[index + 1].GetPath() : _cancel.GetPath();
                    button.FocusPrevious = index > 0 ? _cells[index - 1].GetPath() : _cancel.GetPath();
                }
        _cancel.FocusNext = _cells[0].GetPath(); _cancel.FocusPrevious = _cells[^1].GetPath();
        _cancel.FocusNeighborTop = _cells[^1].GetPath(); _cancel.FocusNeighborBottom = _cancel.GetPath();
        _cancel.FocusNeighborLeft = _cancel.GetPath(); _cancel.FocusNeighborRight = _cancel.GetPath();
        if (_stopped) StopInteraction();
        else _cells[0].GrabFocus();
    }

    private bool CanInteract => !_busy && !_stopped && !_leaving && _session.Stage == FalloutHackingStage.Choosing;

    private void Select(int column, int row, int character)
    {
        if (!CanInteract) return;
        _busy = true;
        try
        {
            // The transaction resolves words anew, including spans across rows.
            var outcome = _session.SelectDisplay(column, row, character + 7);
            _message = outcome.Selection switch
            {
                FalloutHackingSelection.WrongWord => $"Incorrect password. Likeness: {outcome.Likeness}.",
                FalloutHackingSelection.CorrectWord => "Password accepted.",
                FalloutHackingSelection.DudRemoved => "Dud removed.",
                FalloutHackingSelection.AttemptsReset => "Attempts restored.",
                _ => "No selection at this position."
            };
            if (_session.Stage != FalloutHackingStage.Choosing) StopInteraction();
            Refresh();
            _selected(outcome);
        }
        catch (Exception error) { Fail(error); }
        finally
        {
            _busy = false;
            if (_leaving && !_stopped) CancelCore(refresh: false);
        }
    }

    private void Cancel()
    {
        if (CanInteract) CancelCore(refresh: true);
    }

    private void CancelCore(bool refresh)
    {
        if (_stopped) return;
        StopInteraction();
        if (_session.Stage != FalloutHackingStage.Choosing) return;
        try
        {
            _session.Cancel();
            if (refresh) Refresh();
            _cancelled();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Refresh()
    {
        var display = _session.DisplayBytes();
        if (display.Length != _cells.Length) throw new InvalidDataException("Hacking display must contain two columns of seventeen twelve-byte rows.");
        // Keep one byte per cell; never normalize, trim or reflow the payload.
        for (var index = 0; index < display.Length; ++index)
            _cells[index].Text = ((char)display[index]).ToString();
        _attempts.Text = $"Attempts remaining: {_session.RemainingAttempts} / {_session.MaximumAttempts}";
        _status.Text = Error is not null ? "Terminal interaction stopped." : _session.Stage switch
        {
            FalloutHackingStage.Succeeded => "Password accepted.",
            FalloutHackingStage.LockedOut => "No attempts remaining.",
            FalloutHackingStage.Cancelled => "Hacking cancelled.",
            FalloutHackingStage.Faulted => "Terminal interaction stopped.",
            _ => _message
        };
    }

    private void StopInteraction()
    {
        _stopped = true;
        if (!GodotObject.IsInstanceValid(this)) return;
        SetProcess(false); SetProcessInput(false);
        foreach (var button in _cells)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = true;
        if (GodotObject.IsInstanceValid(_cancel)) _cancel.Disabled = true;
    }

    private void Fail(Exception error)
    {
        if (_failureReported) return;
        _failureReported = true; Error = error.Message;
        StopInteraction();
        if (GodotObject.IsInstanceValid(_status)) _status.Text = "Terminal interaction stopped.";
        try { _failed(error); }
        catch (Exception notificationError) { GD.PushError($"OPENNV_HACKING_FAILURE_CALLBACK {notificationError.Message}"); }
    }

    public override void _Input(InputEvent input)
    {
        if (!CanInteract || input is not InputEventKey { Pressed: true, Echo: false } key ||
            key.PhysicalKeycode != Key.Escape && key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled(); Cancel();
    }

    public override void _Process(double delta)
    {
        if (_stopped || _session.Stage == FalloutHackingStage.Choosing) return;
        try { StopInteraction(); Refresh(); }
        catch (Exception error) { Fail(error); }
    }

    public override void _ExitTree()
    {
        _leaving = true; DisconnectButtons();
        if (!_busy) CancelCore(refresh: false);
    }
}
