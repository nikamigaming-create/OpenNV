using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class CaptureDiagnosticContracts
{
    internal static void Run(FalloutPluginStack records)
    {
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
        Console.WriteLine("OPENNV_CAPTURE_DIAGNOSTIC_CONTRACT_PASS retained=true nativeLifetime=true soundCounts=true hitFaultSeparate=true observerReplacement=true noAdmission=true noSaveSchema=true parity=unverified");
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
