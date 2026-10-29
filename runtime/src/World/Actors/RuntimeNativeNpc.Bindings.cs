namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private void ReleaseSourceBindings()
    {
        if (_aiReferenceState is not { } state) return;
        ReleasePendingSandboxStartBindings();
        if (ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery)) state.QueryCurrentPackage = null;
        if (ReferenceEquals(state.QueryLastIdleRequest, _lastIdleRequestQuery)) state.QueryLastIdleRequest = null;
        if (ReferenceEquals(state.QuerySitting, _sittingQuery)) state.QuerySitting = null;
        if (ReferenceEquals(state.QuerySleeping, _sleepingQuery)) state.QuerySleeping = null;
        if (ReferenceEquals(state.CanCaptureIdleRequests, _idleRequestsReady)) state.CanCaptureIdleRequests = null;
        if (ReferenceEquals(state.CaptureIdleRequests, _idleRequestsCapture)) state.CaptureIdleRequests = null;
        if (ReferenceEquals(state.CanCaptureSandboxMarker, _sandboxMarkerReady)) state.CanCaptureSandboxMarker = null;
        if (ReferenceEquals(state.CaptureSandboxMarker, _sandboxMarkerCapture)) state.CaptureSandboxMarker = null;
        if (ReferenceEquals(state.CapturePackageAssignment, _packageAssignmentCapture)) state.CapturePackageAssignment = null;
        if (ReferenceEquals(state.CanCapturePendingPackageSelection, _pendingSelectionReady)) state.CanCapturePendingPackageSelection = null;
        if (ReferenceEquals(state.CapturePendingPackageSelection, _pendingSelectionCapture)) state.CapturePendingPackageSelection = null;
        if (ReferenceEquals(state.CanCapturePackageBindingFailure, _bindingFailureReady)) state.CanCapturePackageBindingFailure = null;
        if (ReferenceEquals(state.CapturePackageBindingFailure, _bindingFailureCapture)) state.CapturePackageBindingFailure = null;
        if (ReferenceEquals(state.CanCaptureSandboxAdmissionFailure, _sandboxAdmissionReady)) state.CanCaptureSandboxAdmissionFailure = null;
        if (ReferenceEquals(state.CaptureSandboxAdmissionFailure, _sandboxAdmissionCapture)) state.CaptureSandboxAdmissionFailure = null;
        if (ReferenceEquals(state.PendingPackageBindingFiniteVoices, _bindingFailureWait)) state.PendingPackageBindingFiniteVoices = null;
        if (ReferenceEquals(state.ObservePackageBindingCapture, _bindingFailureDiagnostic)) state.ObservePackageBindingCapture = null;
        if (ReferenceEquals(state.CanCaptureSelectionFailure, _selectionFailureReady)) state.CanCaptureSelectionFailure = null;
        if (ReferenceEquals(state.CaptureSelectionFailure, _selectionFailureCapture)) state.CaptureSelectionFailure = null;
        if (ReferenceEquals(state.ObserveSelectionCapture, _selectionCaptureDiagnostic)) state.ObserveSelectionCapture = null;
        if (ReferenceEquals(state.CanCaptureDialogue, _dialogueCaptureReady)) state.CanCaptureDialogue = null;
        if (ReferenceEquals(state.CaptureDialogue, _dialogueCapture)) state.CaptureDialogue = null;
        if (ReferenceEquals(state.CanCaptureFurniture, _furnitureCaptureReady)) state.CanCaptureFurniture = null;
        if (ReferenceEquals(state.CaptureFurniture, _furnitureCapture)) state.CaptureFurniture = null;
    }

    // A failed replacement may have temporarily acquired these callbacks. The
    // original presentation resumes its existing owners without replaying AI.
    internal void RestoreSourceBindings()
    {
        if (!IsInsideTree() || _sourceBindingsRetired || _aiReferenceState is not { } state) return;
        if (state.QueryCurrentPackage is not null && !ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery)) return;
        if (_reservedFurniture is { } furniture && _seat is { } seat &&
            _aiWorld?.ReserveFurnitureSeat(furniture, seat.Index, Appearance.Reference!.Value) != true)
            throw new NotSupportedException("Previous native actor cannot restore its source furniture reservation.");
        state.QueryCurrentPackage ??= _currentPackageQuery;
        state.QueryLastIdleRequest ??= _lastIdleRequestQuery;
        state.QuerySitting ??= _sittingQuery;
        state.QuerySleeping ??= _sleepingQuery;
        state.CanCaptureIdleRequests ??= _idleRequestsReady;
        state.CaptureIdleRequests ??= _idleRequestsCapture;
        state.CanCaptureSandboxMarker ??= _sandboxMarkerReady;
        state.CaptureSandboxMarker ??= _sandboxMarkerCapture;
        state.CapturePackageAssignment ??= _packageAssignmentCapture;
        RebindPendingSandboxStartBindings();
        state.CanCapturePendingPackageSelection ??= _pendingSelectionReady;
        state.CapturePendingPackageSelection ??= _pendingSelectionCapture;
        state.CanCapturePackageBindingFailure ??= _bindingFailureReady;
        state.CapturePackageBindingFailure ??= _bindingFailureCapture;
        state.CanCaptureSandboxAdmissionFailure ??= _sandboxAdmissionReady;
        state.CaptureSandboxAdmissionFailure ??= _sandboxAdmissionCapture;
        state.PendingPackageBindingFiniteVoices ??= _bindingFailureWait;
        state.ObservePackageBindingCapture ??= _bindingFailureDiagnostic;
        state.CanCaptureSelectionFailure ??= _selectionFailureReady;
        state.CaptureSelectionFailure ??= _selectionFailureCapture;
        state.ObserveSelectionCapture ??= _selectionCaptureDiagnostic;
        state.CanCaptureDialogue ??= _dialogueCaptureReady;
        state.CaptureDialogue ??= _dialogueCapture;
        state.CanCaptureFurniture ??= _furnitureCaptureReady;
        state.CaptureFurniture ??= _furnitureCapture;
        state.CaptureHeadTracking ??= _headCapture;
        Combat?.RestoreSourceBindings();
    }
}
