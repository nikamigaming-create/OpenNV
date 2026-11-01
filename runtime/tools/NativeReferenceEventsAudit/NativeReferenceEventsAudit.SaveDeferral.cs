using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void SaveDeferral()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-save-deferral-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Contact.esm");
        var player = new RuntimeNativePlayer();
        player.Configure(RuntimeConfiguration.Load(), Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
        AddChild(player); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        try { VerifySaveDeferral(path, player); }
        finally
        {
            player.QueueFree();
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void VerifySaveDeferral(string path, RuntimeNativePlayer player)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var referenceBytes = Record("REFR", 0x901, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24]));
        var group = new byte[24 + referenceBytes.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); referenceBytes.CopyTo(group, 24);
        var playerStats = new byte[24]; playerStats[8] = 1;
        byte[] Setting(uint id, string name, byte[] value) => Record("GMST", id,
            Field("EDID", Encoding.ASCII.GetBytes(name + "\0")), Field("DATA", value));
        File.WriteAllBytes(path, Record("TES4", 0, Field("HEDR", header)).Concat(Record("ACTI", 0x700))
            .Concat(Record("NPC_", 7, Field("ACBS", playerStats), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])))
            .Concat(Setting(0x710, "fAVDHealthEnduranceMult", BitConverter.GetBytes(20f)))
            .Concat(Setting(0x711, "fAVDHealthLevelMult", BitConverter.GetBytes(0f)))
            .Concat(Setting(0x712, "fAVDActionPointsBase", BitConverter.GetBytes(65f)))
            .Concat(Setting(0x713, "fAVDActionPointsMult", BitConverter.GetBytes(3f)))
            .Concat(Setting(0x714, "iXPBase", BitConverter.GetBytes(200)))
            .Concat(Setting(0x715, "iXPBumpBase", BitConverter.GetBytes(150)))
            .Concat(Record("CELL", 0x800, Field("DATA", [1]))).Concat(group).ToArray());
        using var records = FalloutPluginStack.Load(Path.GetDirectoryName(path)!, ["Contact.esm"]);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var inventory = new FalloutPlayerInventory();
        var scripts = new FalloutQuestScripts(records, new(records), new HashSet<FalloutFormKey>(), inventory,
            defaultProcessingDelay: .1f, references: world);
        var driver = new NativeSaveDeferralDriverFixture();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = typeof(RuntimeNativeOpeningStageDriver);
        void Bind(string field, object value) => owner.GetField(field, flags)!.SetValue(driver, value);
        JsonElement State() => JsonSerializer.SerializeToElement(driver.SaveRequestState);
        try
        {
            Bind("_player", player); Bind("_scripts", scripts);
            var vitals = new FalloutPlayerVitals(records, Key(7), new(5, 5, 5, 5, 5, 5, 5));
            Bind("_vitals", vitals);
            var abilities = new FalloutPlayerAbilityScripts(records, () => [],
                _ => throw new InvalidOperationException("Unexpected ability condition in save-deferral fixture."));
            abilities.BindExecutor((_, _, _, _) => throw new InvalidOperationException("Unexpected active effect in save-deferral fixture."));
            Bind("_playerAbilities", abilities);
            Bind("_activeCell", Key(0x800));
            Bind("_stageResults", new FalloutQuestStages(records, new(records),
                (_, _, _) => throw new InvalidOperationException("Unexpected stage result in save-deferral fixture."),
                _ => throw new InvalidOperationException("Unexpected stage condition in save-deferral fixture.")));
            // No Aid is active and the fixture advances zero time. The real
            // driver frame still evaluates and retains its autosave request.
            Bind("_ingestibles", new FalloutPlayerIngestibles(records, inventory, null!, new(Key(0x702), []),
                _ => throw new InvalidOperationException("Unexpected Aid evaluation."), _ => false, () => false));
            Bind("_imageSpaceState", new FalloutImageSpaceState());
            Bind("_presentationActive", true);
            var reference = world.Get(Key(0x901));
            reference.ProcedureCaptureBlocker = "First native procedure snapshot is pending.";
            var readinessQueries = 0;
            reference.CanCaptureIdleRequests = () => { readinessQueries++; return true; };
            var source = records.GetEffective(Key(0x700));
            driver.ApplyNativeSourceCommand(source.FormKey, new(records, source, source, []), "AutoSave", []);
            Require(!State().GetProperty("admissionObservation").GetProperty("sampled").GetBoolean() && readinessQueries == 0,
                "Unsampled autosave telemetry performed readiness admission or invented an observation.");
            for (var frame = 0; frame < 3; frame++) driver._Process(0);
            var observedQueries = readinessQueries;
            var observed = State();
            Require(driver.ExecutionError is null && observedQueries >= 3 && observed.GetProperty("requested").GetBoolean() &&
                observed.GetProperty("deferredBy").GetString() == "actor-procedure-initialization" &&
                observed.GetProperty("admissionObservation").GetProperty("sampled").GetBoolean() &&
                readinessQueries == observedQueries,
                "A temporary procedure capture blocker consumed or permanently failed the queued autosave.");
            try
            {
                owner.GetMethod("CaptureCurrentState", flags)!.Invoke(driver, [Key(0x800), true]);
                throw new InvalidOperationException("Explicit capture admitted an uninitialized procedure.");
            }
            catch (TargetInvocationException error) when (error.InnerException is NotSupportedException &&
                error.InnerException.Message.Contains("actor-procedure-initialization", StringComparison.Ordinal))
            { }
            reference.ProcedureCaptureBlocker = null;
            Require(driver.ObserveManualSaveAdmission().Kind == RuntimeManualSaveAdmissionKind.Ready && world.Capture().Count > 0,
                "Settled procedure did not release the same pending autosave for normal capture.");
            observedQueries = readinessQueries;
            var settled = State();
            Require(driver.ExecutionError is null && settled.GetProperty("requested").GetBoolean() &&
                settled.GetProperty("deferredBy").GetString() == "actor-procedure-initialization" &&
                settled.GetProperty("admissionObservation").GetProperty("processFrame").GetUInt64() ==
                    observed.GetProperty("admissionObservation").GetProperty("processFrame").GetUInt64() &&
                readinessQueries == observedQueries,
                "Historical autosave telemetry rescanned current readiness or rewrote its last actual admission.");
            reference.AnimationSoundEvents.Fail(null, "Fixture source audio continuation is unowned.");
            driver._Process(0);
            observedQueries = readinessQueries;
            for (var frame = 0; frame < 3; frame++) driver._Process(0);
            Require(driver.ExecutionError is null && State().GetProperty("requested").GetBoolean() &&
                State().GetProperty("deferredBy").GetString() == "animation-audio-pending" && readinessQueries == observedQueries,
                "Waiting audio repeatedly captured unrelated actor readiness or admitted the automatic writer.");
            try
            {
                _ = world.Capture();
                throw new InvalidOperationException("Boolean audio deferral waived strict source-ledger capture.");
            }
            catch (NotSupportedException) { }
            const string captureFailure = "Reference Contact.esm:000901: Saved door motion has no retained source animation clock.";
            owner.GetMethod("RetainDriverFailure", flags)!.Invoke(driver, [new InvalidDataException(captureFailure)]);
            Require(driver.ObserveManualSaveAdmission() is
            { Kind: RuntimeManualSaveAdmissionKind.Refused, Blocker: "native-driver-failure: " + captureFailure } &&
                State().GetProperty("requested").GetBoolean(),
                "A pending autosave hid the original driver failure from manual admission.");
            // Restore the fixture's healthy driver for the independent death
            // refusal below; production never clears a failed capture owner.
            owner.GetProperty(nameof(RuntimeNativeOpeningStageDriver.ExecutionError), flags | BindingFlags.Public)!.SetValue(driver, null);
            vitals.Damage(vitals.State.MaximumHitPoints);
            driver._Process(0);
            Require(driver.ExecutionError is null && State().GetProperty("requested").GetBoolean(),
                "Player death consumed or permanently failed a pending autosave.");
            try
            {
                owner.GetMethod("CaptureCurrentState", flags)!.Invoke(driver, [Key(0x800), true]);
                throw new InvalidOperationException("Explicit capture admitted a defeated player.");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException &&
                error.InnerException.Message.Contains("after player death", StringComparison.Ordinal))
            { }
            GD.Print("OPENNV_NATIVE_SAVE_DEFERRAL_PASS sourceRequest=true transientBlockedFrames=true explicitCaptureRefused=true sameRequestEligible=true defeatedPlayerProtected=true");
        }
        finally { driver.Free(); }
    }
}

internal partial class NativeSaveDeferralDriverFixture : RuntimeNativeOpeningStageDriver
{
    public override void _Ready() { }
}
