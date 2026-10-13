using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ActorUpdateCellProcessContracts
{
    private static void LiveSummary(FalloutPluginStack records, string image)
    {
        var scene = FalloutCellSceneReader.Read(records, Cell);
        var owner = new FalloutCellProcesses(FalloutCellProcessDeclaration.ForExecutable(image), records, Selection);
        var lease = owner.BeginAttachment(scene, [Cell], 800);
        var before = JsonSerializer.Serialize(owner.Capture());
        var pending = owner.Summary;
        Require(pending.Cells.Single().Source.Cell == Cell && pending.Cells.Single().Phase == FalloutCellProcessPhase.Attaching &&
            pending.Cells.Single().References == scene.References.Count && pending.Cells.Single().GraphSha256 is not null &&
            pending.Attachments.Single().Children.Pending == scene.References.Count && pending.SaveBlocker == owner.SaveBlocker &&
            pending.TransitionCount != 0 && pending.LastTransition!.Operation == FalloutCellProcessOperation.BeginAttach &&
            before == JsonSerializer.Serialize(owner.Capture()),
            "Live CELL summary invented completion, omitted a blocker, or changed source state.");
        Complete(owner, lease, 8100); owner.PublishRoot(lease, 800, "authored-summary-root-published");
        var saved = RoundTrip(owner.Capture());
        var ready = owner.Summary;
        Require(ready.Attachments.Single().NativeRoot == 800 && ready.Attachments.Single().RootPublished &&
            ready.Attachments.Single().Children.Published == scene.References.Count &&
            ready.Attachments.Single().Children.NativeObjects == scene.References.Count && ready.SaveBlocker is null,
            "CELL summary lost the actual root or source child counts.");
        using var detailed = JsonDocument.Parse(JsonSerializer.Serialize(owner.State));
        using var compact = JsonDocument.Parse(JsonSerializer.Serialize(ready));
        Require(detailed.RootElement.GetProperty("cells")[0].GetProperty("Data").GetProperty("References").GetArrayLength() == scene.References.Count &&
            compact.RootElement.GetProperty("Cells")[0].GetProperty("References").GetInt32() == scene.References.Count &&
            !compact.RootElement.GetProperty("Cells")[0].TryGetProperty("Data", out _) &&
            !compact.RootElement.GetProperty("Attachments")[0].GetProperty("Children").TryGetProperty("Placement", out _),
            "Live CELL summary copied detailed source work or removed its detailed counterpart.");
        var placementReads = 0;
        var cold = new FalloutCellProcesses(FalloutCellProcessDeclaration.ForExecutable(image), records, Selection, saved,
            reference => { placementReads++; return new(reference.Cell, reference.Position.ToArray(), reference.RotationRadians.ToArray()); });
        var coldBefore = JsonSerializer.Serialize(cold.Capture());
        var coldSummary = cold.Summary;
        Require(placementReads == 0 && coldSummary.Cold!.AwaitingNativeAttachments.SequenceEqual([lease]) &&
            coldSummary.Cold.PreviousProcess == saved.CapturedProcess && coldSummary.Attachments.Single().NativeRoot == 0 &&
            !coldSummary.Attachments.Single().RootPublished && coldSummary.SaveBlocker == cold.SaveBlocker &&
            coldSummary.SaveBlocker is not null && coldBefore == JsonSerializer.Serialize(cold.Capture()),
            "Live cold summary entered a source producer or admitted old native identities.");
        cold.Dispose(); Retire(owner, lease, 800); owner.Dispose();

        var failed = new FalloutCellProcesses(FalloutCellProcessDeclaration.ForExecutable(image), records, Selection);
        var failedLease = failed.BeginAttachment(scene, [Cell], 900);
        var children = failed.ReadAttachment(failedLease).Children;
        failed.CompleteChild(failedLease, children[0].Source, FalloutCellProcessChildPhase.Published, [9100], "authored-first-published-child");
        failed.FailChild(failedLease, children[0].Source, new IOException("authored-first-child-failure"), "authored-first-owner");
        failed.FailChild(failedLease, children[1].Source, new IOException("authored-second-child-failure"), "authored-second-owner");
        var failureSummary = failed.Summary;
        Require(failureSummary.SaveBlocker == failed.SaveBlocker && failureSummary.SaveBlocker is not null &&
            failureSummary.Cells.Single().Failure == "authored-first-child-failure" &&
            failureSummary.Attachments.Single().Children.Failed == 2 && failureSummary.Attachments.Single().Children.NativeObjects == 1 &&
            failureSummary.Attachments.Single().ChildFailures.Select(child => (child.Reference, child.Owner, child.Failure)).SequenceEqual(new (FalloutFormKey Reference, string? Owner, string? Failure)[]
            {
                (children[0].Source.Reference, "authored-first-owner", "authored-first-child-failure"),
                (children[1].Source.Reference, "authored-second-owner", "authored-second-child-failure")
            }), "CELL summary lost a secondary failure, source identity, owner or committed native prefix.");
        Retire(failed, failedLease, 900);
        Require(failed.Summary.Attachments.Single().Retired && failed.Summary.Attachments.Single().Children.NativeObjects == 0 &&
            failed.Summary.Attachments.Single().ChildFailures.Count == 2 && failed.Summary.SaveBlocker is not null,
            "Retired live summary erased an original failed child lifetime.");
        failed.Dispose();
    }

    private static void SaveRequestSummary()
    {
        var site = new FalloutScriptManualSaveSite(7, Npc, Key(0x750), new string('a', 64), new string('b', 64), 12);
        var epoch = Guid.NewGuid();
        var invocation = new RuntimeSaveInvocationRetirement(Guid.NewGuid(), 7,
            RuntimeSaveInvocationDisposition.Abandoned, "authored-invocation-failure");
        var request = new RuntimeSaveRequest(3, Guid.NewGuid(), epoch, 23, RuntimeSaveRequestOrigin.ScriptForceSave,
            RuntimeSaveRequestDestination.NewSlot, "authored-destination", site, null,
            invocation,
            RuntimeSaveRequestDisposition.Failed, "authored-writer-failure");
        var status = RuntimeSaveRequestStatus.Read(request);
        Require(status.Order == request.Order && status.Request == request.Request && status.Epoch == epoch &&
            status.RequestedPhase == 23 && status.Disposition == RuntimeSaveRequestDisposition.Failed &&
            status.Error == request.Error && status.InvocationError == invocation.Error &&
            status.InvocationDisposition == RuntimeSaveInvocationDisposition.Abandoned &&
            status.Script!.Caller == site.Caller && status.Script.Program == site.Program && status.Script.Statement == 12,
            "Thin save queue status lost a source identity, failed invocation or writer failure.");
        var receipt = new FalloutScriptManualSaveReceipt(3, request.Request, 23, [site], "Failed", request.Error,
            "authored-destination", [new(7, true, "authored-invocation-failure")]);
        var manual = FalloutScriptManualSaveStatus.Read(receipt)!;
        Require(manual.Sites.Single().ProgramSha256 == site.ProgramSha256 && manual.Invocations!.Single().SourceError == invocation.Error &&
            manual.Disposition == receipt.Disposition && manual.Error == receipt.Error && FalloutScriptManualSaveStatus.Read(null) is null,
            "Thin source manual status lost a source prefix or represented absent state as success.");
        var view = new RuntimeSaveRequestStatusView([status], manual, "source-script-execution", 1, false, request.Error,
            "actual-main-frame-failed", ["main:actual-main-frame-failed", "pcm:disabled-source-emitter"], "unowned-continuations");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view));
        Require(json.RootElement.GetProperty("AllSaveBlockers").GetArrayLength() == 2 &&
            json.RootElement.GetProperty("DeferredBy").GetString() == view.DeferredBy &&
            !json.RootElement.GetProperty("Ordered")[0].GetProperty("Script").TryGetProperty("CompiledProgram", out _),
            "Thin save status dropped independent blockers or copied a compiled program.");
    }
}
