using Godot;


namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1MainMenuNumericContracts
{
    // Original-style asset-free presentation values. Owned Fallout UI begins
    // only after New Game enters the verified character-start contract.
    internal const float PresentationFloat0Point04f = 0.04f;
    internal const float PresentationFloat0Point18f = 0.18f;
    internal const float PresentationFloat0Point22f = 0.22f;
    internal const float PresentationFloat0Point34f = 0.34f;
    internal const float PresentationFloat0Point74f = 0.74f;
    internal const float PresentationFloat0Point82f = 0.82f;
    internal const float PresentationFloat0Point92f = 0.92f;
    internal const float PresentationFloat1Point0f = 1.0f;
    internal const float PresentationFloat360Point0f = 360.0f;
    internal const float PresentationFloat52Point0f = 52.0f;
    internal const int PresentationInt1 = 1;
    internal const int PresentationInt14 = 14;
    internal const int PresentationInt18 = 18;
    internal const int PresentationInt20 = 20;
    internal const int PresentationInt52 = 52;
    internal const int PresentationInt109 = 109;
}

internal partial class Fo1MainMenu : CanvasLayer
{
    private static readonly Color Amber = new(
        Fo1MainMenuNumericContracts.PresentationFloat0Point92f,
        Fo1MainMenuNumericContracts.PresentationFloat0Point74f,
        Fo1MainMenuNumericContracts.PresentationFloat0Point18f);
    private static readonly Color MutedGreen = new(
        Fo1MainMenuNumericContracts.PresentationFloat0Point34f,
        Fo1MainMenuNumericContracts.PresentationFloat0Point82f,
        Fo1MainMenuNumericContracts.PresentationFloat0Point22f);
    private string _startPresentation = string.Empty;
    private bool _continueAvailable;

    internal event Action? ContinueRequested;
    internal event Action? NewGameRequested;
    internal event Action? OptionsRequested;
    internal event Action? ExitRequested;

    internal void Configure(string startPresentation, bool continueAvailable)
    {
        if (startPresentation is not "hex-tactical" and not "first-person")
            throw new ArgumentException(
                "Fallout 1 front end requires hex-tactical or first-person presentation.",
                nameof(startPresentation));
        _startPresentation = startPresentation;
        _continueAvailable = continueAvailable;
        Name = "Fallout1MainMenu";
        Layer = Fo1MainMenuNumericContracts.PresentationInt109;
    }

    internal void RequestContinueForHeadlessProof()
    {
        if (!_continueAvailable)
            throw new InvalidOperationException("Fallout 1 Continue proof requires a menu-visible saved game.");
        ContinueRequested?.Invoke();
    }

    public override void _Ready()
    {
        var background = new ColorRect
        {
            Color = new Color(
                Fo1MainMenuNumericContracts.PresentationFloat0Point04f,
                Fo1MainMenuNumericContracts.PresentationFloat0Point04f,
                Fo1MainMenuNumericContracts.PresentationFloat0Point04f,
                Fo1MainMenuNumericContracts.PresentationFloat1Point0f),
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
                Fo1MainMenuNumericContracts.PresentationFloat360Point0f,
                Fo1MainMenuNumericContracts.PresentationFloat52Point0f),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        stack.AddThemeConstantOverride(
            "separation",
            Fo1MainMenuNumericContracts.PresentationInt18);
        center.AddChild(stack);

        var title = BuildLabel(
            "FALLOUT",
            Fo1MainMenuNumericContracts.PresentationInt52,
            Amber);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(title);

        var subtitle = BuildLabel(
            "OPENNV  •  VAULT 13 ENTRANCE",
            Fo1MainMenuNumericContracts.PresentationInt14,
            MutedGreen);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(subtitle);

        Button? continueGame = null;
        if (_continueAvailable)
        {
            continueGame = BuildMenuButton("CONTINUE");
            continueGame.Pressed += () => ContinueRequested?.Invoke();
            stack.AddChild(continueGame);
        }

        var newGame = BuildMenuButton("NEW GAME");
        newGame.Pressed += () => NewGameRequested?.Invoke();
        stack.AddChild(newGame);

        var options = BuildMenuButton("OPTIONS");
        options.Name = "OpenNVOptionsButton";
        options.Pressed += () => OptionsRequested?.Invoke();
        stack.AddChild(options);

        var route = BuildLabel(
            _startPresentation == "hex-tactical"
                ? "SELECTED VIEW  •  HEX TACTICAL"
                : "SELECTED VIEW  •  FIRST PERSON",
            Fo1MainMenuNumericContracts.PresentationInt14,
            MutedGreen);
        route.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(route);

        var exit = BuildMenuButton("EXIT");
        exit.Pressed += () => ExitRequested?.Invoke();
        stack.AddChild(exit);
        (continueGame ?? newGame).GrabFocus();
    }

    private static Button BuildMenuButton(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(
                Fo1MainMenuNumericContracts.PresentationFloat360Point0f,
                Fo1MainMenuNumericContracts.PresentationFloat52Point0f),
        };
        button.AddThemeColorOverride("font_color", Amber);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeFontSizeOverride(
            "font_size",
            Fo1MainMenuNumericContracts.PresentationInt20);
        return button;
    }

    private static Label BuildLabel(string text, int fontSize, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride(
            "outline_size",
            Fo1MainMenuNumericContracts.PresentationInt1);
        return label;
    }
}
