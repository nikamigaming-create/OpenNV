using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2OpeningTailHandoffNumericContracts
{
    internal const int CanvasLayer = 125;
    internal const int RenderSettleFrames = 2;
}

internal sealed partial class Fo2OpeningTailHandoff : Node
{
    private ImageTexture? _activeTexture;
    private bool _skipRequested;

    internal bool IsPlaying { get; private set; }
    internal bool Completed { get; private set; }
    internal bool ControlReleased { get; private set; }
    internal bool TerminalBlackPresented { get; private set; }
    internal bool SkipRequested => _skipRequested;
    internal bool SkipTerminalStateApplied { get; private set; }
    internal int FinalPresentedSourceFrame { get; private set; }
    internal float FinalSourceFadeFraction { get; private set; }
    internal IReadOnlyList<int> PresentedSourceFrames => _presentedSourceFrames;
    internal Transform3D PreparedCameraTransform { get; private set; }
    internal Transform3D RevealedCameraTransform { get; private set; }
    internal string RawTerminalFramePath { get; private set; } = "";
    internal string RenderedTerminalFramePath { get; private set; } = "";
    internal string BlackSeamFramePath { get; private set; } = "";
    internal string LiveRevealFramePath { get; private set; } = "";
    internal Fo2ArroyoCavesPlayProof.World3DAudit? World3DAudit { get; private set; }
    internal Fo2ArroyoCavesPlayProof.SourceClosureLedger? SourceClosure
    {
        get;
        private set;
    }

    private readonly List<int> _presentedSourceFrames = [];

    internal void RequestSkip()
    {
        if (IsPlaying)
            _skipRequested = true;
    }

