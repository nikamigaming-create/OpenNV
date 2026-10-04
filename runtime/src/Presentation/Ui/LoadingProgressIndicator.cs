using System.Globalization;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

// OpenNV feedback supplements the owned loading artwork; it makes no claim to
// reproduce the retail LoadingMenu's ancillary progress or animation widgets.
internal sealed partial class LoadingProgressIndicator : HBoxContainer
{
    private readonly Label _status;
    private readonly Label _elapsed;
    private readonly Spinner _spinner;
    private readonly ulong _started = Time.GetTicksMsec();
    private ulong _phaseStarted = Time.GetTicksMsec();
    private bool _failed;
    internal object State => new
    {
        phase = _status.Text,
        elapsedMilliseconds = Time.GetTicksMsec() - _started,
        phaseMilliseconds = Time.GetTicksMsec() - _phaseStarted,
        failed = _failed,
        feedback = "OpenNV-spinner-and-phase; retail-ancillary-ui-unbound",
    };

    internal LoadingProgressIndicator(string status)
    {
        Name = "LoadingProgress";
        ProcessMode = ProcessModeEnum.Always;
        Alignment = AlignmentMode.Center;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 16);
        _spinner = new Spinner { CustomMinimumSize = new Vector2(32, 32), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        AddChild(_spinner);
        var text = new VBoxContainer();
        AddChild(text);
        _status = new Label { Text = status };
        _status.AddThemeColorOverride("font_color", new Color(0.66f, 0.95f, 0.50f));
        _status.AddThemeFontSizeOverride("font_size", 20);
        text.AddChild(_status);
        _elapsed = new Label { Text = "0s elapsed" };
        _elapsed.AddThemeColorOverride("font_color", new Color(0.66f, 0.76f, 0.64f));
        _elapsed.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(_elapsed);
    }

    internal void SetStatus(string status)
    {
        if (status == _status.Text) return;
        _status.Text = status;
        _phaseStarted = Time.GetTicksMsec();
    }

    internal void ShowError(string message)
    {
        _failed = true;
        _status.Text = message;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _elapsed.Text = "Close this window and try again.";
        _spinner.Hide();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_failed)
            _elapsed.Text = ((Time.GetTicksMsec() - _started) / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "s elapsed";
    }

    private sealed partial class Spinner : Control
    {
        internal Spinner()
        {
            ProcessMode = ProcessModeEnum.Always;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Process(double delta)
        {
            _ = delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var center = Size / 2;
            var angle = (float)(Time.GetTicksMsec() % 1200) / 1200 * Mathf.Tau;
            DrawArc(center, 12, 0, Mathf.Tau, 48, new Color(0.25f, 0.32f, 0.24f), 2.5f, true);
            DrawArc(center, 12, angle, angle + Mathf.Pi * 1.5f, 36, new Color(0.66f, 0.95f, 0.50f), 2.5f, true);
        }
    }
}
