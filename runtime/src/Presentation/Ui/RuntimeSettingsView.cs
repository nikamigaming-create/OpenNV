using System.Globalization;
using Godot;
using OpenNV.Runtime.Gameplay.Settings;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class RuntimeSettingsViewNumericContracts
{
    internal const float PresentationFloat0Point92f = 0.92f;
    internal const float PresentationFloat0Point74f = 0.74f;
    internal const float PresentationFloat0Point18f = 0.18f;
    internal const float PresentationFloat0Point34f = 0.34f;
    internal const float PresentationFloat0Point82f = 0.82f;
    internal const float PresentationFloat0Point22f = 0.22f;
    internal const float PresentationFloat0Point88f = 0.88f;
    internal const float PresentationFloat420Point0f = 420.0f;
    internal const float PresentationFloat52Point0f = 52.0f;
    internal const int PresentationInt1 = 1;
    internal const int PresentationInt14 = 14;
    internal const int PresentationInt18 = 18;
    internal const int PresentationInt20 = 20;
    internal const int PresentationInt32 = 32;
    internal const int PresentationInt120 = 120;
}

internal sealed partial class RuntimeSettingsView : CanvasLayer
{
    private static readonly Color Amber = new(
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point92f,
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point74f,
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point18f);
    private static readonly Color Green = new(
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point34f,
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point82f,
        RuntimeSettingsViewNumericContracts.PresentationFloat0Point22f);
    private RuntimeSettingsState _settings = null!;
    private float _configuredRadiansPerPixel;
    private LineEdit _scale = null!;
    private Label _effective = null!;

    internal event Action? CloseRequested;

    internal void Configure(
        RuntimeSettingsState settings,
        float configuredRadiansPerPixel)
    {
        _settings = settings;
        _configuredRadiansPerPixel = configuredRadiansPerPixel;
        _ = settings.ApplyMouseSensitivity(configuredRadiansPerPixel);
        Name = "OpenNVRuntimeSettings";
        Layer = RuntimeSettingsViewNumericContracts.PresentationInt120;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        var background = new ColorRect
        {
            Color = new Color(
                Colors.Black,
                RuntimeSettingsViewNumericContracts.PresentationFloat0Point88f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var stack = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(
                RuntimeSettingsViewNumericContracts.PresentationFloat420Point0f,
                RuntimeSettingsViewNumericContracts.PresentationFloat52Point0f),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        stack.AddThemeConstantOverride(
            "separation",
            RuntimeSettingsViewNumericContracts.PresentationInt18);
        center.AddChild(stack);

        stack.AddChild(Label("OPENNV OPTIONS", RuntimeSettingsViewNumericContracts.PresentationInt32, Amber));
        stack.AddChild(Label(
            $"CONFIGURED MOUSE LOOK  {_configuredRadiansPerPixel.ToString("G6", CultureInfo.InvariantCulture)} RAD/PIXEL",
            RuntimeSettingsViewNumericContracts.PresentationInt14,
            Green));
        _scale = new LineEdit
        {
            Name = "MouseSensitivityScale",
            Text = _settings.MouseSensitivityScale.ToString("G6", CultureInfo.InvariantCulture),
            PlaceholderText = "Positive mouse-look multiplier",
        };
        stack.AddChild(_scale);
        _effective = Label(string.Empty, RuntimeSettingsViewNumericContracts.PresentationInt14, Green);
        stack.AddChild(_effective);
        RefreshEffective();

        var apply = Button("APPLY");
        apply.Name = "ApplyRuntimeSettings";
        apply.Pressed += ApplyText;
        stack.AddChild(apply);
        var restore = Button("RESTORE CONFIGURED DEFAULT");
        restore.Pressed += () =>
        {
            _settings.RestoreMouseSensitivityDefault();
            _scale.Text = RuntimeSettingsState.NeutralMouseSensitivityScale.ToString(
                "G6",
                CultureInfo.InvariantCulture);
            RefreshEffective();
        };
        stack.AddChild(restore);
        var done = Button("DONE");
        done.Pressed += Dismiss;
        stack.AddChild(done);
        _scale.GrabFocus();
        GetTree().Paused = true;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Dismiss();
        }
    }

    public override void _ExitTree() => GetTree().Paused = false;

    internal void ApplyMouseSensitivityForProof(float scale)
    {
        _scale.Text = scale.ToString("G9", CultureInfo.InvariantCulture);
        ApplyText();
    }

    internal void CloseForProof() => Dismiss();

    private void Dismiss()
    {
        GetTree().Paused = false;
        CloseRequested?.Invoke();
    }

    private void ApplyText()
    {
        if (!float.TryParse(
                _scale.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var scale))
            throw new InvalidOperationException("Mouse-sensitivity scale is not a number.");
        _settings.SetMouseSensitivityScale(scale);
        RefreshEffective();
    }

    private void RefreshEffective() =>
        _effective.Text =
            $"EFFECTIVE  {_settings.ApplyMouseSensitivity(_configuredRadiansPerPixel).ToString("G6", CultureInfo.InvariantCulture)} RAD/PIXEL  •  SAVED";

    private static Button Button(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(
                RuntimeSettingsViewNumericContracts.PresentationFloat420Point0f,
                RuntimeSettingsViewNumericContracts.PresentationFloat52Point0f),
        };
        button.AddThemeColorOverride("font_color", Amber);
        button.AddThemeFontSizeOverride("font_size", RuntimeSettingsViewNumericContracts.PresentationInt20);
        return button;
    }

    private static Label Label(string text, int fontSize, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", RuntimeSettingsViewNumericContracts.PresentationInt1);
        return label;
    }
}
