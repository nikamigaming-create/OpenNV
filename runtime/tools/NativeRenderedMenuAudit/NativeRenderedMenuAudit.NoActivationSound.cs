using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task NoActivationSound(string baseRoot, string mod, string root, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        NativeOwnedScriptSoundPlayer? presenter = null;
        RuntimeNativePlayer? player = null;
        AudioEffectCapture? capture = null;
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            var executable = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe");
            var executableHash = SHA256.HashData(File.ReadAllBytes(executable));
            var defaultEditorId = FalloutExecutableStringTable.ReadNoActivationSoundDefault(executable);
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var questHash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var sourceIndex = Array.FindIndex(fields, field => field.Signature == "SCTX" &&
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)).Any(IsSet));
            if (sourceIndex < 0) throw new InvalidDataException("Owned feedback fixture requires a source SetNoActivationSound stage entry.");
            var begin = sourceIndex;
            while (begin > 0 && fields[begin].Signature != "QSDT") --begin;
            if (fields[begin].Signature != "QSDT") throw new InvalidDataException("Owned feedback command has no stage-entry scope.");
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("QSDT" or "INDX" or "QOBJ")) ++end;
            var command = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(fields[sourceIndex].Data.Span)).First(IsSet);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Feedback fixture invented a presentation effect.")));
            using var defaultBinding = world.NoActivationSound.BindDefault(() => defaultEditorId);
            presenter = new(world.Sounds, source, world.Menus); AddChild(presenter);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            var configuration = RuntimeConfiguration.Load();
            OpenNV.Runtime.InputSystem.DesktopInputMap.Configure(configuration.Player.DesktopInput);
            player = new(); player.Configure(configuration, Transform3D.Identity); AddChild(player);
            player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
            player.ApplySourceControls(new(false, false, false, false, false, false, false));
            player.NoActivationFeedback = () => world.NoActivationSound.RejectActivation(records.RuntimeFormKey(0x14));
            void Activate()
            {
                using var input = new InputEventAction { Action = configuration.Player.DesktopInput.Activate.Action, Pressed = true };
                player._UnhandledInput(input);
            }
            executor.ExecuteStage(quest, fields[begin..end], command);
            var selected = world.NoActivationSound.Capture() ?? throw new InvalidDataException("Owned sound override was not selected.");
            if (world.Sounds.LastRequest is not null) throw new InvalidDataException("Setting the feedback sound played it early.");
            var session = new FalloutScriptSession(world.NoActivationSound);
            var saved = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records);
            var cold = new FalloutScriptSession(coldWorld.NoActivationSound); cold.Restore(saved);
            if (cold.Capture().NoActivationSound != selected || coldWorld.Sounds.LastRequest is not null)
                throw new InvalidDataException("Owned cold feedback selection was lost or replayed its voice.");
            player.SetModalInput(true); Activate(); player.XrActivate(); player.SetModalInput(false);
            if (world.Sounds.LastRequest is not null) throw new InvalidDataException("Modal input emitted activation feedback.");
            Activate(); var overrideRequest = world.Sounds.LastRequest!; var overrideMedia = world.Sounds.LastMedia!;
            Activate(); player.XrActivate();
            if (world.Sounds.LastRequest!.Id != overrideRequest.Id) throw new InvalidDataException("Repeated flat/XR input restarted active feedback.");
            var overrideMix = await CompleteMix();
            world.NoActivationSound.Clear(); Activate();
            var defaultRequest = world.Sounds.LastRequest!; var defaultMedia = world.Sounds.LastMedia!;
            if (defaultRequest.Source.EditorId != defaultEditorId) throw new InvalidDataException("Owned reset did not select the executable's default SOUN.");
            var defaultMix = await CompleteMix();
            player.ApplySourceControls(FalloutPlayerControlState.AllEnabled); Activate();
            if (world.Sounds.LastRequest!.Source.EditorId != defaultEditorId)
                throw new InvalidDataException("Empty-target ordinary activation lost its default feedback.");
            await CompleteMix();
            presenter.Free(); presenter = null;
            if (world.Sounds.ActiveVoices != 0 || !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(questHash) ||
                !SHA256.HashData(File.ReadAllBytes(executable)).AsSpan().SequenceEqual(executableHash))
                throw new InvalidDataException("Feedback retirement leaked audio or changed source inputs.");
            GD.Print("OPENNV_NATIVE_NO_ACTIVATION_SOUND_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                command,
                selected,
                defaultEditorId,
                overrideRequest,
                overrideMedia,
                overrideMix,
                defaultRequest,
                defaultMedia,
                defaultMix,
                cold = true,
                noColdReplay = true,
                modalSuppression = true,
                activeSuppression = true,
                flatDisabledAndEmptyTargetInput = true,
                xrInputAdapterWithoutHeadset = true,
                sourceReadonly = true,
                recording = false,
                boundary = "isolated-owned-command-input-and-mixer;campaign-endpoint-and-retail-parity-unverified"
            }));

            async Task<object> CompleteMix()
            {
                long samples = 0; var peak = 0f;
                var media = world.Sounds.LastMedia!; var request = world.Sounds.LastRequest!;
                var deadline = Time.GetTicksMsec() + checked((ulong)((media.Duration / request.Selection.PitchScale + 3) * 1000));
                void Drain()
                {
                    var count = capture.GetFramesAvailable();
                    if (count == 0) return;
                    var frames = capture.GetBuffer(count);
                    if (frames.Length != count) throw new InvalidDataException("Feedback mixer packet was lost.");
                    samples += frames.Length;
                    foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
                }
                while (world.Sounds.ActiveVoices != 0 && Time.GetTicksMsec() < deadline)
                { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
                Drain();
                if (world.Sounds.ActiveVoices != 0 || peak <= .000001f || capture.GetDiscardedFrames() != 0 || world.Sounds.LastError is not null)
                    throw new InvalidDataException("Feedback did not complete with nonzero native mixer samples and no sample loss.");
                return new { samples, peak, discarded = capture.GetDiscardedFrames(), completed = true };
            }
        }
        finally
        {
            player?.Free(); presenter?.Free();
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
        }
        static bool IsSet(string command) => command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
            .Equals("SetNoActivationSound", StringComparison.OrdinalIgnoreCase);
    }
}
