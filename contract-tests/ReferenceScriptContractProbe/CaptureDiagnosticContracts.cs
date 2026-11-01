using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class CaptureDiagnosticContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var observed = RuntimeNativeNpc.ObserveProcedureDiagnostic(() => 8);
        Require(observed.Value == 8 && observed.Error is null, "Procedure diagnostics lost their actual owner value.");
        const string refusal = "Sleep transition has no admitted native procedure phase.";
        observed = RuntimeNativeNpc.ObserveProcedureDiagnostic(() => throw new NotSupportedException(refusal));
        Require(observed.Value is null && observed.Error == refusal,
            "An unqualified procedure erased its diagnostic or invented a phase.");
        var refusalJson = JsonSerializer.Serialize(observed.Error);
        Require(JsonSerializer.Deserialize<string>(refusalJson) == refusal, "Procedure refusal did not survive diagnostic publication.");
        var reference = new FalloutFormKey("Pose.esm", 0x900);
        var cell = new FalloutFormKey("Pose.esm", 0x800);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        var state = world.Get(reference);
        state.ProcedureCaptureBlocker = "Actual independent procedure remains unbound.";
        var sounds = new FalloutAnimationSoundCaptureDiagnostic(false, true, true, 1, 2, 7,
            Array.AsReadOnly(new[] { "source-envelope-unbound" }));
        var pose = new FalloutActorStoppedPoseCaptureDiagnostic(false, true,
            Array.AsReadOnly(new[] { new FalloutActorCaptureBlocker("combat-pose", "enemy-sounds") }),
            sounds, "Historical query failed before an active hit reaction.", null);
        var retired = new FalloutActorSelectionCaptureDiagnostic(reference, 11, false, true, null, 45,
            Array.AsReadOnly(new[] { new FalloutActorCaptureBlocker("npc-selection", "independent-pose") }), pose);
        state.RetiredSelectionCaptureDiagnostic = retired;
        world.UnloadCell(cell);
        Require(state.SelectionCaptureDiagnostic == retired && world.PendingProcedureCaptureCount == 1,
            "Retirement lost the exact same-instance diagnostic or changed capture readiness.");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(world.PendingProcedureCaptures)))
        {
            var captured = json.RootElement[0].GetProperty("selectionCapture");
            Require(captured.GetProperty("NativeOwner").GetUInt64() == 11 &&
                captured.GetProperty("StoppedPose").GetProperty("EnemySounds").GetProperty("ActiveVoices").GetInt32() == 2 &&
                captured.GetProperty("StoppedPose").GetProperty("HitReactionError").GetString() == pose.HitReactionError,
                "Detailed telemetry lost pre-cleanup sounds or the independent historical hit fault.");
        }
        Reject(world.Capture);
        // Even a claimed ready observation cannot grant a saved continuation.
        state.RetiredSelectionCaptureDiagnostic = retired with { Ready = true };
        Reject(world.Capture);
        var current = retired with { NativeOwner = 12, Retired = false };
        state.ObserveSelectionCapture = () => current;
        Require(state.SelectionCaptureDiagnostic == current, "A retired observer replaced the current presentation's diagnostic.");
        state.ObserveSelectionCapture = null;
        Require(state.SelectionCaptureDiagnostic?.NativeOwner == 11, "Observer teardown lost the retained native lifetime identity.");
        state.ProcedureCaptureBlocker = null;
        var snapshot = world.Capture();
        using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshot);
        Require(cold.Get(reference).SelectionCaptureDiagnostic is null && cold.PendingProcedureCaptureCount == 0,
            "A diagnostic became saved continuation authority or created a cold capture blocker.");
        SinglePublication(records, reference, cell, retired);
        Console.WriteLine("OPENNV_CAPTURE_DIAGNOSTIC_CONTRACT_PASS retained=true nativeLifetime=true soundCounts=true hitFaultSeparate=true observerReplacement=true singlePublication=true freshReadiness=true noAdmission=true noSaveSchema=true parity=unverified");
    }

    private static void SinglePublication(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey cell,
        FalloutActorSelectionCaptureDiagnostic retired)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        var state = world.Get(reference);
        state.ProcedureCaptureBlocker = "Actual independent procedure remains unbound.";
        state.RetiredSelectionCaptureDiagnostic = retired;
        var idleCalls = 0; var bindingCalls = 0;
        var idleReady = false; var bindingReady = false;
        state.CanCaptureIdleRequests = () => { idleCalls++; return idleReady; };
        state.CanCapturePackageBindingFailure = () => { bindingCalls++; return bindingReady; };
        var random = state.SoundRandom.State;
        var instances = world.InstanceCount;
        var first = world.ObserveCaptureReadiness(includeCorpseReadiness: false);
        Require(idleCalls == 1 && bindingCalls == 1 && first.PendingProcedures == 1 && first.ProcedureOwners.Count == 1 &&
            first.StoppedPackageBindings == 0 && first.PendingCorpseCaptures is null && first.PendingAnimationSounds == 0,
            "Live publication repeated readiness callbacks or lost its exact count/owner/summary scope.");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(first.ProcedureOwners)))
            Require(json.RootElement[0].GetProperty("blocker").GetString() == "Idle request/sequence continuation." &&
                json.RootElement[0].GetProperty("selectionCapture").GetProperty("NativeOwner").GetUInt64() == retired.NativeOwner,
                "Single-publication classification lost its observed idle blocker or retained native diagnostic.");
        idleReady = true;
        var second = world.ObserveCaptureReadiness(includeCorpseReadiness: true);
        Require(idleCalls == 2 && bindingCalls == 2 && second.PendingProcedures == 1 && second.PendingCorpseCaptures == 0,
            "A later publication reused old readiness or evaluated the binding classification twice.");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(second.ProcedureOwners)))
            Require(json.RootElement[0].GetProperty("blocker").GetString() == state.ProcedureCaptureBlocker,
                "A publication retained an earlier idle refusal after that owner became ready.");
        bindingReady = true;
        var third = world.ObserveCaptureReadiness(includeCorpseReadiness: false);
        Require(idleCalls == 3 && bindingCalls == 3 && third.PendingProcedures == 0 && third.StoppedPackageBindings == 1 &&
            first.PendingProcedures == 1 && second.PendingProcedures == 1,
            "Readiness observation was mutable or reused a different publication's admission result.");
        bindingReady = false;
        Reject(world.Capture);
        Require(idleCalls > 3 && bindingCalls > 3 && state.SoundRandom.State == random && world.InstanceCount == instances,
            "Live readiness became save authority or changed source randomness/instance ownership.");
        Require(world.HasPendingProcedureCapture, "Boolean admission lost the current procedure blocker.");
        var beforeRecheck = idleCalls;
        Require(world.HasPendingProcedureCapture && idleCalls > beforeRecheck,
            "Waiting admission reused a stale readiness result rather than rechecking its actual blocker.");
        bindingReady = true;
        Require(!world.HasPendingProcedureCapture, "Resolved procedure owner remained blocked by its previous observation.");
        bindingReady = false;
        Require(world.HasPendingProcedureCapture, "A new live procedure refusal was hidden by a prior ready observation.");
        Reject(world.Capture);
    }

    private static void Reject(Func<IReadOnlyList<FalloutReferenceSnapshot>> capture)
    {
        try { capture(); }
        catch (NotSupportedException) { return; }
        throw new InvalidDataException("A diagnostic waived an unowned capture continuation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
