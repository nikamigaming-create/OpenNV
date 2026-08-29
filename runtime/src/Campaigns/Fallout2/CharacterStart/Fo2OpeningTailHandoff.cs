using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2OpeningTailHandoffNumericContracts
{
    internal const int CanvasLayer = 125;
    internal const int RenderSettleFrames = 2;
}

internal sealed partial class Fo2OpeningTailHandoff : Node
{
    private readonly List<ImageTexture> _textures = [];
    private bool _skipRequested;

    internal bool IsPlaying { get; private set; }
    internal bool Completed { get; private set; }
    internal bool ControlReleased { get; private set; }
    internal bool TerminalBlackPresented { get; private set; }
    internal int FinalPresentedSourceFrame { get; private set; }
    internal float FinalSourceFadeFraction { get; private set; }
    internal IReadOnlyList<int> PresentedSourceFrames => _presentedSourceFrames;
    internal Transform3D PreparedCameraTransform { get; private set; }
    internal Transform3D RevealedCameraTransform { get; private set; }
    internal string RawTerminalFramePath { get; private set; } = "";
    internal string RenderedTerminalFramePath { get; private set; } = "";
    internal string BlackSeamFramePath { get; private set; } = "";
    internal string LiveRevealFramePath { get; private set; } = "";

    private readonly List<int> _presentedSourceFrames = [];

    internal void RequestSkip()
    {
        if (IsPlaying)
            _skipRequested = true;
    }

    internal async Task Play(
        Fo2OpeningTailContract contract,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        string? proofRoot)
    {
        if (IsPlaying || Completed || contract.Frames.Count == 0)
            throw new InvalidOperationException(
                "Fallout 2 opening tail may play exactly once with decoded frames.");
        if (runtime.Player.CurrentMapIndex != contract.HandoffMapIndex ||
            runtime.Player.CurrentElevation != contract.HandoffElevation ||
            runtime.Player.CurrentTile != contract.HandoffArrivalTile ||
            runtime.Player.Presentation.Direction != contract.HandoffArrivalRotation)
            throw new InvalidOperationException(
                "Fallout 2 opening tail does not lead to its source-bound Arroyo arrival.");

        foreach (var frame in contract.Frames)
        {
            var image = Image.LoadFromFile(frame.Path);
            if (image is null || image.IsEmpty() || image.GetWidth() != contract.Width ||
                image.GetHeight() != contract.Height)
                throw new InvalidOperationException(
                    $"Fallout 2 Elder frame {frame.SourceFrame} failed runtime decode.");
            _textures.Add(ImageTexture.CreateFromImage(image));
        }

        var audioStream = AudioStreamWav.LoadFromFile(contract.AudioPath)
            ?? throw new InvalidOperationException(
                "Fallout 2 Elder tail PCM could not be loaded.");
        var audio = new AudioStreamPlayer
        {
            Name = "OwnedElderTailAudio",
            Stream = audioStream,
        };
        var layer = new CanvasLayer
        {
            Name = "OwnedElderTailToLiveArroyo",
            Layer = Fo2OpeningTailHandoffNumericContracts.CanvasLayer,
        };
        var background = FullRect("OwnedElderTailBlackBackground", Colors.Black);
        var video = new TextureRect
        {
            Name = "OwnedElderTailFrames",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var fade = FullRect(
            "OwnedElderSourceFadeToBlack",
            new Color(0.0f, 0.0f, 0.0f, 0.0f));
        AddChild(layer);
        layer.AddChild(background);
        layer.AddChild(video);
        layer.AddChild(fade);
        layer.AddChild(audio);

        scene.Root.Visible = false;
        runtime.Player.SetControlsEnabled(false);
        IsPlaying = true;
        audio.Play();
        for (var index = 0; index < contract.Frames.Count && !_skipRequested; index++)
        {
            var source = contract.Frames[index];
            video.Texture = _textures[index];
            var fadeStep = source.SourceFrame - contract.FadeStartFrame + 1;
            var fadeFraction = Math.Clamp(fadeStep / (float)contract.FadeSteps, 0.0f, 1.0f);
            fade.Color = new Color(0.0f, 0.0f, 0.0f, fadeFraction);
            FinalPresentedSourceFrame = source.SourceFrame;
            FinalSourceFadeFraction = fadeFraction;
            _presentedSourceFrames.Add(source.SourceFrame);
            await ToSignal(
                GetTree().CreateTimer(contract.FramePeriodSeconds),
                SceneTreeTimer.SignalName.Timeout);
        }

        if (!_skipRequested && FinalPresentedSourceFrame != contract.TerminalFrame)
            throw new InvalidOperationException(
                "Fallout 2 Elder tail did not reach its terminal source frame.");
        if (!_skipRequested && proofRoot is not null)
        {
            Directory.CreateDirectory(proofRoot);
            RawTerminalFramePath = Path.Combine(
                proofRoot,
                "00-owned-elder-terminal-source.png");
            File.Copy(contract.Frames[^1].Path, RawTerminalFramePath, true);
            RenderedTerminalFramePath = await Capture(
                proofRoot,
                "01-owned-elder-terminal-source-fade.png");
        }

        audio.Stop();
        fade.Color = Colors.Black;
        TerminalBlackPresented = true;
        await WaitPostDraw();
        if (proofRoot is not null)
            BlackSeamFramePath = await Capture(proofRoot, "02-movie-end-black.png");

        var camera = runtime.Player.GetNode<Camera3D>("ARROYO_PLAYER_FOLLOW_CAMERA");
        scene.Root.Visible = true;
        PreparedCameraTransform = camera.GlobalTransform;
        for (var frame = 0;
             frame < Fo2OpeningTailHandoffNumericContracts.RenderSettleFrames;
             frame++)
            await WaitPostDraw();
        layer.Visible = false;
        await WaitPostDraw();
        RevealedCameraTransform = camera.GlobalTransform;
        if (proofRoot is not null)
            LiveRevealFramePath = await Capture(proofRoot, "03-live-arroyo-before-control.png");
        runtime.Player.SetControlsEnabled(true);
        ControlReleased = true;
        IsPlaying = false;
        Completed = true;
        layer.QueueFree();
    }

    private static ColorRect FullRect(string name, Color color)
    {
        var rect = new ColorRect
        {
            Name = name,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    private async Task WaitPostDraw()
    {
        await ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
    }

    private async Task<string> Capture(string proofRoot, string filename)
    {
        await WaitPostDraw();
        var path = Path.Combine(proofRoot, filename);
        var image = GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                $"Fallout 2 opening handoff frame could not be written: {path}");
        return path;
    }
}
