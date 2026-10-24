using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private static readonly string[] InstrumentControls =
    [
        "CONTROL_FunctionKey_0", "CONTROL_MainTuning_pivotY", "CONTROL_FunctionKey_1",
        "CONTROL_FunctionKey_2", "CONTROL_Range_pivotY", "CONTROL_Toggle_0_pivotX",
        "CONTROL_Toggle_1_pivotX", "CONTROL_FunctionKey_3",
    ];
    private InstrumentReadout? _instrumentReadout;
    private string _instrumentFocus = InstrumentControls[0];
    private string _instrumentLastToast = string.Empty;
    private string _instrumentLastGame = string.Empty;
    private bool _instrumentConfiguration;
    private float _instrumentBrightness = .85f;
    private int _instrumentRouteCursor;
    private int _instrumentFeedbackPage;
    private string _instrumentFeedback = string.Empty;

    internal bool InstrumentPresentationConstructed => DevicePresentationConstructed && _instrumentReadout is not null;
    internal string InstrumentSelectedGame => LaunchCampaign().Id;

    private void BuildInstrumentReadout(SubViewport canvas)
    {
        _instrumentReadout = new InstrumentReadout
        {
            Name = "InstrumentReadout",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        canvas.AddChild(_instrumentReadout);
        _instrumentReadout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    // Refresh consumes the existing semantic owners' results. It does not probe
    // installations, resolve mod stacks, or synthesize readiness every frame.
    private void RefreshInstrument()
    {
        if (_instrumentReadout is null || _launch is null) return;
        var campaign = LaunchCampaign();
        var games = InstrumentGames();
        var index = Array.FindIndex(games, item => item.Id == campaign.Id);
        if (_instrumentLastGame != campaign.Id)
        {
            _instrumentLastGame = campaign.Id;
            _instrumentRouteCursor = Array.IndexOf(InstrumentModes(campaign), _selectedPresentation);
            _instrumentFeedbackPage = 0;
        }
        _instrumentLastToast = _toast.Text;
        var feedback = _toast.Text.Length > 0 ? _toast.Text : _stackStatus.Text;
        if (_instrumentConfiguration && _toast.Text.Length == 0)
        {
            var source = _profiles.TryGet(campaign.Id, out var profile) ? profile.InstallRoot : "No source folder selected.";
            feedback = "SOURCE: " + source + "\nCONFIG cycles presentation. Hold CONFIG to choose source. RETURN closes configuration.";
        }
        if (_instrumentFeedback != feedback) { _instrumentFeedback = feedback; _instrumentFeedbackPage = 0; }
        var bus = AudioServer.GetBusIndex("Master");
        var audio = bus < 0 ? "UNAVAILABLE" : AudioServer.IsBusMute(bus) ? "OFF" : "ON";
        var classification = campaign.Presentations.TryGetValue(_selectedPresentation, out var route) && route.PreviewOnly
            ? "SOURCE PREVIEW / CAMPAIGN INCOMPLETE" : "EXPERIMENTAL / GAMEPLAY INCOMPLETE";
        _instrumentReadout.SetState(new InstrumentState(
            campaign.Title.ToUpperInvariant(), index + 1, _instrumentConfiguration,
            _launching ? "LAUNCH REQUESTED" : _launch.Disabled ? "NOT READY TO LOAD" : "READY TO LOAD",
            RouteLabel(_selectedPresentation).ToUpperInvariant(), classification,
            feedback, _instrumentFeedbackPage, audio, _deviceReadScreen, _instrumentBrightness,
            InstrumentControlLabel(_instrumentFocus)));
        _instrumentReadout.Modulate = new Color(_instrumentBrightness, _instrumentBrightness, _instrumentBrightness);
        SyncInstrumentHardware(index, bus >= 0 && !AudioServer.IsBusMute(bus));
    }

    private NativeGodotLauncherCampaign[] InstrumentGames() => CampaignIds
        .Where(FallbackCampaigns.ContainsKey)
        .Select(id => _campaigns.FirstOrDefault(campaign => campaign.Id == id))
        .OfType<NativeGodotLauncherCampaign>().ToArray();

    private static string[] InstrumentModes(NativeGodotLauncherCampaign campaign) => campaign.Id is "fallout1" or "fallout2"
        ? ["hex-tactical", "first-person", "openxr"] : ["first-person", "openxr"];

    private void ActivateInstrumentControl(string name, int direction)
    {
        SetInstrumentFocus(name);
        switch (name)
        {
            case "CONTROL_MainTuning_pivotY":
                var games = InstrumentGames();
                if (games.Length == 0) { ShowToast("No game definitions are available."); break; }
                var current = Array.FindIndex(games, campaign => campaign.Id == _selectedGameId);
                SelectLauncherCampaign(games[(current + Math.Sign(direction) + games.Length) % games.Length]);
                break;
            case "CONTROL_Range_pivotY":
                _instrumentBrightness = Math.Clamp(_instrumentBrightness + Math.Sign(direction) * .05f, .35f, 1f);
                break;
            case "CONTROL_FunctionKey_0":
                // Same guarded service as the accessible launcher; no signal on
                // a hidden button and no invented ready/launch state.
                LaunchSelected();
                break;
            case "CONTROL_FunctionKey_1":
                if (direction == 0) { ChooseInstrumentSource(); break; }
                if (!_instrumentConfiguration)
                {
                    _instrumentConfiguration = true;
                    _instrumentRouteCursor = Array.IndexOf(InstrumentModes(LaunchCampaign()), _selectedPresentation);
                    _toast.Text = string.Empty;
                }
                else
                {
                    var modes = InstrumentModes(LaunchCampaign());
                    _instrumentRouteCursor = (_instrumentRouteCursor + 1) % modes.Length;
                    SelectLauncherPresentation(modes[_instrumentRouteCursor]);
                }
                break;
            case "CONTROL_FunctionKey_2":
                if (_fileDialog is not null) CloseFileDialog();
                else if (_stackWindow is not null) CloseStackWindow();
                else { _instrumentConfiguration = false; _instrumentFeedbackPage = 0; _toast.Text = string.Empty; }
                break;
            case "CONTROL_FunctionKey_3":
                GetTree().Quit();
                return;
            case "CONTROL_Toggle_0_pivotX":
                var bus = AudioServer.GetBusIndex("Master");
                if (bus < 0) ShowToast("Audio switch unavailable: the Master audio bus is missing.");
                else AudioServer.SetBusMute(bus, !AudioServer.IsBusMute(bus));
                break;
            case "CONTROL_Toggle_1_pivotX":
                _deviceReadScreen = !_deviceReadScreen;
                ResizeDevice();
                break;
            default:
                ShowToast("Unbound physical instrument control: " + name);
                break;
        }
        RefreshInstrument();
    }

    private void ChooseInstrumentSource()
    {
        _instrumentConfiguration = true;
        // F2 may have left a mod detail selected. The hardware source picker
        // always belongs to the game shown on this instrument.
        _selectedId = _selectedGameId;
        ChooseInstallation();
    }

    private void SetInstrumentFocus(string name)
    {
        if (_instrumentFocus == name || !InstrumentControls.Contains(name)) return;
        _instrumentFocus = name;
        RefreshInstrument();
    }

    private void PageInstrumentFeedback(int direction)
    {
        if (_instrumentReadout is null) return;
        _instrumentFeedbackPage = Math.Clamp(_instrumentFeedbackPage + direction, 0, _instrumentReadout.LastFeedbackPage);
        RefreshInstrument();
    }

    private bool HandleInstrumentInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Left: ActivateInstrumentControl("CONTROL_MainTuning_pivotY", -1); break;
                case Key.Right: ActivateInstrumentControl("CONTROL_MainTuning_pivotY", 1); break;
                case Key.Up: ActivateInstrumentControl("CONTROL_Range_pivotY", 1); break;
                case Key.Down: ActivateInstrumentControl("CONTROL_Range_pivotY", -1); break;
                case Key.Enter: case Key.KpEnter: case Key.F1: ActivateInstrumentControl("CONTROL_FunctionKey_0", 1); break;
                case Key.C: ActivateInstrumentControl("CONTROL_FunctionKey_1", 1); break;
                case Key.Escape: ActivateInstrumentControl("CONTROL_FunctionKey_2", 1); break;
                case Key.F3: ActivateInstrumentControl("CONTROL_Toggle_1_pivotX", 1); break;
                case Key.M: ActivateInstrumentControl("CONTROL_Toggle_0_pivotX", 1); break;
                case Key.F4: ChooseInstrumentSource(); RefreshInstrument(); break;
                case Key.Pageup: PageInstrumentFeedback(-1); break;
                case Key.Pagedown: PageInstrumentFeedback(1); break;
                case Key.Tab:
                    var index = Array.IndexOf(InstrumentControls, _instrumentFocus);
                    SetInstrumentFocus(InstrumentControls[(index + (key.ShiftPressed ? -1 : 1) + InstrumentControls.Length) % InstrumentControls.Length]);
                    break;
                case Key.Space: ActivateInstrumentControl(_instrumentFocus, key.ShiftPressed ? -1 : 1); break;
                default: return false;
            }
            return true;
        }
        if (input is InputEventJoypadButton { Pressed: true } button)
        {
            switch (button.ButtonIndex)
            {
                case JoyButton.DpadLeft: ActivateInstrumentControl("CONTROL_MainTuning_pivotY", -1); break;
                case JoyButton.DpadRight: ActivateInstrumentControl("CONTROL_MainTuning_pivotY", 1); break;
                case JoyButton.DpadUp: ActivateInstrumentControl("CONTROL_Range_pivotY", 1); break;
                case JoyButton.DpadDown: ActivateInstrumentControl("CONTROL_Range_pivotY", -1); break;
                case JoyButton.A: ActivateInstrumentControl("CONTROL_FunctionKey_0", 1); break;
                case JoyButton.B: ActivateInstrumentControl("CONTROL_FunctionKey_2", 1); break;
                case JoyButton.X: ActivateInstrumentControl("CONTROL_FunctionKey_1", 1); break;
                case JoyButton.Y: ChooseInstrumentSource(); RefreshInstrument(); break;
                case JoyButton.LeftShoulder: PageInstrumentFeedback(-1); break;
                case JoyButton.RightShoulder: PageInstrumentFeedback(1); break;
                case JoyButton.LeftStick: ActivateInstrumentControl("CONTROL_Toggle_0_pivotX", 1); break;
                case JoyButton.RightStick: ActivateInstrumentControl("CONTROL_Toggle_1_pivotX", 1); break;
                case JoyButton.Back: ToggleDeviceStandardView(); break;
                default: return false;
            }
            return true;
        }
        return false;
    }

    private void SyncInstrumentHardware(int selectedGame, bool audioEnabled)
    {
        PoseDeviceInstruments(_instrumentBrightness, audioEnabled, _deviceReadScreen, selectedGame >= 0 && !_launching && !_launch.Disabled);
    }

    public override void _Process(double delta)
    {
        if (_instrumentReadout is null || _deviceStandardView) return;
        // Async launch/folder failures may set the existing toast directly.
        if (_toast.Text != _instrumentLastToast) RefreshInstrument();
        _instrumentReadout.AdvanceTrace(delta);
    }

    private static string InstrumentControlLabel(string name) => name switch
    {
        "CONTROL_MainTuning_pivotY" => "SELECT GAME / LEFT - RIGHT",
        "CONTROL_Range_pivotY" => "BRIGHTNESS / UP - DOWN",
        "CONTROL_FunctionKey_0" => "LOAD / ENTER",
        "CONTROL_FunctionKey_1" => "CONFIG / C   HOLD: SOURCE / F4",
        "CONTROL_FunctionKey_2" => "RETURN / ESC",
        "CONTROL_FunctionKey_3" => "POWER / QUIT",
        "CONTROL_Toggle_0_pivotX" => "AUDIO / M",
        "CONTROL_Toggle_1_pivotX" => "READ VIEW / F3",
        _ => name,
    };

    private sealed record InstrumentState(string Title, int Cartridge, bool Configuration, string Readiness,
        string Route, string Classification, string Feedback, int FeedbackPage, string Audio,
        bool ReadView, float Brightness, string Focus);

    // A purpose-built output surface, with no buttons, browser controls, cards,
    // search boxes or hidden interactive layers. Hardware owns every action.
    private sealed partial class InstrumentReadout : Control
    {
        private readonly Font _font = new SystemFont { FontNames = ["Consolas", "DejaVu Sans Mono", "Liberation Mono"] };
        private readonly Color _phosphor = new("b6f6a7");
        private readonly Color _dim = new("6cad70");
        private InstrumentState? _state;
        private string[] _feedbackLines = [];
        private double _tracePhase;
        internal int LastFeedbackPage => Math.Max(0, (_feedbackLines.Length - 1) / 5);

        internal void SetState(InstrumentState state)
        {
            if (_state?.Feedback != state.Feedback) _feedbackLines = Wrap(state.Feedback, 920, 32);
            _state = state;
            QueueRedraw();
        }

        internal void AdvanceTrace(double delta)
        {
            _tracePhase = (_tracePhase + delta * .22) % Math.Tau;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_state is not { } state) return;
            Text("OPENNV", 145, 140, _phosphor);
            Text(state.Configuration ? "INSTRUMENT CONFIGURATION" : $"CARTRIDGE {state.Cartridge:00}", 190, 34, _dim);
            Text(state.Title, 266, Fit(state.Title, 76, 960), _phosphor);
            Text(state.Readiness, 336, 42, _phosphor);
            Text(state.Route, 381, 29, _dim);
            var page = Math.Min(state.FeedbackPage, LastFeedbackPage);
            var lines = _feedbackLines.Skip(page * 5).Take(5).ToArray();
            for (var i = 0; i < lines.Length; i++) Text(lines[i], 438 + i * 36, 32, _phosphor);
            if (LastFeedbackPage > 0) Text($"MESSAGE {page + 1}/{LastFeedbackPage + 1}  PGUP / PGDN / WHEEL", 633, 25, _dim);
            DrawLine(new Vector2(132, 658), new Vector2(1060, 658), _dim, 1.5f, true);
            // Decorative phosphor trace, deliberately not a readiness probe,
            // loading meter, waveform of audio, or fabricated telemetry.
            var points = new Vector2[97];
            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector2(132 + i * 9.66f, 691 + 12 * MathF.Sin(i * .24f + (float)_tracePhase));
            DrawPolyline(points, _dim, 1.5f, true);
            Text(state.Classification, 742, 25, _dim);
            Text($"AUDIO {state.Audio}   LIGHT {state.Brightness * 100:0}%   VIEW {(state.ReadView ? "READ" : "DEVICE")}", 779, 25, _dim);
            Text(state.Focus, 820, 25, _phosphor);
        }

        private void Text(string value, float y, int size, Color color) =>
            DrawString(_font, new Vector2(132, y), value, HorizontalAlignment.Left, -1, size, color);

        private int Fit(string text, int maximum, float width)
        {
            while (maximum > 28 && _font.GetStringSize(text, HorizontalAlignment.Left, -1, maximum).X > width) maximum--;
            return maximum;
        }

        private string[] Wrap(string message, float width, int size)
        {
            var lines = new List<string>();
            foreach (var paragraph in message.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
            {
                var line = string.Empty;
                foreach (var character in paragraph)
                {
                    if (_font.GetStringSize(line + character, HorizontalAlignment.Left, -1, size).X > width && line.Length > 0)
                    {
                        lines.Add(line); line = string.Empty;
                    }
                    line += character;
                }
                lines.Add(line);
            }
            return lines.ToArray();
        }

        public override void _ExitTree() => _font.Dispose();
    }
}