    internal async Task Play(
        Fo2OpeningTailContract contract,
        Fo2ArroyoCavesPresentationCatalog catalog,
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
        var camera = runtime.Player.GetNode<Camera3D>("ARROYO_PLAYER_FOLLOW_CAMERA");
        var proofHost = GetParent() ?? throw new InvalidOperationException(
            "Fallout 2 opening handoff has no live scene host.");
        World3DAudit = Fo2ArroyoCavesPlayProof.AuditWorld3D(
            proofHost,
            catalog,
            scene,
            runtime,
            camera);
        SourceClosure = Fo2ArroyoCavesPlayProof.BuildSourceClosure(
            catalog,
            scene,
            runtime,
            World3DAudit);
        VerifyFirstBeatClosure(scene, runtime, World3DAudit, SourceClosure);

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
        var preparedTexture = LoadSourceTexture(contract, contract.Frames[0]);
        audio.Play();
        var playbackClock = Stopwatch.StartNew();
        for (var index = 0; index < contract.Frames.Count && !_skipRequested; index++)
        {
            await WaitForSourceTime(playbackClock, index * contract.FramePeriodSeconds);
            PresentSourceFrame(contract, index, preparedTexture, video, fade);
            if (index + 1 < contract.Frames.Count)
                preparedTexture = LoadSourceTexture(contract, contract.Frames[index + 1]);
        }

        if (_skipRequested)
        {
            if (FinalPresentedSourceFrame != contract.TerminalFrame)
            {
                var terminalIndex = contract.Frames.Count - 1;
                PresentSourceFrame(
                    contract,
                    terminalIndex,
                    LoadSourceTexture(contract, contract.Frames[terminalIndex]),
                    video,
                    fade);
            }
            SkipTerminalStateApplied = true;
            await WaitPostDraw();
        }
        else
            await WaitForSourceTime(
                playbackClock,
                contract.Frames.Count * contract.FramePeriodSeconds);
        if (FinalPresentedSourceFrame != contract.TerminalFrame ||
            !Mathf.IsEqualApprox(
                FinalSourceFadeFraction,
                contract.TerminalFadeFraction))
            throw new InvalidOperationException(
                "Fallout 2 Elder tail did not converge on its source terminal state.");
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

    private void PresentSourceFrame(
        Fo2OpeningTailContract contract,
        int index,
        ImageTexture texture,
        TextureRect video,
        ColorRect fade)
    {
        if (index < 0 || index >= contract.Frames.Count)
            throw new InvalidOperationException(
                "Fallout 2 Elder source-frame presentation index drifted.");
        var source = contract.Frames[index];
        video.Texture = texture;
        var previous = _activeTexture;
        _activeTexture = texture;
        previous?.Dispose();
        var fadeFraction = contract.SourceFadeFraction(source.SourceFrame);
        fade.Color = new Color(0.0f, 0.0f, 0.0f, fadeFraction);
        FinalPresentedSourceFrame = source.SourceFrame;
        FinalSourceFadeFraction = fadeFraction;
        _presentedSourceFrames.Add(source.SourceFrame);
    }

    private static ImageTexture LoadSourceTexture(
        Fo2OpeningTailContract contract,
        Fo2OpeningTailFrame source)
    {
        var image = Image.LoadFromFile(source.Path);
        if (image is null || image.IsEmpty() || image.GetWidth() != contract.Width ||
            image.GetHeight() != contract.Height)
            throw new InvalidOperationException(
                $"Fallout 2 Elder frame {source.SourceFrame} failed runtime decode.");
        return ImageTexture.CreateFromImage(image);
    }

    private async Task WaitForSourceTime(Stopwatch playbackClock, double targetSeconds)
    {
        var remaining = targetSeconds - playbackClock.Elapsed.TotalSeconds;
        if (remaining > 0.0)
            await ToSignal(
                GetTree().CreateTimer(remaining),
                SceneTreeTimer.SignalName.Timeout);
    }

    private static void VerifyFirstBeatClosure(
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2ArroyoCavesPlayProof.World3DAudit world,
        Fo2ArroyoCavesPlayProof.SourceClosureLedger closure)
    {
        if (!runtime.Player.Presentation.UsesOwnedFrmRelief ||
            runtime.Player.Presentation.UsesOwnedDonor ||
            runtime.Player.Presentation.MeshInstances != 2 ||
            runtime.Player.Presentation.MoldedFaceTriangles <= 0 ||
            runtime.Player.Presentation.MoldedSideTriangles <= 0 ||
            world.VisibleSpriteCards != 0 || world.InFrustumSpriteCards != 0 ||
            world.ClosedReliefSourceObjects != scene.Molded3D.ClosedReliefWorldObjects ||
            world.SourceTorchAssemblies != scene.Molded3D.VisibleSourceTorchProps ||
            world.SourceTorchPostLayeredAssemblies !=
                scene.Molded3D.SourceTorchPostLayeredAssemblies ||
            world.SourceTorchFrmPixelProps != world.SourceTorchAssemblies ||
            world.SourceMapLightRecords != scene.Molded3D.SourceMapLightRecords ||
            world.SourceMapLights != scene.Molded3D.SourceMapLights ||
            world.SourceTorchMotivatedMapLights !=
                scene.Molded3D.SourceTorchMotivatedMapLights ||
            world.InFrustumTorchAssembliesWithMissingSourcePixels != 0 ||
            world.InvalidSourceMapLights != 0 ||
            closure.UnaccountedSourceObjects != 0 ||
            !closure.FirstBeatRuntimeClosurePassed)
            throw new InvalidOperationException(
                "Fallout 2 opening handoff zero-card or first-beat closure failed: " +
                $"player={runtime.Player.Presentation.GeometryMode}, " +
                $"visibleCards={world.VisibleSpriteCards}, " +
                $"frustumCards={world.InFrustumSpriteCards}, " +
                $"relief={world.ClosedReliefSourceObjects}/" +
                    $"{scene.Molded3D.ClosedReliefWorldObjects}, " +
                $"torches={world.SourceTorchAssemblies}/" +
                    $"{scene.Molded3D.VisibleSourceTorchProps}, " +
                $"frmPixelProps={world.SourceTorchFrmPixelProps}, mapLights=" +
                    $"{world.SourceMapLightRecords}/{world.SourceMapLights}, " +
                $"missingSourcePixels={world.InFrustumTorchAssembliesWithMissingSourcePixels}, " +
                $"invalidMapLights={world.InvalidSourceMapLights}, " +
                $"unaccounted={closure.UnaccountedSourceObjects}, " +
                $"admittedIncomplete={closure.AdmittedBehaviorIncompleteSourceObjects}, " +
                $"hudState={closure.FirstBeatRuntimeClosurePassed}.");
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
