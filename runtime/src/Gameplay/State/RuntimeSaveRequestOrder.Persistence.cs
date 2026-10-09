using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class RuntimeSaveRequestOrder
{
    internal static void ValidateSnapshot(RuntimeSaveRequestOrderSnapshot snapshot, string source,
        FalloutPluginStack records, IReadOnlyList<FalloutCompiledSliceReceipt> retainedSuspensions)
    {
        if (snapshot is null || snapshot.Schema != Schema || snapshot.SourceCompatibilityId != source ||
            snapshot.Epoch == Guid.Empty || snapshot.Requests is null || snapshot.Handoffs is null ||
            snapshot.Requests.Any(row => row is null) || snapshot.Handoffs.Any(row => row is null) ||
            snapshot.Requests.Select(row => row.Order).Distinct().Count() != snapshot.Requests.Count ||
            snapshot.Requests.Select(row => row.Request).Distinct().Count() != snapshot.Requests.Count ||
            snapshot.Requests.Where(row => row.Native is not null).Select(row => (row.Native!.Session, row.Native.Generation)).Distinct().Count() !=
                snapshot.Requests.Count(row => row.Native is not null) ||
            snapshot.Requests.Count != 0 && snapshot.Requests[^1].Order != snapshot.LastOrder ||
            snapshot.Requests.Count == 0 && snapshot.LastOrder != 0)
            throw new InvalidDataException("Save queue lacks its complete current ordered denominator/selection.");
        (snapshot.Process ?? throw new InvalidDataException("Saved queue process owner is absent.")).Validate();
        ulong prior = 0;
        var unresolved = false;
        var requestPhases = new Dictionary<Guid, ulong>();
        foreach (var row in snapshot.Requests)
        {
            if (row is null || row.Order != checked(prior + 1) || row.Request == Guid.Empty || row.Epoch == Guid.Empty || (row.HandoffEpoch ?? row.Epoch) != snapshot.Epoch ||
                (row.HandoffEpoch is null) != (row.HandoffPhase is null) ||
                (row.Epoch == snapshot.Epoch) != (row.HandoffEpoch is null) ||
                row.HandoffPhase is { } rebased && (snapshot.Handoffs.Count == 0 || rebased != snapshot.Handoffs[^1].LoadedPhase) ||
                !Enum.IsDefined(row.Origin) || !Enum.IsDefined(row.Destination) || !Enum.IsDefined(row.Disposition) ||
                !Path.IsPathFullyQualified(row.DestinationPath) ||
                (row.Disposition is RuntimeSaveRequestDisposition.Failed or RuntimeSaveRequestDisposition.Cancelled) != (row.Error is not null) ||
                row.Error is not null && string.IsNullOrWhiteSpace(row.Error) ||
                (row.Disposition == RuntimeSaveRequestDisposition.Completed) != (row.Committed is not null && row.CommittedSha256 is not null) ||
                row.Disposition != RuntimeSaveRequestDisposition.Completed && (row.Committed is not null || row.CommittedSha256 is not null))
                throw new InvalidDataException("Saved request order/origin/destination/disposition is incomplete.");
            if (requestPhases.TryGetValue(row.Epoch, out var previousPhase) && row.RequestedPhase < previousPhase)
                throw new InvalidDataException("Saved source/native request phase order regressed within its original epoch.");
            requestPhases[row.Epoch] = row.RequestedPhase;
            if (row.Epoch != snapshot.Epoch && !snapshot.Handoffs.Any(handoff => handoff.PreviousEpoch == row.Epoch && row.Order <= handoff.LastOrder))
                throw new InvalidDataException("Saved request has no actual originating epoch handoff.");
            if (unresolved && row.Disposition == RuntimeSaveRequestDisposition.Completed)
                throw new InvalidDataException("Saved completed writer overtook an earlier unresolved request.");
            unresolved |= row.Disposition is RuntimeSaveRequestDisposition.Pending or RuntimeSaveRequestDisposition.Writing or RuntimeSaveRequestDisposition.Failed;
            prior = row.Order;
            var script = row.Origin is RuntimeSaveRequestOrigin.ScriptAutoSave or RuntimeSaveRequestOrigin.ScriptForceSave;
            var expectedDestination = row.Origin is RuntimeSaveRequestOrigin.ScriptForceSave or RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu
                ? RuntimeSaveRequestDestination.NewSlot : RuntimeSaveRequestDestination.Continue;
            if (row.Destination != expectedDestination || script != (row.Script is not null && row.Invocation is not null) || script == (row.Native is not null) ||
                !script && (row.Script is not null || row.Invocation is not null) ||
                row.Disposition == RuntimeSaveRequestDisposition.Cancelled && row.Origin is not (RuntimeSaveRequestOrigin.PlayerInput or RuntimeSaveRequestOrigin.SessionMenu))
                throw new InvalidDataException("Saved request substitutes its source/native origin or destination.");
            if (row.Script is { } site)
            {
                ValidateScript(records, row, site, retainedSuspensions);
            }
            if (row.Native is { } native) ValidateNative(records, row.Origin, native);
            if (row.Disposition == RuntimeSaveRequestDisposition.Completed && row.Invocation?.Disposition is
                RuntimeSaveInvocationDisposition.Entered or RuntimeSaveInvocationDisposition.Suspended or RuntimeSaveInvocationDisposition.Abandoned)
                throw new InvalidDataException("Saved completed writer lacks its actually retired requesting invocation.");
            if (row.Committed is { } committed &&
                (committed.Id != (row.Destination == RuntimeSaveRequestDestination.NewSlot ? row.Request.ToString("N") : "current") ||
                !Path.GetFullPath(committed.Path).Equals(row.DestinationPath, StringComparison.OrdinalIgnoreCase) || !Hash(row.CommittedSha256)))
                throw new InvalidDataException("Saved completed request lacks its original committed destination/digest.");
        }
        var writing = snapshot.Requests.Where(row => row.Disposition == RuntimeSaveRequestDisposition.Writing).ToArray();
        if (writing.Length != (snapshot.CapturedOrder is null ? 0 : 1) || writing.Length == 1 &&
            (writing[0].Order != snapshot.CapturedOrder || snapshot.Requests.Any(row => row.Order < writing[0].Order &&
                row.Disposition is not (RuntimeSaveRequestDisposition.Completed or RuntimeSaveRequestDisposition.Cancelled)) ||
                writing[0].Invocation?.Disposition is RuntimeSaveInvocationDisposition.Entered or RuntimeSaveInvocationDisposition.Suspended))
            throw new InvalidDataException("Captured save head overtakes an earlier unresolved request.");
        Guid? previousCurrent = null;
        RuntimeSaveProcessIdentity? previousProcess = null;
        var epochs = new HashSet<Guid>();
        foreach (var handoff in snapshot.Handoffs)
        {
            if (handoff is null || handoff.PreviousEpoch == Guid.Empty || handoff.CurrentEpoch == Guid.Empty ||
                handoff.PreviousEpoch == handoff.CurrentEpoch ||
                previousCurrent is { } expected && expected != handoff.PreviousEpoch || handoff.LastOrder > snapshot.LastOrder ||
                !Path.IsPathFullyQualified(handoff.LoadedPath) || !Hash(handoff.LoadedSha256) ||
                handoff.PreviousProcess is null || handoff.CurrentProcess is null)
                throw new InvalidDataException("Saved queue process-epoch handoff is malformed or cyclic.");
            handoff.PreviousProcess.Validate(); handoff.CurrentProcess.Validate();
            if (previousCurrent is null) epochs.Add(handoff.PreviousEpoch);
            if (!epochs.Add(handoff.CurrentEpoch) || previousProcess is not null && previousProcess != handoff.PreviousProcess)
                throw new InvalidDataException("Saved queue epoch/process handoff is cyclic or changes its actual predecessor.");
            if (!Enum.IsDefined(handoff.Kind) || (handoff.Kind == RuntimeSaveRequestHandoffKind.SameProcessSession) !=
                handoff.PreviousProcess.SameNativeProcess(handoff.CurrentProcess))
                throw new InvalidDataException("Saved queue invents a cold process handoff.");
            previousCurrent = handoff.CurrentEpoch;
            previousProcess = handoff.CurrentProcess;
        }
        if (previousCurrent is { } last && last != snapshot.Epoch)
            throw new InvalidDataException("Saved queue handoff does not end at its captured process epoch.");
        if (previousProcess is not null && previousProcess != snapshot.Process)
            throw new InvalidDataException("Saved queue handoff does not end at its captured native process identity.");
    }

    private static void ValidateScript(FalloutPluginStack records, RuntimeSaveRequest row,
        FalloutScriptManualSaveSite site, IReadOnlyList<FalloutCompiledSliceReceipt> suspended)
    {
        var lease = row.Invocation!;
        if (site.Invocation == 0 || site.Statement < 0 || lease.Invocation != site.Invocation || lease.Session != row.Epoch ||
            !Hash(site.RecordSha256) || !Hash(site.ProgramSha256) || !Enum.IsDefined(site.Authority) || !Enum.IsDefined(lease.Disposition) ||
            !Convert.ToHexString(SHA256.HashData(records.GetEffective(site.Program).ReadData())).Equals(site.RecordSha256, StringComparison.OrdinalIgnoreCase) ||
            records.RuntimeFormId(site.Caller) != 0x14 && records.GetEffective(site.Caller).Signature is not ("QUST" or "REFR" or "ACHR" or "ACRE" or "SCPT"))
            throw new InvalidDataException("Saved request invocation differs from its winning caller/program/hash.");
        if (site.Authority == FalloutScriptResultAuthority.CompiledVanilla)
        {
            (site.CompiledProgram ?? throw new InvalidDataException("Saved SCDA request lacks its exact parsed scope."))
                .RequireSave(records, site, row.Origin);
        }
        else throw new NotSupportedException("Diagnostic statement identity cannot authorize persistent script save execution.");
        if (lease.Disposition == RuntimeSaveInvocationDisposition.Entered ||
            (lease.Disposition is RuntimeSaveInvocationDisposition.Stopped or RuntimeSaveInvocationDisposition.Abandoned) != (lease.Error is not null) ||
            lease.Error is not null && string.IsNullOrWhiteSpace(lease.Error) ||
            (lease.Disposition == RuntimeSaveInvocationDisposition.Suspended) != (lease.SuspendedSlice is not null) ||
            (lease.Disposition is RuntimeSaveInvocationDisposition.Completed or RuntimeSaveInvocationDisposition.Stopped or RuntimeSaveInvocationDisposition.Abandoned) !=
                (lease.RetiredSession is not null && lease.RetiredInvocation is > 0) || (lease.RetiredSession is null) != (lease.RetiredInvocation is null) || lease.RetiredSession == Guid.Empty)
            throw new InvalidDataException("Saved request lacks its actual retired invocation or captured suspended suffix.");
        if (lease.SuspendedSlice is { } slice)
        {
            if (slice.Disposition != "suspended" || slice.Caller != site.Caller || slice.Program != site.Program ||
                slice.ProgramSha256 != site.ProgramSha256 || slice.EventScopeSha256 != site.ScopeSha256 ||
                site.Statement >= slice.Cursor.NextOffset || slice.Cursor.Completed ||
                !suspended.Any(actual => JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(slice)))
                throw new InvalidDataException("Pending request's suffix is not the actual saved scheduler slice.");
            slice.Require(site.CompiledProgram!.Require(records, site), site.Caller);
        }
    }

    internal static void ValidateNative(FalloutPluginStack records, RuntimeSaveRequestOrigin origin, RuntimeSaveNativeSite site)
    {
        if (site.Session == Guid.Empty || site.Generation == 0 || records.RuntimeFormId(site.Player) != 0x14 ||
            records.GetEffective(site.Cell).Signature != "CELL" ||
            site.PreviousCell is { } previous && records.GetEffective(previous).Signature != "CELL")
            throw new InvalidDataException("Save native origin lacks its real player/cell/session generation.");
        if ((site.Reference is null) != (site.ReferenceSha256 is null) || (site.ArrivalDoor is null) != (site.ArrivalDoorSha256 is null))
            throw new InvalidDataException("Native save reference has no exact source identity.");
        foreach (var (reference, digest) in new[] { (site.Reference, site.ReferenceSha256), (site.ArrivalDoor, site.ArrivalDoorSha256) })
            if (reference is { } key && (!Hash(digest) || records.GetEffective(key).Signature is not ("REFR" or "ACHR" or "ACRE") ||
                !Convert.ToHexString(SHA256.HashData(records.GetEffective(key).ReadData())).Equals(digest, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Native save reference/transport differs from its winning record.");
        if (origin == RuntimeSaveRequestOrigin.NativeDoorTransport && site.Reference is { } sourceDoor && site.ArrivalDoor is { } arrivalDoor &&
            FalloutCellSceneReader.ReadTeleport(records.GetEffective(sourceDoor))?.Door != arrivalDoor)
            throw new InvalidDataException("Native door save does not bind its actual source XTEL destination.");
        if (origin == RuntimeSaveRequestOrigin.NativeDoorTransport && (site.Reference is null || site.ArrivalDoor is null || site.PreviousCell is null) ||
            origin == RuntimeSaveRequestOrigin.NativeInteraction && site.Reference is null ||
            origin != RuntimeSaveRequestOrigin.NativeDoorTransport && (site.ArrivalDoor is not null || site.PreviousCell is not null))
            throw new InvalidDataException("Native save origin substituted its interaction or transport identity.");
        if ((origin == RuntimeSaveRequestOrigin.NativeRestStart) != (site.Rest is not null))
            throw new InvalidDataException("Native save origin has no matching actual rest-policy request identity.");
        if (site.Rest is { } rest)
        {
            rest.Request.Validate();
            if (rest.RequestOrdinal <= 0 || rest.Request.Origin == FalloutRestOrigin.ScriptHours ||
                !Hash(rest.RestSourceSha256) || !Hash(rest.PolicySha256) || rest.Location.Cell != site.Cell ||
                rest.Request.Bed != site.Reference || rest.Request.BedSha256 != site.ReferenceSha256)
                throw new InvalidDataException("Native rest save replaced its actual menu/source/bed/CELL request.");
            rest.Location.RequireCurrent(records, site.Cell);
            if (rest.Request.BedSource is { } bed)
                bed.RequireCurrent(FalloutSleepWaitBed.ReadCurrent(records, bed.Reference));
        }
    }

    internal static void RequirePublishedCapture(RuntimeSaveRequestOrderSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Requests is null || snapshot.Requests.Any(row => row is null))
            throw new InvalidDataException("Persistent campaign has no complete captured save queue.");
        var writing = snapshot.Requests.SingleOrDefault(row => row.Order == snapshot.CapturedOrder);
        if (snapshot.CapturedOrder is null || writing is not { Disposition: RuntimeSaveRequestDisposition.Writing } ||
            snapshot.Requests.Any(row => row.Order < writing.Order && row.Disposition is not (RuntimeSaveRequestDisposition.Completed or RuntimeSaveRequestDisposition.Cancelled)))
            throw new InvalidDataException("Persistent campaign bytes lack their actual captured head-writer transaction.");
    }

    internal void Restore(RuntimeSaveRequestOrderSnapshot snapshot, string loadedPath, FalloutPluginStack records,
        IReadOnlyList<FalloutCompiledSliceReceipt> retainedSuspensions)
    {
        if (_restored || _requests.Count != 0 || _source is null || snapshot.Epoch == Epoch)
            throw new InvalidOperationException("Cold save ordering requires a fresh process/queue owner.");
        ValidateSnapshot(snapshot, _source, records, retainedSuspensions);
        var path = Path.GetFullPath(loadedPath); var bytes = File.ReadAllBytes(path);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var phase = ObservePhase();
        List<RuntimeSaveRequest> restored = [];
        foreach (var original in snapshot.Requests)
        {
            var expectedPath = Path.GetFullPath(original.Destination == RuntimeSaveRequestDestination.Continue ? _continuePath! : _slotPath!(original.Request));
            if (!expectedPath.Equals(original.DestinationPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cold queued destination differs from the selected session's actual catalog.");
            var row = original with { HandoffEpoch = Epoch, HandoffPhase = phase };
            if (row.Order == snapshot.CapturedOrder)
            {
                // Capture was serialized before publication. Confirm the actual
                // committed destination bytes; a crash before slot publication
                // remains refused and never invents or replays that write.
                if (!File.Exists(expectedPath) || !File.ReadAllBytes(expectedPath).AsSpan().SequenceEqual(bytes))
                    throw new NotSupportedException("Loaded capture has no matching committed destination; its in-flight writer is unresolved.");
                row = row with
                {
                    Disposition = RuntimeSaveRequestDisposition.Completed,
                    Committed = new(row.Destination == RuntimeSaveRequestDestination.NewSlot ? row.Request.ToString("N") : "current",
                        expectedPath, FalloutNativeCampaignSave.ExpectedSchema, null, null, null, File.GetLastWriteTimeUtc(expectedPath)),
                    CommittedSha256 = digest
                };
            }
            restored.Add(row);
        }
        _requests.AddRange(restored);
        _lastOrder = snapshot.LastOrder;
        _handoffs.AddRange(snapshot.Handoffs);
        _handoffs.Add(new(snapshot.Epoch, Epoch, phase, _lastOrder, path, digest, snapshot.Process, RuntimeSaveProcessIdentity.Current,
            snapshot.Process.SameNativeProcess(RuntimeSaveProcessIdentity.Current) ? RuntimeSaveRequestHandoffKind.SameProcessSession : RuntimeSaveRequestHandoffKind.ColdProcess));
        _restored = true;
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
