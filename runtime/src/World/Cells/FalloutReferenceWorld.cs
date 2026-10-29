using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceSnapshot(FalloutFormKey Reference, FalloutFormKey Cell,
    FalloutFormKey Base, FalloutFormKey? Script, string? ScriptSha256,
    IReadOnlyDictionary<uint, double> Variables, string? ScriptError, bool? Enabled = null,
    FalloutReferenceEnableRequest? EnableRequest = null, float Opacity = 1, bool? NoFade = null,
    IReadOnlyDictionary<string, FalloutActorValue>? ActorValues = null, bool Destroyed = false, bool DeletePending = false, bool Deleted = false,
    FalloutReferenceInventorySnapshot? Inventory = null, bool Taken = false, bool DoorOpen = false, bool Unlocked = false,
    ulong? SoundRandomState = null, FalloutActorAnimationSnapshot? Animation = null, bool Unconscious = false,
    FalloutMapMarkerState? MapMarker = null, FalloutActorInjury? Injury = null, FalloutActorRagdollState? Ragdoll = null,
    FalloutActorEngagement? Engagement = null, FalloutActorTemplateSnapshot? Templates = null,
    FalloutReferencePlacement? Placement = null, bool Restrained = false, bool PlayerTeammate = false,
    bool TalkedToPlayer = false, FalloutActorPackageMotion? PackageMotion = null,
    FalloutActorHitReaction? HitReaction = null, ulong? HitReactionRandomState = null, bool KnockedDown = false,
    FalloutDestructionState? Destruction = null, IReadOnlyList<FalloutObjectAnimationSnapshot>? ObjectAnimations = null,
    FalloutDoorMotionState? DoorMotion = null, FalloutReferenceLockState? LockState = null,
    FalloutReferenceOwnershipOverride? OwnershipOverride = null,
    IReadOnlyList<FalloutPackageStart>? PackageStarts = null, FalloutPackageEventIdle? PackageIdle = null,
    FalloutFormKey? TalkingActivatorActor = null, FalloutActorPackageAssignment? PackageAssignment = null,
    FalloutActorPackageBindingFailure? PackageBindingFailure = null, bool? BroadcastState = null,
    IReadOnlyList<FalloutReferencePackageEventSnapshot>? PackageEvents = null,
    FalloutActorFurnitureContinuation? FurnitureContinuation = null, FalloutActorSelectionFailure? SelectionFailure = null,
    FalloutActorDialogueContinuation? DialogueContinuation = null, int? DeathCount = null, ulong? AttackRandomState = null,
    FalloutReferenceScriptStoppedFrame? ScriptStoppedFrame = null,
    FalloutReferenceScriptStoppedFrame? CompletedScriptContinuation = null,
    FalloutActorHeadTrackingSnapshot? HeadTracking = null,
    FalloutActorPendingPackageSelection? PendingPackageSelection = null,
    FalloutAnimationSoundEventsSnapshot? AnimationSoundEvents = null,
    FalloutHitReactionFaultsSnapshot? HitReactionFaults = null,
    FalloutActivationRelaySnapshot? ActivationRelay = null,
    FalloutActorCorpseEquipment? CorpseEquipment = null,
    FalloutActorDeferredPackageContinuation? DeferredPackageContinuation = null,
    FalloutAggroRadiusWarning? AggroWarning = null,
    FalloutReferenceGrabEvents? GrabEvents = null,
    IReadOnlyList<FalloutObjectPhysicsSnapshot>? ObjectPhysics = null,
    float? ConversationSuppressionSeconds = null, FalloutConversationHistory? ConversationHistory = null,
    FalloutActorSandboxAdmissionFailure? SandboxAdmissionFailure = null,
    FalloutTerminalHackState? TerminalHackState = null, bool? ObjectHealthPresent = null,
    FalloutActorSandboxMotionFailure? SandboxMotionFailure = null,
    FalloutActorStoppedIdleAnimation? CreatureScriptIdle = null,
    FalloutActorPendingSandboxStart? PendingSandboxStart = null,
    FalloutActorProjectileLaunch? ProjectileLaunch = null, FalloutActorIngestiblesSnapshot? Ingestibles = null,
    FalloutActorSandboxAdmissionFailure? InterruptedSandboxAdmission = null,
    FalloutActorPackageStartLocation? PackageStartLocation = null,
    FalloutSandboxDailyWindow? SandboxDailyWindow = null, uint? IdleMarkerUsedMask = null,
    FalloutActorIdleRequestsSnapshot? IdleRequests = null,
    FalloutActorSandboxMarkerContinuation? SandboxMarker = null,
    FalloutSandboxIdleSelectionSnapshot? SandboxIdleSelection = null)
{
    internal static void Validate(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        var seen = new HashSet<FalloutFormKey>();
        var eventRevisions = new HashSet<long>();
        static bool ValidKey(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || !seen.Add(snapshot.Reference) || !ValidKey(snapshot.Reference) ||
                !ValidKey(snapshot.Cell) || !ValidKey(snapshot.Base) || snapshot.Variables is null ||
                snapshot.Variables.Values.Any(value => !double.IsFinite(value)) ||
                snapshot.Deleted && snapshot.DeletePending || snapshot.DeathCount is < 0 ||
                snapshot.ObjectHealthPresent == true && snapshot.Destruction is null ||
                !float.IsFinite(snapshot.Opacity) || snapshot.Opacity is < 0 or > 1 ||
                (snapshot.Script is null ? snapshot.ScriptSha256 is not null || snapshot.Variables.Count != 0 :
                    !ValidKey(snapshot.Script.Value) || snapshot.ScriptSha256 is not { Length: 64 } || !snapshot.ScriptSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Saved reference state is invalid or duplicated.");
            foreach (var (name, value) in snapshot.ActorValues ?? new Dictionary<string, FalloutActorValue>())
                if ((name is not ("health" or "aggression") && FalloutActorValue.SourceSlot(name) != name) || value is null || !value.IsFinite)
                    throw new InvalidDataException("Saved actor value is invalid.");
            if (snapshot.ConversationSuppressionSeconds is { } suppression && !float.IsFinite(suppression))
                throw new InvalidDataException("Saved conversation suppression is not finite.");
            snapshot.ConversationHistory?.Validate();
            snapshot.PackageStartLocation?.Validate();
            snapshot.SandboxDailyWindow?.Validate();
            snapshot.IdleRequests?.Validate();
            snapshot.SandboxMarker?.Validate();
            snapshot.SandboxIdleSelection?.Validate();
            if (snapshot.IdleMarkerUsedMask is > 1)
                throw new InvalidDataException("Saved idle marker used mask exceeds source slot zero.");
            if (snapshot.Ingestibles is { } ingestibles) FalloutActorIngestiblesSnapshot.Validate(ingestibles);
            if (snapshot.Animation is { } animation) FalloutActorAnimationState.Validate(animation);
            snapshot.GrabEvents?.Validate(snapshot.Reference);
            if (snapshot.ObjectPhysics is { } physics) FalloutObjectPhysicsSnapshot.ValidateSet(physics);
            if (snapshot.ObjectAnimations is { } objectAnimations)
            {
                var controllers = new HashSet<(string, int)>();
                foreach (var state in objectAnimations)
                {
                    if (state is null) throw new InvalidDataException("Saved object animation is absent.");
                    state.Validate();
                    if (!controllers.Add((state.Sha256.ToLowerInvariant(), state.Controller)))
                        throw new InvalidDataException("Saved object animation controller is duplicated.");
                }
            }
            try { snapshot.DoorMotion?.Validate(snapshot.DoorOpen, snapshot.ObjectAnimations); }
            catch (InvalidDataException error)
            { throw new InvalidDataException($"Reference {snapshot.Reference}: {error.Message}", error); }
            snapshot.ActivationRelay?.Validate();
            snapshot.LockState?.Validate();
            snapshot.OwnershipOverride?.Validate();
            snapshot.TerminalHackState?.Validate();
            if (snapshot.TalkingActivatorActor is { } dialogueActor && !ValidKey(dialogueActor))
                throw new InvalidDataException("Saved talking activator actor identity is invalid.");
            snapshot.Placement?.Validate();
            snapshot.Engagement?.Validate();
            snapshot.AggroWarning?.Validate();
            snapshot.ProjectileLaunch?.Validate();
            if (snapshot.AggroWarning is { } warning && (snapshot.Engagement is not null || warning.Target == snapshot.Reference))
                throw new InvalidDataException("Actor cannot warn and engage in combat simultaneously.");
            FalloutActorCorpseEquipment.ValidateSnapshot(snapshot);
            snapshot.HeadTracking?.Validate();
            if (snapshot.HeadTracking is { } head && head.Binding.Actor != snapshot.Reference)
                throw new InvalidDataException("Saved head tracking belongs to a different reference.");
            if (snapshot.Engagement?.AttackRandomState is { } attackRandom && snapshot.AttackRandomState != attackRandom)
                throw new InvalidDataException("Saved attack randomness differs between reference and active engagement.");
            snapshot.PackageMotion?.Validate();
            snapshot.PackageAssignment?.Validate();
            snapshot.PendingSandboxStart?.ValidateShape(snapshot);
            snapshot.DeferredPackageContinuation?.ValidateShape(snapshot);
            snapshot.PackageBindingFailure?.Validate();
            snapshot.SandboxAdmissionFailure?.Validate();
            snapshot.InterruptedSandboxAdmission?.Validate();
            if (snapshot.InterruptedSandboxAdmission is { } interrupted &&
                (snapshot.Injury?.Dead != true || snapshot.Ragdoll is null || snapshot.SandboxAdmissionFailure is not null ||
                 snapshot.PackageMotion is not null || snapshot.PackageIdle is not null ||
                 snapshot.SandboxMotionFailure is not null || snapshot.PackageBindingFailure is not null ||
                 snapshot.SelectionFailure is not null || snapshot.PendingPackageSelection is not null ||
                 snapshot.PendingSandboxStart is not null || snapshot.FurnitureContinuation is not null ||
                 snapshot.DialogueContinuation is not null || snapshot.DeferredPackageContinuation is not null ||
                 snapshot.PackageAssignment is not { } assignment || assignment.Package != interrupted.Package || assignment.Done ||
                 !assignment.Sha256.Equals(interrupted.PackageSha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Interrupted Sandbox history requires its dead actor, captured body and unchanged source assignment.");
            snapshot.SandboxMotionFailure?.Validate();
            snapshot.CreatureScriptIdle?.Validate();
            if (snapshot.CreatureScriptIdle is { } scriptIdle && (scriptIdle.Owner != "script" ||
                scriptIdle.Transition is null ||
                snapshot.Animation is null || snapshot.SoundRandomState is null || snapshot.PackageIdle is not null ||
                snapshot.Engagement is not null || snapshot.Ragdoll is not null || snapshot.Injury?.Dead == true))
                throw new InvalidDataException("Creature script idle has conflicting or missing continuation owners.");
            if (snapshot.SandboxMotionFailure is not null && (snapshot.Animation is null || snapshot.SoundRandomState is null ||
                snapshot.SandboxAdmissionFailure is not null || snapshot.PackageBindingFailure is not null ||
                snapshot.SelectionFailure is not null || snapshot.PendingPackageSelection is not null ||
                snapshot.FurnitureContinuation is not null || snapshot.DialogueContinuation is not null ||
                snapshot.DeferredPackageContinuation is not null))
                throw new InvalidDataException("Stopped Sandbox motion has conflicting or missing continuation owners.");
            if (snapshot.SandboxAdmissionFailure is { } sandbox && (snapshot.Animation is null || snapshot.SoundRandomState is null ||
                snapshot.Unconscious || snapshot.Restrained || sandbox.Activity.InCombat ||
                snapshot.PackageMotion is not null || snapshot.PackageIdle is not null ||
                snapshot.PackageBindingFailure is not null || snapshot.SelectionFailure is not null ||
                snapshot.PendingPackageSelection is not null || snapshot.FurnitureContinuation is not null ||
                snapshot.DialogueContinuation is not null || snapshot.DeferredPackageContinuation is not null ||
                snapshot.Engagement is not null || snapshot.Ragdoll is not null || snapshot.Injury?.Dead == true ||
                snapshot.PackageAssignment is { } sandboxAssignment && (sandboxAssignment.Package != sandbox.Package ||
                    sandboxAssignment.Done || !sandboxAssignment.Sha256.Equals(sandbox.PackageSha256, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Stopped Sandbox admission requires its base clock and exclusive stationary lifecycle.");
            snapshot.SelectionFailure?.Validate();
            snapshot.PendingPackageSelection?.Validate();
            if (snapshot.PendingPackageSelection is not null && (snapshot.Animation is null ||
                snapshot.PackageAssignment is not null || snapshot.PackageBindingFailure is not null ||
                snapshot.SelectionFailure is not null || snapshot.FurnitureContinuation is not null || snapshot.DialogueContinuation is not null))
                throw new InvalidDataException("Pending selection requires its base clock and no active procedure.");
            FalloutActorStoppedPose.Validate(snapshot);
            snapshot.DialogueContinuation?.Validate();
            if (snapshot.DialogueContinuation is { } dialogue && (snapshot.Animation is null ||
                snapshot.PackageAssignment != dialogue.Assignment || snapshot.SelectionFailure is not null ||
                snapshot.FurnitureContinuation is not null || snapshot.PackageBindingFailure is not null))
                throw new InvalidDataException("Dialogue wait requires its base clock and matching exclusive procedure.");
            if (snapshot.SelectionFailure is not null && (snapshot.Animation is null || snapshot.PackageAssignment is not null ||
                snapshot.PackageBindingFailure is not null || snapshot.FurnitureContinuation is not null))
                throw new InvalidDataException("Failed selection requires its base clock and no active procedure.");
            snapshot.FurnitureContinuation?.Validate();
            if (snapshot.FurnitureContinuation is { } furniture && (snapshot.Animation is null ||
                snapshot.PackageBindingFailure is not null || snapshot.PackageAssignment != furniture.Assignment))
                throw new InvalidDataException("Saved furniture requires its base clock and matching package assignment.");
            var packageEvents = new HashSet<(FalloutFormKey, FalloutReferencePackageEventKind)>();
            foreach (var mark in snapshot.PackageEvents ?? [])
            {
                (mark ?? throw new InvalidDataException("Saved actor package event is absent.")).Validate();
                if (!packageEvents.Add((mark.Package, mark.Kind)) || !eventRevisions.Add(mark.Revision))
                    throw new InvalidDataException("Saved actor package event or revision is duplicated.");
            }
            if (snapshot.PackageBindingFailure is not null && (snapshot.PackageAssignment is not null || snapshot.Animation is null))
                throw new InvalidDataException("Saved stopped package binding requires its base clock and no running assignment.");
            foreach (var start in snapshot.PackageStarts ?? [])
                (start ?? throw new InvalidDataException("Saved package selection time is absent.")).Validate();
            snapshot.PackageIdle?.Validate();
            snapshot.AnimationSoundEvents?.Validate();
            snapshot.HitReactionFaults?.Validate();
            if (snapshot.AnimationSoundEvents is { } sounds && sounds.Reference != snapshot.Reference ||
                snapshot.HitReactionFaults is { } faults && faults.Reference != snapshot.Reference)
                throw new InvalidDataException("Saved native event history belongs to a different reference.");
            snapshot.HitReaction?.Validate();
            if (snapshot.KnockedDown && (snapshot.Injury is not { Dead: false } || snapshot.Ragdoll is null && snapshot.HitReaction is null))
                throw new InvalidDataException("Knockdown requires a living actor and a retained physical or recovery pose.");
            if (snapshot.HitReaction is not null && snapshot.Injury is not { Dead: false })
                throw new InvalidDataException("Hit reaction requires a living injured actor.");
            if (snapshot.Ragdoll is { } ragdoll)
            {
                ragdoll.Validate();
                if (snapshot.Injury?.Dead != true && !snapshot.KnockedDown) throw new InvalidDataException("Living reference has a saved ragdoll without knockdown.");
                if (!(ragdoll.Cuts ?? []).Select(cut => cut.Part).Order().SequenceEqual((snapshot.Injury?.SeveredParts ?? []).Order()))
                    throw new InvalidDataException("Saved cut poses disagree with the severed source limbs.");
            }
        }
    }
}

internal sealed partial class FalloutReferenceInstance
{
    // A newly discovered source reference can already have unloaded package
    // state before its first native process is materialized. A restored record
    // with missing process history has a different, explicitly unknown past.
    internal bool HasRestoredSnapshot { get; set; }
    internal bool MayInitializeLaunchProcess(bool hasNativeOwner) => !hasNativeOwner && ProjectileLaunch is null &&
        (!HasRestoredSnapshot || Animation.Resource.Length == 0 && PackageAssignment is null && PackageMotion is null &&
            Engagement is null && AggroWarning is null && SandboxAdmissionFailure is null && InterruptedSandboxAdmission is null && SandboxMotionFailure is null &&
            PendingSandboxStart is null);
    internal FalloutFormKey Reference { get; }
    internal FalloutFormKey Cell { get; }
    internal FalloutFormKey Base { get; }
    internal FalloutReferenceScriptDefinition? Script { get; private set; }
    internal FalloutActorTemplateSelection? Templates { get; set; }
    internal float? ConversationSuppressionSeconds { get; set; }
    internal FalloutConversationHistory? ConversationHistory { get; set; }
    internal FalloutSandboxDailyWindow? SandboxDailyWindow { get; set; }
    internal uint IdleMarkerUsedMask { get; set; }
    internal FalloutActorIdleRequestsSnapshot? IdleRequests { get; set; }
    internal Func<bool>? CanCaptureIdleRequests { get; set; }
    internal Func<FalloutActorIdleRequestsSnapshot?>? CaptureIdleRequests { get; set; }
    internal bool IdleRequestsCaptureReady => CanCaptureIdleRequests?.Invoke() ?? true;
    internal FalloutActorSandboxMarkerContinuation? SandboxMarker { get; set; }
    internal FalloutSandboxIdleSelectionSnapshot? SandboxIdleSelection { get; set; }
    internal Func<bool>? CanCaptureSandboxMarker { get; set; }
    internal Func<FalloutActorSandboxMarkerContinuation?>? CaptureSandboxMarker { get; set; }
    internal bool SandboxMarkerCaptureReady => CanCaptureSandboxMarker?.Invoke() ?? SandboxMarker is not null;
    internal Dictionary<uint, double> Variables { get; }
    internal string? ScriptError { get; set; }
    internal FalloutReferenceScriptStoppedFrame? ScriptStoppedFrame { get; set; }
    internal FalloutReferenceScriptStoppedFrame? CompletedScriptContinuation { get; set; }
    private Action<FalloutReferenceInstance, bool>? _stateChanged;
    internal long WorldRegistrationOrder { get; private set; }
    internal void BindStateChanges(long order, Action<FalloutReferenceInstance, bool> changed)
    {
        WorldRegistrationOrder = order;
        _stateChanged = changed;
        changed(this, true);
    }
    private bool _enabled;
    internal bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; _stateChanged?.Invoke(this, true); }
    }
    private FalloutReferenceEnableRequest? _enableRequest;
    internal FalloutReferenceEnableRequest? EnableRequest
    {
        get => _enableRequest;
        set { if (_enableRequest == value) return; _enableRequest = value; _stateChanged?.Invoke(this, false); }
    }
    private float _opacity = 1;
    internal float Opacity
    {
        get => _opacity;
        set { if (_opacity == value) return; _opacity = value; _stateChanged?.Invoke(this, true); }
    }
    internal bool NoFade { get; set; }
    internal bool Destroyed { get; set; }
    internal FalloutDestructionState? Destruction { get; set; }
    internal bool? ObjectHealthPresent { get; set; } = false;
    internal bool DeletePending { get; set; }
    private bool _deleted, _taken;
    internal bool Deleted
    {
        get => _deleted;
        set { if (_deleted == value) return; _deleted = value; _stateChanged?.Invoke(this, true); }
    }
    internal bool Taken
    {
        get => _taken;
        set { if (_taken == value) return; _taken = value; _stateChanged?.Invoke(this, true); }
    }
    internal bool DoorOpen { get; set; }
    internal FalloutActorProjectileLaunch? ProjectileLaunch { get; set; }
    internal FalloutReferenceGrabEvents? GrabEvents { get; set; }
    internal FalloutDoorMotionState? DoorMotion { get; set; }
    private FalloutActivationRelaySnapshot? _activationRelay;
    internal FalloutActivationRelaySnapshot? ActivationRelay
    {
        get => _activationRelay;
        set { if (_activationRelay == value) return; _activationRelay = value; _stateChanged?.Invoke(this, false); }
    }
    internal bool Unlocked { get; set; }
    internal FalloutReferenceLockState? LockState { get; set; }
    internal FalloutReferenceOwnershipOverride? OwnershipOverride { get; set; }
    internal FalloutTerminalHackState? TerminalHackState { get; set; }
    internal bool Unconscious { get; set; }
    internal bool KnockedDown { get; set; }
    internal bool Restrained { get; set; }
    internal bool PlayerTeammate { get; set; }
    internal bool TalkedToPlayer { get; set; }
    internal bool? BroadcastState { get; set; }
    internal FalloutFormKey? TalkingActivatorActor { get; set; }
    internal FalloutActorPackageMotion? PackageMotion { get; set; }
    internal void ObserveInitialSandboxMotion(FalloutFormKey package)
    {
        if (ProcedureCaptureBlocker != FalloutActorPendingSandboxStart.CaptureBlocker) return;
        if (PackageMotion is not { Sandbox: not null } motion || motion.Package != package)
            throw new InvalidOperationException("Sandbox has no matching published activity and native motion.");
        ProcedureCaptureBlocker = null;
    }
    internal FalloutActorIngestiblesSnapshot? Ingestibles { get; set; }
    internal FalloutActorPackageAssignment? PackageAssignment { get; set; }
    internal FalloutActorDeferredPackageContinuation? DeferredPackageContinuation { get; set; }
    internal Func<FalloutActorPackageAssignment?>? CapturePackageAssignment { get; set; }
    internal FalloutActorPackageBindingFailure? PackageBindingFailure { get; set; }
    internal FalloutActorSandboxAdmissionFailure? SandboxAdmissionFailure { get; set; }
    internal FalloutActorSandboxAdmissionFailure? InterruptedSandboxAdmission { get; set; }
    internal bool InterruptedSandboxAdmissionCaptureReady => Injury?.Dead == true &&
        InterruptedSandboxAdmission is { } history && SandboxAdmissionFailure is null &&
        PackageMotion is null && PackageIdle is null && PackageAssignment is { } assignment &&
        assignment.Package == history.Package && !assignment.Done &&
        assignment.Sha256.Equals(history.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
        ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + history.Error && CorpseEquipmentCaptureReady;
    internal FalloutActorSandboxMotionFailure? SandboxMotionFailure { get; set; }
    internal FalloutActorPendingSandboxStart? PendingSandboxStart { get; set; }
    internal Func<bool>? CanCapturePendingSandboxStart { get; set; }
    internal Func<FalloutActorPendingSandboxStart>? CapturePendingSandboxStart { get; set; }
    internal bool PendingSandboxStartCaptureReady => CanCapturePendingSandboxStart?.Invoke() ??
        PendingSandboxStart is not null && ProcedureCaptureBlocker == FalloutActorPendingSandboxStart.CaptureBlocker;
    internal FalloutActorStoppedIdleAnimation? CreatureScriptIdle { get; set; }
    internal Func<bool>? CanCaptureCreatureScriptIdle { get; set; }
    internal Func<FalloutActorStoppedIdleAnimation?>? CaptureCreatureScriptIdle { get; set; }
    internal bool CreatureScriptIdleCaptureReady => CreatureScriptIdle is null || (CanCaptureCreatureScriptIdle?.Invoke() ?? true);
    internal Func<bool>? CanCaptureSandboxMotionFailure { get; set; }
    internal Func<FalloutActorSandboxMotionFailure>? CaptureSandboxMotionFailure { get; set; }
    internal bool SandboxMotionFailureCaptureReady => CanCaptureSandboxMotionFailure?.Invoke() ??
        SandboxMotionFailure is { } motionFailure && ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + motionFailure.Error;
    internal Func<bool>? CanCaptureSandboxAdmissionFailure { get; set; }
    internal Func<FalloutActorSandboxAdmissionFailure>? CaptureSandboxAdmissionFailure { get; set; }
    internal bool SandboxAdmissionFailureCaptureReady => CanCaptureSandboxAdmissionFailure?.Invoke() ??
        SandboxAdmissionFailure is { } failure && ProcedureCaptureBlocker == FalloutSandboxPackage.CaptureBlockerPrefix + failure.Error;
    internal Func<bool>? CanCapturePackageBindingFailure { get; set; }
    internal Func<FalloutActorPackageBindingFailure>? CapturePackageBindingFailure { get; set; }
    internal Func<IReadOnlyList<OpenNV.Runtime.Content.FalloutFiniteSoundVoice>?>? PendingPackageBindingFiniteVoices { get; set; }
    internal Func<FalloutActorPackageBindingCaptureDiagnostic>? ObservePackageBindingCapture { get; set; }
    internal FalloutActorPackageBindingCaptureDiagnostic? RetiredPackageBindingCaptureDiagnostic { get; set; }
    internal FalloutActorPackageBindingCaptureDiagnostic? PackageBindingCaptureDiagnostic =>
        ObservePackageBindingCapture?.Invoke() ?? RetiredPackageBindingCaptureDiagnostic;
    internal FalloutActorFurnitureContinuation? FurnitureContinuation { get; set; }
    internal FalloutActorSelectionFailure? SelectionFailure { get; set; }
    internal FalloutActorRetirementCandidate? StoppedRetirement { get; set; }
    internal FalloutActorPendingPackageSelection? PendingPackageSelection { get; set; }
    internal Func<bool>? CanCapturePendingPackageSelection { get; set; }
    internal Func<FalloutActorPendingPackageSelection>? CapturePendingPackageSelection { get; set; }
    internal bool PendingPackageSelectionCaptureReady => CanCapturePendingPackageSelection?.Invoke() ??
        PendingPackageSelection is not null && ProcedureCaptureBlocker == FalloutActorPendingPackageSelection.CaptureBlocker;
    internal FalloutActorDialogueContinuation? DialogueContinuation { get; set; }
    internal Func<bool>? CanCaptureDialogue { get; set; }
    internal Func<FalloutActorDialogueContinuation?>? CaptureDialogue { get; set; }
    internal bool DialogueCaptureReady => CanCaptureDialogue?.Invoke() ??
        DialogueContinuation is not null && ProcedureCaptureBlocker == FalloutActorDialogueContinuation.CaptureBlocker;
    internal Func<bool>? CanCaptureSelectionFailure { get; set; }
    internal Func<FalloutActorSelectionFailure>? CaptureSelectionFailure { get; set; }
    internal Func<FalloutActorSelectionCaptureDiagnostic>? ObserveSelectionCapture { get; set; }
    internal FalloutActorSelectionCaptureDiagnostic? RetiredSelectionCaptureDiagnostic { get; set; }
    internal FalloutActorSelectionCaptureDiagnostic? SelectionCaptureDiagnostic =>
        ObserveSelectionCapture?.Invoke() ?? RetiredSelectionCaptureDiagnostic;
    internal bool SelectionFailureCaptureReady => CanCaptureSelectionFailure?.Invoke() ??
        SelectionFailure is { } failure && ProcedureCaptureBlocker == failure.Error;
    internal Func<bool>? CanCaptureFurniture { get; set; }
    internal Func<FalloutActorFurnitureContinuation?>? CaptureFurniture { get; set; }
    internal bool FurnitureCaptureReady => CanCaptureFurniture?.Invoke() ??
        FurnitureContinuation is not null && ProcedureCaptureBlocker == FalloutActorFurnitureContinuation.CaptureBlocker;
    internal bool PackageBindingFailureCaptureReady => CanCapturePackageBindingFailure?.Invoke() ??
        PackageBindingFailure is { } failure && ProcedureCaptureBlocker == failure.Error;
    internal List<FalloutPackageStart> PackageStarts { get; } = [];
    internal FalloutActorPackageStartLocation? PackageStartLocation { get; set; }
    internal FalloutPackageEventIdle? PackageIdle { get; set; }
    internal string? ProcedureCaptureBlocker { get; set; }
    internal FalloutActorHeadTrackingSnapshot? HeadTracking { get; set; }
    internal bool HeadTrackingRequired { get; set; }
    internal string? HeadTrackingCaptureBlocker { get; set; }
    internal Func<FalloutActorHeadTrackingSnapshot>? CaptureHeadTracking { get; set; }
    internal FalloutReferencePlacement? Placement { get; set; }
    internal long PlacementRevision { get; set; }
    internal FalloutReferenceInventory? Inventory { get; set; }
    internal Dictionary<string, FalloutActorValue> ActorValues { get; } = [];
    internal FalloutReferenceEnableParent? EnableParent { get; }
    private FalloutSoundRandomState? _soundRandom;
    private FalloutAnimationSoundEvents? _animationSoundEvents;
    internal FalloutAnimationSoundEvents AnimationSoundEvents => _animationSoundEvents ??= new(Reference, QueryReferenceEnabled);
    private Func<bool>? _referenceEnabled;
    private bool QueryReferenceEnabled() => (_referenceEnabled ??
        throw new NotSupportedException("Source sound emitter has no authoritative reference enable owner."))();
    internal void BindReferenceEnableQuery(Func<bool> query) => _referenceEnabled = query;
    internal bool AnimationSoundsCaptureReady => _animationSoundEvents?.CanCapture != false;
    internal FalloutAnimationSoundHistoryDiagnostic? AnimationSoundCaptureDiagnostic => _animationSoundEvents?.CaptureDiagnostic;
    private FalloutHitReactionFaults? _hitReactionFaults;
    internal FalloutHitReactionFaults HitReactionFaults => _hitReactionFaults ??= new(Reference);
    internal string? CurrentHitReactionError => _hitReactionFaults?.CurrentError;
    internal bool HasHitReactionHistory => _hitReactionFaults is not null;
    internal string? HitReactionFaultCaptureBlocker { get; set; }
    internal FalloutSoundRandomState SoundRandom => _soundRandom ??= new(
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutActorAnimationState Animation { get; } = new();
    private FalloutSoundRandomState? _hitReactionRandom;
    internal FalloutSoundRandomState HitReactionRandom => _hitReactionRandom ??= new(
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private FalloutSoundRandomState? _attackRandom;
    internal FalloutSoundRandomState AttackRandom => _attackRandom ??= new(
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutActorHitReaction? HitReaction { get; set; }
    internal FalloutMapMarkerState? MapMarker { get; set; }
    internal FalloutActorInjury? Injury { get; set; }
    internal int DeathCount { get; set; }
    internal FalloutActorRagdollState? Ragdoll { get; set; }
    internal Func<FalloutActorRagdollState>? CaptureRagdoll { get; set; }
    internal FalloutActorEngagement? Engagement { get; set; }
    internal FalloutAggroRadiusWarning? AggroWarning { get; set; }
    internal Action? StopCombat { get; set; }
    internal Func<FalloutActorEngagement?>? CaptureEngagement { get; set; }
    internal FalloutActorCorpseEquipment? CorpseEquipment { get; set; }
    internal Func<bool, bool>? CanCaptureCorpseEquipment { get; set; }
    internal Func<FalloutActorCorpseEquipment?>? CaptureCorpseEquipment { get; set; }
    internal Func<FalloutActorCorpseEquipment?>? CaptureCorpseEquipmentForFiniteWait { get; set; }
    internal Func<FalloutOpeningInventoryGrant, FalloutOpeningInventoryGrant, FalloutInventoryChangeLease?>? PrepareNativeInventoryRemoval { get; set; }
    internal string? CorpseEquipmentCaptureBlocker { get; private set; }
    internal void SetCorpseEquipmentCaptureBlocker(Func<FalloutActorCorpseEquipment?> owner, string? blocker)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // Prepared or replaced presentations may inspect their own readiness,
        // but only the bound native owner can change reference save admission.
        if (CaptureCorpseEquipment == owner) CorpseEquipmentCaptureBlocker = blocker;
    }
    internal bool CorpseEquipmentCaptureReady => CanCaptureCorpseEquipment?.Invoke(false) ?? CorpseEquipmentCaptureBlocker is null;
    internal IReadOnlyList<FalloutObjectAnimationSnapshot>? ObjectAnimations { get; set; }
    private readonly List<Func<IReadOnlyList<FalloutObjectAnimationSnapshot>>> _objectAnimationCaptures = [];
    internal Func<IReadOnlyList<FalloutObjectAnimationSnapshot>>? CaptureObjectAnimations => _objectAnimationCaptures.LastOrDefault();

    internal void BindObjectAnimationCapture(Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> capture) =>
        _objectAnimationCaptures.Add(capture);

    internal void UnbindObjectAnimationCapture(Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> capture)
    {
        if (!_objectAnimationCaptures.Contains(capture)) throw new InvalidOperationException("Object animation capture was not bound.");
        try
        {
            if (CaptureObjectAnimations == capture) ObjectAnimations = capture();
        }
        finally { _objectAnimationCaptures.Remove(capture); }
    }

    internal FalloutReferenceInstance(FalloutPluginRecord reference, FalloutReferenceScriptDefinition? script,
        Func<bool>? referenceEnabled = null)
    {
        if (reference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new InvalidDataException($"{reference.FormKey} is not a placed reference.");
        Reference = reference.FormKey;
        _referenceEnabled = referenceEnabled;
        Cell = FalloutCellSceneReader.ParentCell(reference) ??
            throw new InvalidDataException($"Reference {Reference} has no source CELL.");
        Base = FalloutDialogueTopic.RequiredForm(reference, "NAME");
        Script = script;
        Enabled = (reference.Flags & 0x800) == 0;
        NoFade = (reference.Flags & 0x08000000) != 0;
        EnableParent = FalloutReferenceEnableParent.Read(reference);
        Variables = script?.Locals.Values.ToDictionary(index => index, _ => 0d) ?? [];
    }

    internal double Read(uint index) => Variables.TryGetValue(index, out var value) ? value :
        throw new NotSupportedException($"Reference {Reference} has no declared variable {index}.");

    internal Func<FalloutFormKey?>? QueryCurrentPackage { get; set; }
    internal Func<int>? QuerySitting { get; set; }
    internal Func<int>? QuerySleeping { get; set; }
    internal Func<FalloutReferencePlacement>? QuerySpatialPlacement { get; set; }

    internal void Write(uint index, double value)
    {
        _ = Read(index);
        if (!double.IsFinite(value)) throw new InvalidDataException("Reference variable is non-finite.");
        Variables[index] = value;
    }

    internal void BindTemplateScript(FalloutReferenceScriptDefinition? script)
    {
        if (Script?.Record.FormKey == script?.Record.FormKey) return;
        if (Variables.Values.Any(value => value != 0) || ScriptError is not null)
            throw new InvalidDataException("Cannot replace an actor script after its execution has started.");
        Script = script; Variables.Clear();
        foreach (var index in script?.Locals.Values ?? []) Variables.Add(index, 0);
    }

    internal FalloutReferenceSnapshot Capture() => CaptureCore(null, null, false);

    // Only the retirement candidate owner calls this before teardown, after
    // validating exact live finite receipts. This copy cannot be written as a save.
    internal FalloutReferenceSnapshot CaptureStoppedRetirementSnapshot(FalloutPluginStack records, ulong nativeOwner,
        FalloutActorSelectionFailure? selection, FalloutActorPackageBindingFailure? binding)
    {
        if ((selection is null) == (binding is null) ||
            FalloutActorRetirementCandidate.ReadLiveFiniteReceipts(records, this, nativeOwner) is null)
            throw new NotSupportedException("Stopped retirement lacks its exact actual finite source/native receipts.");
        return CaptureCore(selection, binding, true);
    }

    private FalloutReferenceSnapshot CaptureCore(FalloutActorSelectionFailure? selectionOverride,
        FalloutActorPackageBindingFailure? bindingOverride, bool nonaudioRetirement)
    {
        if (Injury?.Dead == true && !(CanCaptureCorpseEquipment?.Invoke(nonaudioRetirement) ?? CorpseEquipmentCaptureReady))
            throw new NotSupportedException($"Reference {Reference} cannot save its corpse equipment: {CorpseEquipmentCaptureBlocker ?? "native continuation is not ready"}");
        if (HeadTrackingCaptureBlocker is not null || HeadTrackingRequired && CaptureHeadTracking is null && HeadTracking is null)
            throw new NotSupportedException($"Reference {Reference} cannot save head tracking: {HeadTrackingCaptureBlocker ?? "required owner is missing"}");
        if (HitReactionFaultCaptureBlocker is { } hitFault)
            throw new NotSupportedException($"Reference {Reference} cannot save its hit reaction: {hitFault}");
        var soundEvents = nonaudioRetirement ? null : _animationSoundEvents?.Capture();
        if (!IdleRequestsCaptureReady)
            throw new NotSupportedException($"Reference {Reference} cannot save its idle requests: active source bindings or objects are unowned.");
        var hitReactionFaults = _hitReactionFaults?.Capture();
        if (!CreatureScriptIdleCaptureReady)
            throw new NotSupportedException($"Reference {Reference} cannot save its active creature script idle.");
        var failureReady = bindingOverride is not null || PackageBindingFailureCaptureReady;
        var selectionReady = selectionOverride is not null || SelectionFailureCaptureReady;
        if (ProcedureCaptureBlocker is { } blocker && !failureReady && !FurnitureCaptureReady && !SandboxMarkerCaptureReady && !selectionReady && !DialogueCaptureReady && !PendingPackageSelectionCaptureReady && !SandboxAdmissionFailureCaptureReady && !InterruptedSandboxAdmissionCaptureReady && !SandboxMotionFailureCaptureReady && !PendingSandboxStartCaptureReady &&
            DeferredPackageContinuation?.CanCapture(this) != true)
            throw new NotSupportedException($"Reference {Reference} cannot save: {blocker}");
        var bindingFailure = bindingOverride?.Copy() ?? (failureReady ? CapturePackageBindingFailure is { } captureFailure
            ? captureFailure() : PackageBindingFailure?.Copy() : null);
        return new(Reference, Cell, Base, Script?.Record.FormKey,
            Script?.Sha256, new Dictionary<uint, double>(Variables), ScriptError, Enabled, EnableRequest, Opacity, NoFade,
            new Dictionary<string, FalloutActorValue>(ActorValues), Destroyed, DeletePending, Deleted, Inventory?.Capture(), Taken, DoorOpen, Unlocked,
            _soundRandom?.State, Animation.Capture(), Unconscious, MapMarker,
            Injury is null ? null : Injury with { LimbDamage = new Dictionary<byte, float>(Injury.LimbDamage) }, CaptureRagdoll?.Invoke() ?? Ragdoll,
            CaptureEngagement?.Invoke() ?? Engagement, Templates?.Capture(), Placement?.Copy(), Restrained, PlayerTeammate,
            TalkedToPlayer, PackageMotion?.Copy(), HitReaction?.Copy(), _hitReactionRandom?.State, KnockedDown, Destruction,
            CaptureObjectAnimations?.Invoke() ?? ObjectAnimations, DoorMotion, LockState, OwnershipOverride,
            PackageStarts.Count == 0 ? null : PackageStarts.ToArray(), PackageIdle, TalkingActivatorActor,
            CapturePackageAssignment is { } captureAssignment ? captureAssignment() : PackageAssignment, bindingFailure, BroadcastState,
            FurnitureContinuation: CaptureFurniture is { } captureFurniture ? captureFurniture() : FurnitureContinuation?.Copy(),
            SelectionFailure: selectionOverride?.Copy() ?? (selectionReady ? CaptureSelectionFailure is { } captureSelection
                ? captureSelection() : SelectionFailure?.Copy() : null),
            DialogueContinuation: CaptureDialogue is { } captureDialogue ? captureDialogue() : DialogueContinuation?.Copy(),
            DeathCount: DeathCount == 0 ? null : DeathCount, AttackRandomState: _attackRandom?.State,
            ScriptStoppedFrame: ScriptStoppedFrame?.Copy(), CompletedScriptContinuation: CompletedScriptContinuation?.Copy(),
            HeadTracking: CaptureHeadTracking?.Invoke() ?? HeadTracking?.Copy(),
            PendingPackageSelection: PendingPackageSelectionCaptureReady ? CapturePendingPackageSelection is { } capturePending
                ? capturePending() : PendingPackageSelection?.Copy() : null,
            AnimationSoundEvents: soundEvents, HitReactionFaults: hitReactionFaults, ActivationRelay: ActivationRelay?.Copy(),
            CorpseEquipment: nonaudioRetirement && CaptureCorpseEquipmentForFiniteWait is { } finiteEquipment
                ? finiteEquipment() : CaptureCorpseEquipment is { } captureEquipment ? captureEquipment() : CorpseEquipment?.Copy(),
            DeferredPackageContinuation: DeferredPackageContinuation, AggroWarning: AggroWarning, GrabEvents: GrabEvents,
            ObjectPhysics: CaptureObjectPhysics?.Invoke() ?? ObjectPhysics?.Select(body => body.Copy()).ToArray(),
            ConversationSuppressionSeconds: ConversationSuppressionSeconds, ConversationHistory: ConversationHistory?.Copy(),
            SandboxAdmissionFailure: SandboxAdmissionFailureCaptureReady ? CaptureSandboxAdmissionFailure is { } captureSandbox
                ? captureSandbox() : SandboxAdmissionFailure?.Copy() : null,
            TerminalHackState: TerminalHackState, ObjectHealthPresent: ObjectHealthPresent,
            SandboxMotionFailure: SandboxMotionFailureCaptureReady ? CaptureSandboxMotionFailure is { } captureSandboxMotion
                ? captureSandboxMotion() : SandboxMotionFailure?.Copy() : null,
            CreatureScriptIdle: CreatureScriptIdle is not null && CaptureCreatureScriptIdle is { } captureScriptIdle
                ? captureScriptIdle()?.Copy() : CreatureScriptIdle?.Copy(),
            PendingSandboxStart: PendingSandboxStartCaptureReady ? CapturePendingSandboxStart is { } captureSandboxStart
                ? captureSandboxStart() : PendingSandboxStart?.Copy() : null,
            ProjectileLaunch: ProjectileLaunch, Ingestibles: Ingestibles?.Copy(),
            InterruptedSandboxAdmission: InterruptedSandboxAdmissionCaptureReady ? InterruptedSandboxAdmission?.Copy() : null,
            PackageStartLocation: PackageStartLocation?.Copy(), SandboxDailyWindow: SandboxDailyWindow,
            IdleMarkerUsedMask: IdleMarkerUsedMask == 0 ? null : IdleMarkerUsedMask,
            IdleRequests: CaptureIdleRequests is { } captureIdles ? captureIdles() : IdleRequests?.Copy(),
            SandboxMarker: CaptureSandboxMarker is { } captureMarker ? captureMarker() : SandboxMarker?.Copy(),
            SandboxIdleSelection: SandboxIdleSelection);
    }
}

internal sealed class FalloutReferenceScriptDefinition(FalloutPluginRecord record)
{
    internal FalloutPluginRecord Record { get; } = record;
    internal string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    internal IReadOnlyDictionary<string, uint> Locals { get; } = FalloutScriptLocals.Read(record);
    internal IReadOnlyDictionary<string, FalloutScriptLocalDeclaration> Declarations { get; } =
        FalloutScriptLocals.ReadDeclarations(record);
    internal IReadOnlySet<FalloutFormKey> ReferencedForms { get; } = record.ReadSubrecords()
        .Where(field => field.Signature == "SCRO" && field.Data.Length == 4)
        .Select(field => record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)))
        .ToHashSet();
}

// World lifetime is independent of draw/resource lifetime. A disabled or model-less
// reference still owns its script state. Unloading a cell suspends its residency;
// it cannot reset variables shared with scripts in other cells.
internal sealed partial class FalloutReferenceWorld(FalloutPluginStack records,
    FalloutScriptValueStore? scriptValues = null, FalloutAuxiliaryStore? auxiliary = null,
    FalloutScriptIniStore? ini = null, FalloutUiComponentStore? ui = null, FalloutInputControls? controls = null,
    FalloutFaceGeometryControls? faceControls = null) : IDisposable
{
    internal FalloutScriptValueStore ScriptValues { get; } = scriptValues ?? new();
    internal FalloutAuxiliaryStore Auxiliary { get; } = auxiliary ?? new();
    internal FalloutScriptIniStore? Ini { get; } = ini;
    internal FalloutUiComponentStore? Ui { get; } = ui;
    internal FalloutInputControls? Controls { get; } = controls;
    internal FalloutPlayerMoves PlayerMoves { get; } = new();
    internal FalloutPlayerNotes PlayerNotes { get; } = new(records);
    internal FalloutPlayerCaravanCards PlayerCaravanCards { get; } = new(records);
    internal FalloutScriptMenus Menus { get; } = new();
    private FalloutReferencePackageEvents? _packageEvents;
    internal FalloutReferencePackageEvents PackageEvents => _packageEvents ??= new(records);
    internal int PendingPackageEventCount => _packageEvents?.PendingCount ?? 0;
    private FalloutReferenceHitEvents? _hitEvents;
    internal FalloutReferenceHitEvents HitEvents => _hitEvents ??= new(records);
    internal int PendingHitEventCount => _hitEvents?.PendingCount ?? 0;
    private FalloutScriptSounds? _sounds;
    internal FalloutScriptSounds Sounds => _sounds ??= new(records, Menus);
    private FalloutPipBoyRadio? _pipBoyRadio;
    internal FalloutPipBoyRadio PipBoyRadio => _pipBoyRadio ??= new(records);
    private FalloutNoActivationSound? _noActivationSound;
    internal FalloutNoActivationSound NoActivationSound => _noActivationSound ??= new(records, Sounds);
    private FalloutScreenBlood? _screenBlood;
    internal FalloutScreenBlood ScreenBlood => _screenBlood ??= new(records);
    private readonly Dictionary<FalloutFormKey, FalloutReferenceInstance> _instances = [];
    private readonly Dictionary<FalloutFormKey, FalloutReferenceScriptDefinition> _definitions = [];
    private readonly Dictionary<FalloutFormKey, FalloutReferenceScriptDefinition?> _baseScriptDefinitions = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutReferenceInstance>> _residentCells = [];
    private readonly Dictionary<FalloutFormKey, int> _residentReferences = [];
    private FalloutReferenceInstance[]? _residentSnapshot;
    private bool _disposed;

    internal int InstanceCount => _instances.Count;
    internal int ResidentCellCount => _residentCells.Count;
    internal bool IsCellResident(FalloutFormKey cell) => _residentCells.ContainsKey(cell);
    internal int ScriptDefinitionCount => _definitions.Count;
    internal IEnumerable<FalloutReferenceInstance> ResidentInstances => _residentReferences.Keys.Select(key => _instances[key]);
    internal IReadOnlyList<FalloutReferenceInstance> ResidentSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _residentSnapshot ??= ResidentInstances.ToArray();
    }
    internal bool IsResident(FalloutFormKey reference) => _residentReferences.ContainsKey(reference);
    internal FalloutReferenceInstance Retained(FalloutFormKey reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _instances.TryGetValue(reference, out var instance) ? instance :
            throw new InvalidOperationException($"Reference {reference} has no retained world instance.");
    }
    internal bool CanActivate(FalloutFormKey reference) => IsEnabled(reference) && Get(reference).Templates?.Absent != true &&
        Get(reference) is { Destroyed: false, DeletePending: false, Deleted: false };

    internal double ReadVariable(FalloutQuestState quests, FalloutFormKey owner, uint index) =>
        records.GetEffective(owner).Signature == "QUST" ? quests.Variable(owner, index) : Get(owner).Read(index);

    internal void WriteVariable(FalloutQuestState quests, FalloutFormKey owner, uint index, double value)
    {
        if (records.GetEffective(owner).Signature == "QUST") quests.SetVariable(owner, index, value);
        else Get(owner).Write(index, value);
    }

    private FalloutTransientExplosions? _transientExplosions;
    internal FalloutTransientExplosions TransientExplosions => _transientExplosions ??= new(records);

    internal void ValidateValueHandles()
    {
        foreach (var instance in _instances.Values)
        {
            if (instance.Script is null) continue;
            foreach (var declaration in instance.Script.Declarations.Values)
            {
                if (declaration.Kind == FalloutScriptLocalKind.Form)
                    TransientExplosions.ValidateFormHandle(instance.Read(declaration.Index));
                ScriptValues.ValidateLocal(declaration.Kind, instance.Read(declaration.Index),
                    $"{instance.Reference}:{declaration.Index}");
            }
        }
    }

    internal IReadOnlyList<FalloutReferenceInstance> LoadCell(FalloutCellScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_residentCells.ContainsKey(scene.Cell.FormKey))
            throw new InvalidOperationException($"Cell {scene.Cell.FormKey} is already resident.");
        var instances = scene.References.Select(reference => Get(reference.FormKey)).ToArray();
        foreach (var instance in instances.Where(instance => instance.DeletePending && !_residentReferences.ContainsKey(instance.Reference)))
        { instance.Deleted = true; instance.DeletePending = false; }
        // Exterior residency includes persistent references whose source parent
        // is the world cell. Source ancestry remains unchanged in every instance.
        if (instances.Where((instance, index) => instance.Cell != scene.References[index].Cell).Any() ||
            instances.Select(instance => instance.Reference).Distinct().Count() != instances.Length)
            throw new InvalidDataException("Resident cell has conflicting reference ownership.");
        _residentSnapshot = null;
        _residentCells.Add(scene.Cell.FormKey, instances);
        _placementSourceCells.TryAdd(scene.Cell.FormKey, scene);
        foreach (var reference in scene.References)
            _placementSourceReferences.TryAdd(reference.FormKey, reference);
        foreach (var instance in instances)
            _residentReferences[instance.Reference] = _residentReferences.GetValueOrDefault(instance.Reference) + 1;
        _residentSnapshot = null;
        return instances;
    }

    internal void UnloadCell(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_residentCells.Remove(cell, out var instances)) throw new InvalidOperationException($"Cell {cell} is not resident.");
        _residentSnapshot = null;
        foreach (var instance in instances)
        {
            var remaining = _residentReferences[instance.Reference] - 1;
            if (remaining > 0) { _residentReferences[instance.Reference] = remaining; continue; }
            _residentReferences.Remove(instance.Reference);
            if (instance.DeletePending) { instance.Deleted = true; instance.DeletePending = false; }
        }
        _residentSnapshot = null;
    }

    internal FalloutReferenceInstance Get(FalloutFormKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instances.TryGetValue(key, out var instance)) return instance;
        return CreateInstance(key);
    }

    private FalloutReferenceInstance CreateInstance(FalloutFormKey key)
    {
        var record = records.GetEffective(key);
        var baseKey = FalloutDialogueTopic.RequiredForm(record, "NAME");
        // Engine primitive bases need no ESM base record; their XPRM reference
        // still has a real identity and lifetime. Other missing bases fail closed.
        if (!_baseScriptDefinitions.TryGetValue(baseKey, out var definition))
        {
            // Winning base records are immutable for this world. Cache only
            // successfully resolved definitions; locals and faults remain on
            // each reference instance and malformed attachments still fail.
            if (records.TryGetEffective(baseKey, out var baseRecord))
            {
                var script = FalloutScriptLocals.AttachedScript(records, baseRecord);
                if (script is not null && !_definitions.TryGetValue(script.FormKey, out definition))
                    _definitions.Add(script.FormKey, definition = new(script));
                _baseScriptDefinitions.Add(baseKey, definition);
            }
            else if (!record.ReadSubrecords().Any(field => field.Signature == "XPRM"))
                throw new InvalidDataException($"Reference {key} has no winning base {baseKey}.");
        }
        var instance = new FalloutReferenceInstance(record, definition, () => IsEnabled(key));
        instance.TerminalHackState = InitialTerminalHackState(instance);
        _instances.Add(key, instance);
        instance.BindStateChanges(_instances.Count, TrackReferenceWork);
        return instance;
    }

    internal FalloutActorTemplateSelection InitializeActorTemplates(FalloutFormKey reference, int level,
        FalloutGlobalState? globals = null)
    {
        var instance = Actor(reference);
        if (instance.Templates is { } retained) return retained;
        var encounter = ActorEncounter(instance, level);
        var selection = new FalloutActorTemplateSelection(encounter.Level,
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))), globals is null ? null : globals.Get,
            encounter.ListLevel, encounter.AllLevels);
        selection.ResolveAll(records, instance.Base);
        instance.Templates = selection;
        BindTemplateScript(instance);
        return selection;
    }

    private void BindTemplateScript(FalloutReferenceInstance instance)
    {
        if (instance.Templates is null || instance.Templates.Absent) return;
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 512, instance.Templates);
        var script = FalloutScriptLocals.AttachedScript(records, owner);
        FalloutReferenceScriptDefinition? definition = null;
        if (script is not null && !_definitions.TryGetValue(script.FormKey, out definition))
            _definitions.Add(script.FormKey, definition = new(script));
        instance.BindTemplateScript(definition);
    }

    private bool PendingProcedureCapture(FalloutReferenceInstance instance) =>
        !instance.IdleRequestsCaptureReady || !instance.CreatureScriptIdleCaptureReady || instance.ProcedureCaptureBlocker is not null && !instance.PackageBindingFailureCaptureReady && !instance.FurnitureCaptureReady && !instance.SandboxMarkerCaptureReady &&
        !instance.SelectionFailureCaptureReady && !instance.DialogueCaptureReady && !instance.PendingPackageSelectionCaptureReady && !instance.SandboxAdmissionFailureCaptureReady && !instance.InterruptedSandboxAdmissionCaptureReady && !instance.SandboxMotionFailureCaptureReady && !instance.PendingSandboxStartCaptureReady &&
        (IsResident(instance.Reference) || instance.DeferredPackageContinuation?.CanCapture(instance) != true);
    internal int PendingProcedureCaptureCount => _instances.Values.Count(PendingProcedureCapture);
    internal int StoppedPackageBindingCount => _instances.Values.Count(instance => instance.PackageBindingFailureCaptureReady);
    internal int PendingAnimationSoundCaptureCount => _instances.Values.Count(instance => !instance.AnimationSoundsCaptureReady);
    internal object PendingAnimationSoundCaptures => _instances.Values.Where(instance => !instance.AnimationSoundsCaptureReady)
        .OrderBy(instance => records.RuntimeFormId(instance.Reference)).Select(instance => new
        {
            reference = instance.Reference.ToString(),
            cell = instance.Cell.ToString(),
            resident = IsResident(instance.Reference),
            history = instance.AnimationSoundCaptureDiagnostic
        }).ToArray();
    internal object PendingProcedureCaptures => _instances.Values.Where(PendingProcedureCapture)
        .Select(instance => new
        {
            reference = instance.Reference.ToString(),
            assignment = instance.PackageAssignment?.Package.ToString(),
            motion = instance.PackageMotion?.Package.ToString(),
            blocker = !instance.IdleRequestsCaptureReady ? "Idle request/sequence continuation." : instance.ProcedureCaptureBlocker,
            bindingCapture = instance.PackageBindingCaptureDiagnostic,
            selectionCapture = instance.SelectionCaptureDiagnostic,
            stoppedRetirement = instance.StoppedRetirement?.State,
        }).ToArray();

    private FalloutScriptManualSaveRequests? _scriptManualSaves;
    internal FalloutScriptManualSaveRequests ScriptManualSaves => _scriptManualSaves ??= new(records);

    internal IReadOnlyList<FalloutReferenceSnapshot> Capture() => CaptureSequence().ToArray();

    // The caller holds the save pause boundary for the entire enumeration.
    // Native continuation callbacks stay on the gameplay thread, but may be
    // distributed across frames instead of capturing every reference at once.
    internal IEnumerable<FalloutReferenceSnapshot> CaptureSequence()
    {
        _scriptManualSaves?.RequireCapture();
        _transientExplosions?.RequireCapture();
        ObjectDisposedException.ThrowIf(_disposed, this);
        PlayerMoves.RequireSettled();
        if (PendingHitEventCount != 0)
            throw new NotSupportedException("Saving pending reference hit events requires their continuation state.");
        if (_instances.Values.Any(instance => instance.DeferredPackageContinuation is not null && IsResident(instance.Reference)))
            throw new NotSupportedException("Resident deferred package requires its actual native handoff before saving.");
        foreach (var actor in _packageEvents?.PendingActors ?? []) _ = Get(actor);
        foreach (var instance in _instances.Values.OrderBy(instance => records.RuntimeFormId(instance.Reference)))
            yield return instance.Capture() with
            {
                PackageEvents = _packageEvents?.Capture(instance.Reference) is { Count: > 0 } events ? events : null
            };
    }

    internal void Restore(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instances.Count != 0 || PendingPackageEventCount != 0) throw new InvalidOperationException("Reference restoration requires a fresh world.");
        FalloutReferenceSnapshot.Validate(snapshots);
        using var validated = new FalloutReferenceWorld(records);
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || validated._instances.ContainsKey(snapshot.Reference))
                throw new InvalidDataException("Saved reference is absent or duplicated.");
            var instance = validated.Get(snapshot.Reference);
            instance.HasRestoredSnapshot = true;
            if (snapshot.ObjectPhysics is not null && records.GetEffective(snapshot.Reference).Signature is not ("REFR" or "PGRE" or "PMIS"))
                throw new InvalidDataException($"Object physics for {snapshot.Reference} cannot replace an actor continuation owner.");
            if (snapshot.Templates is { } templates)
            {
                instance.Templates = new(templates);
                instance.Templates.ResolveAll(records, instance.Base);
                validated.BindTemplateScript(instance);
            }
            if (snapshot.Cell != instance.Cell || snapshot.Base != instance.Base || snapshot.Script != instance.Script?.Record.FormKey ||
                snapshot.ScriptSha256 != instance.Script?.Sha256 || snapshot.Variables is null ||
                !snapshot.Variables.Keys.Order().SequenceEqual(instance.Variables.Keys.Order()))
                throw new InvalidDataException($"Saved reference {snapshot.Reference} differs from its winning source declaration.");
            foreach (var (index, value) in snapshot.Variables) instance.Write(index, value);
            // Older builds incorrectly retained failed engine default actions
            // as source-program faults even on objects with no script.
            // Parsing executes no source statements. Retry it after a cold
            // load so a parser correction can recover an existing save.
            // Reached execution failures retain their applied prefix/error.
            // PACK result scripts belong to their package, so even an actor
            // without an attached script can retain their reached failure.
            var packageFault = snapshot.ScriptError is { } error &&
                (error.StartsWith("Package POBA ", StringComparison.Ordinal) ||
                 error.StartsWith("Package POCA ", StringComparison.Ordinal) ||
                 error.StartsWith("Package POEA ", StringComparison.Ordinal));
            instance.ScriptError = instance.Script is null && !packageFault ||
                snapshot.ScriptError?.StartsWith("Parse:", StringComparison.OrdinalIgnoreCase) == true
                ? null : snapshot.ScriptError;
            if (snapshot.ScriptStoppedFrame is not null || snapshot.CompletedScriptContinuation is not null)
            {
                if (instance.Script is null) throw new InvalidDataException("Saved stopped frame has no source script.");
                var fields = instance.Script.Record.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
                if (fields.Length != 1) throw new InvalidDataException("Saved stopped frame has no unique source program.");
                var blocks = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(fields[0].Data.Span));
                snapshot.ScriptStoppedFrame?.Validate(instance.ScriptError, instance.Script.Sha256, blocks);
                snapshot.CompletedScriptContinuation?.Validate(snapshot.CompletedScriptContinuation.Error, instance.Script.Sha256, blocks);
                foreach (var retained in new[] { snapshot.ScriptStoppedFrame, snapshot.CompletedScriptContinuation }.OfType<FalloutReferenceScriptStoppedFrame>())
                {
                    if (retained.PreparedDetection is { } request) validated.Detection.ValidateRequest(request);
                    if (retained.Speech is { } receipt)
                        FalloutFinishedSpeechSourceBinding.Require(records, instance, receipt);
                }
                instance.ScriptStoppedFrame = snapshot.ScriptStoppedFrame?.Copy();
                instance.CompletedScriptContinuation = snapshot.CompletedScriptContinuation?.Copy();
            }
            instance.Enabled = snapshot.Enabled ?? instance.Enabled;
            instance.EnableRequest = snapshot.EnableRequest;
            instance.Opacity = snapshot.Opacity;
            instance.NoFade = snapshot.NoFade ?? instance.NoFade;
            instance.Destroyed = snapshot.Destroyed;
            instance.ObjectHealthPresent = snapshot.ObjectHealthPresent;
            if (snapshot.Destruction is { } destruction)
            {
                var declaration = FalloutDestructible.Read(records, instance.Base) ??
                    throw new InvalidDataException("Saved destruction has no source DEST.");
                destruction.Validate(declaration);
                if (snapshot.ObjectHealthPresent == false &&
                    (destruction.Health != declaration.Health || destruction.Stage != -1))
                    throw new InvalidDataException("Missing object health cannot retain applied destruction damage.");
                instance.Destruction = destruction;
            }
            instance.DeletePending = snapshot.DeletePending;
            instance.Deleted = snapshot.Deleted;
            instance.Taken = snapshot.Taken;
            instance.DoorOpen = snapshot.DoorOpen;
            instance.DoorMotion = snapshot.DoorMotion;
            if (snapshot.ActivationRelay is { } relay)
            {
                validated.ValidateActivationRelay(snapshot.Reference, relay);
                instance.ActivationRelay = relay.Copy();
            }
            if (snapshot.Placement is { } placement)
            {
                if (records.GetEffective(placement.Cell).Signature != "CELL") throw new InvalidDataException("Saved placement has no winning CELL.");
                instance.Placement = placement.Copy();
            }
            if (snapshot.PackageStartLocation is { } packageStart)
                validated.SetActorPackageStartLocation(snapshot.Reference, packageStart);
            // Effective access metadata belongs to the restored location, not
            // the editor cell. All staging remains in the temporary world.
            validated.RestoreAccess(instance, snapshot);
            validated.RestoreTerminalHackState(instance, snapshot);
            if (snapshot.BroadcastState is { } broadcast)
                validated.SetBroadcastState(snapshot.Reference, broadcast ? 1 : 0);
            var talkingActivator = snapshot.TalkedToPlayer && records.GetEffective(instance.Base).Signature == "TACT";
            if (talkingActivator) _ = FalloutDialogueSpeaker.Read(records, instance.Base);
            if (snapshot.Restrained || snapshot.PlayerTeammate || snapshot.TalkedToPlayer && !talkingActivator || snapshot.PackageMotion is not null ||
                snapshot.HitReaction is not null || snapshot.HitReactionRandomState is not null || snapshot.AttackRandomState is not null)
                _ = validated.Actor(snapshot.Reference);
            instance.Restrained = snapshot.Restrained;
            instance.PlayerTeammate = snapshot.PlayerTeammate;
            instance.TalkedToPlayer = snapshot.TalkedToPlayer;
            if (snapshot.PackageAssignment is { } assignment)
            {
                _ = validated.Actor(snapshot.Reference);
                var package = records.GetEffective(assignment.Package);
                if (package.Signature != "PACK" || !RecordHash(package).Equals(assignment.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved actor assignment differs from its winning package.");
                instance.PackageAssignment = assignment;
            }
            if (snapshot.DeferredPackageContinuation is { } deferred)
            {
                deferred.Validate(records, snapshot);
                instance.DeferredPackageContinuation = deferred;
                instance.ProcedureCaptureBlocker = FalloutUnloadedActorPackages.ContinuationBlocker;
            }
            if (snapshot.PackageBindingFailure is { } bindingFailure)
            {
                bindingFailure.Validate(records, instance);
                instance.PackageBindingFailure = bindingFailure.Copy();
                instance.ProcedureCaptureBlocker = bindingFailure.Error;
            }
            if (snapshot.TalkingActivatorActor is { } dialogueActor)
                validated.SetTalkingActivatorActor(snapshot.Reference, dialogueActor);
            if (snapshot.PackageMotion is { } motion)
            {
                var stale = snapshot.PackageAssignment is { } currentAssignment && currentAssignment.Package != motion.Package;
                if (!stale)
                {
                    var package = records.GetEffective(motion.Package);
                    if (package.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(package.ReadData()))
                        .Equals(motion.PackageSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Saved package motion differs from the winning package.");
                    if (motion.Patrol is { } patrol) FalloutPatrolRoute.Read(records, package, snapshot.Reference).Validate(patrol);
                    if (motion.Escort is { } escort) { _ = FalloutEscortPackage.Read(package); escort.Validate(); }
                    if (motion.Sandbox is not null)
                        FalloutSandboxPackage.Read(package).RequireRetainedStart(records, instance);
                    if (motion.EditorTravel is { } editorTravel)
                        FalloutEditorTravelPackage.Read(package).Validate(validated, snapshot.Reference, editorTravel);
                    if (motion.DialogueCompleted)
                    {
                        _ = FalloutDialoguePackage.Read(package);
                        var declaration = FalloutScriptPackage.Read(package);
                        if (declaration.LocationType is not (null or 2 or 3) || declaration.LocationRadius != 0)
                            throw new InvalidDataException("Saved dialogue completion has no supported source procedure.");
                    }
                }
                instance.PackageMotion = motion.Copy();
                // Older snapshots retained this exact Travel destination but
                // had no separate actor location field. Only the matching
                // current assignment supplies an unambiguous recovery value.
                if (instance.PackageStartLocation is null && motion.Travel is { } priorTravel &&
                    snapshot.PackageAssignment?.Package == motion.Package &&
                    FalloutScriptPackage.Read(records.GetEffective(motion.Package)).LocationType == 2)
                    validated.SetActorPackageStartLocation(snapshot.Reference, new(priorTravel.Cell, priorTravel.Location));
            }
            validated.RestorePackageTiming(instance, snapshot);
            if (snapshot.ConversationSuppressionSeconds is { } suppression)
            {
                _ = validated.Actor(snapshot.Reference);
                instance.ConversationSuppressionSeconds = suppression;
            }
            if (snapshot.SandboxDailyWindow is { } dailyWindow)
            {
                _ = validated.Actor(snapshot.Reference);
                instance.SandboxDailyWindow = dailyWindow;
            }
            if (snapshot.IdleRequests is { } idleRequests)
            {
                if (records.GetEffective(instance.Base).Signature != "NPC_")
                    throw new InvalidDataException("Saved native idle requests have no NPC source owner.");
                idleRequests.Validate(records);
                instance.IdleRequests = idleRequests.Copy();
            }
            instance.SandboxIdleSelection = snapshot.SandboxIdleSelection;
            if (snapshot.IdleMarkerUsedMask is { } usedMask)
            {
                if (records.GetEffective(instance.Base).Signature != "IDLM")
                    throw new InvalidDataException("Saved idle marker used mask has no winning IDLM source.");
                instance.IdleMarkerUsedMask = usedMask;
            }
            if (snapshot.ConversationHistory is { } history)
            {
                _ = validated.Actor(snapshot.Reference);
                foreach (var partner in history.Partners)
                    if (records.GetEffective(partner).Signature is not ("ACHR" or "ACRE"))
                        throw new InvalidDataException("Saved conversation partner is not an actor reference.");
                instance.ConversationHistory = history.Copy();
            }
            if (snapshot.ProjectileLaunch is { } launch)
            {
                _ = validated.Actor(snapshot.Reference);
                launch.Validate();
                instance.ProjectileLaunch = launch;
            }
            if (snapshot.SoundRandomState is { } soundRandom) instance.SoundRandom.Restore(soundRandom);
            if (snapshot.AnimationSoundEvents is { } soundEvents) instance.AnimationSoundEvents.Restore(soundEvents, records);
            if (snapshot.HitReactionFaults is { } hitFaults)
            {
                _ = validated.Actor(snapshot.Reference);
                hitFaults.Validate();
                if (hitFaults.Faults.Any(fault => !validated.BodyParts(snapshot.Reference).Parts.Any(part => part.Type == fault.Part)))
                    throw new InvalidDataException("Saved hit-reaction fault has no source anatomy.");
                instance.HitReactionFaults.Restore(hitFaults, records);
            }
            if (snapshot.Animation is { } animation) instance.Animation.Restore(animation);
            if (snapshot.CreatureScriptIdle is { } scriptIdle)
            {
                if (records.GetEffective(instance.Base).Signature != "CREA")
                    throw new InvalidDataException("Creature script idle belongs to a non-creature reference.");
                scriptIdle.Validate(records);
                instance.CreatureScriptIdle = scriptIdle.Copy();
            }
            if (snapshot.Unconscious) validated.SetUnconscious(snapshot.Reference, true);
            if (snapshot.MapMarker is { } mapMarker)
            {
                _ = FalloutMapMarker.Read(records.GetEffective(snapshot.Reference));
                instance.MapMarker = mapMarker;
            }
            if (snapshot.Inventory is { } inventory)
            {
                _ = validated.InventoryOwner(snapshot.Reference);
                instance.Inventory = new();
                instance.Inventory.Restore(inventory, records);
            }
            if (snapshot.ActorValues is { Count: > 0 } values)
            {
                _ = validated.Actor(snapshot.Reference);
                foreach (var (name, value) in values) instance.ActorValues.Add(name, value);
            }
            if (snapshot.Injury is { } injury) validated.RestoreInjury(instance, injury);
            else if (instance.ActorValues.ContainsKey("health")) throw new InvalidDataException("Saved health has no actor injury state.");
            if (snapshot.Ingestibles is { } ingestibles)
            {
                validated.ValidateActorIngestibles(instance.Reference, ingestibles);
                instance.Ingestibles = ingestibles.Copy();
            }
            instance.DeathCount = snapshot.DeathCount ?? (snapshot.Injury?.DeathInventoryGranted == true ? 1 : 0);
            if (snapshot.Injury?.DeathInventoryGranted == true && instance.DeathCount < 1)
                throw new InvalidDataException("Saved killed actor has no consumed death history.");
            if (instance.DeathCount != 0)
            {
                _ = validated.Actor(snapshot.Reference);
                if (snapshot.Injury is null)
                    throw new InvalidDataException("Saved death history has no supported actor lifetime.");
            }
            instance.Ragdoll = snapshot.Ragdoll;
            instance.KnockedDown = snapshot.KnockedDown;
            instance.Engagement = snapshot.Engagement;
            instance.GrabEvents = snapshot.GrabEvents;
            if (snapshot.AggroWarning is { } warning)
            {
                _ = validated.Actor(snapshot.Reference);
                if (warning.Target != records.RuntimeFormKey(0x14)) _ = validated.Actor(warning.Target);
                if (FalloutActorThreat.Read(records, instance.Base, instance.Templates) is not { RadiusBehavior: true, Radius: > 0 })
                    throw new InvalidDataException("Saved aggro warning has no source radius behavior.");
                instance.AggroWarning = warning;
            }
            if (snapshot.CorpseEquipment is { } equipment)
            {
                equipment.ValidateSource(records, instance.Inventory?.Contents ??
                    throw new InvalidDataException("Saved corpse equipment has no retained inventory owner."));
                instance.CorpseEquipment = equipment.Copy();
            }
            FalloutActorStoppedPose.ValidateTarget(records, snapshot);
            instance.ObjectAnimations = snapshot.ObjectAnimations?.ToArray();
            instance.ObjectPhysics = snapshot.ObjectPhysics?.Select(body => body.Copy()).ToArray();
            if (snapshot.HitReaction is { } reaction)
            {
                var idle = records.GetEffective(reaction.Idle);
                if (idle.Signature != "IDLE" || !Convert.ToHexString(SHA256.HashData(idle.ReadData()))
                    .Equals(reaction.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
                    !validated.BodyParts(snapshot.Reference).Parts.Any(part => part.Type == reaction.Part))
                    throw new InvalidDataException("Saved hit reaction differs from its source idle or anatomy.");
                instance.HitReaction = reaction.Copy();
            }
            if (snapshot.HitReactionRandomState is { } reactionRandom) instance.HitReactionRandom.Restore(reactionRandom);
            if (snapshot.AttackRandomState is { } attackRandom) instance.AttackRandom.Restore(attackRandom);
            if (snapshot.HeadTracking is { } headTracking)
            {
                FalloutActorHeadTrackingSource.Validate(records, snapshot.Reference, headTracking);
                instance.HeadTracking = headTracking.Copy(); instance.HeadTrackingRequired = true;
            }
            if (instance.EnableRequest is not null && instance.EnableParent is not null)
                throw new InvalidDataException("Saved child reference has an independent enable request.");
        }
        // Reference-marker targets may themselves have saved placements. All
        // placements must be restored before validating their Travel anchors.
        foreach (var snapshot in snapshots)
        {
            if (snapshot.PendingSandboxStart is { } pendingStart)
            {
                var actor = validated.Get(snapshot.Reference);
                pendingStart.Validate(records, actor);
                actor.PendingSandboxStart = pendingStart.Copy();
                actor.ProcedureCaptureBlocker = FalloutActorPendingSandboxStart.CaptureBlocker;
            }
            if (snapshot.SandboxMotionFailure is { } motionFailure)
            {
                var actor = validated.Get(snapshot.Reference);
                motionFailure.Validate(records, actor);
                actor.SandboxMotionFailure = motionFailure.Copy();
                actor.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + motionFailure.Error;
            }
            if (snapshot.InterruptedSandboxAdmission is { } interrupted)
            {
                var actor = validated.Get(snapshot.Reference);
                interrupted.Validate(records, actor);
                actor.InterruptedSandboxAdmission = interrupted.Copy();
                actor.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + interrupted.Error;
            }
            if (snapshot.SandboxAdmissionFailure is { } sandbox)
            {
                var actor = validated.Get(snapshot.Reference);
                sandbox.Validate(records, actor);
                actor.SandboxAdmissionFailure = sandbox.Copy();
                actor.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + sandbox.Error;
            }
            if (snapshot.PendingPackageSelection is { } pendingSelection)
            {
                var actor = validated.Get(snapshot.Reference);
                pendingSelection.Validate(records, actor);
                actor.PendingPackageSelection = pendingSelection.Copy();
                actor.ProcedureCaptureBlocker = FalloutActorPendingPackageSelection.CaptureBlocker;
            }
            if (snapshot.DialogueContinuation is { } dialogue)
            {
                var actor = validated.Get(snapshot.Reference);
                dialogue.Validate(records, actor);
                actor.DialogueContinuation = dialogue.Copy();
                actor.ProcedureCaptureBlocker = FalloutActorDialogueContinuation.CaptureBlocker;
            }
            if (snapshot.SelectionFailure is { } selectionFailure)
            {
                var actor = validated.Get(snapshot.Reference);
                selectionFailure.Validate(records, actor);
                actor.SelectionFailure = selectionFailure.Copy();
                actor.ProcedureCaptureBlocker = selectionFailure.Error;
            }
            if (snapshot.FurnitureContinuation is { } furniture)
            {
                var actor = validated.Get(snapshot.Reference);
                furniture.Validate(records, validated, actor);
                actor.FurnitureContinuation = furniture.Copy();
                actor.ProcedureCaptureBlocker = FalloutActorFurnitureContinuation.CaptureBlocker;
                if (furniture.Furniture is { } seatReference &&
                    !validated._furnitureSeats.TryAdd((seatReference, furniture.Seat!.Index), snapshot.Reference))
                    throw new InvalidDataException("Saved furniture reservation is shared by competing actors.");
            }
            if (snapshot.SandboxMarker is { } marker)
            {
                var actor = validated.Get(snapshot.Reference);
                marker.Validate(records, validated, actor);
                actor.SandboxMarker = marker.Copy();
                actor.ProcedureCaptureBlocker = FalloutActorSandboxMarkerContinuation.CaptureBlocker;
                validated.RestoreIdleMarkerReservation(marker.Marker, snapshot.Reference);
            }
            if (snapshot.PackageMotion is { } staleMotion && snapshot.PackageAssignment is { } assignment &&
                assignment.Package != staleMotion.Package) continue;
            if (snapshot.PackageMotion?.Travel is { } travel)
                FalloutTravelPackage.Read(records.GetEffective(snapshot.PackageMotion.Package),
                    ownsIdleCollection: records.GetEffective(validated.Get(snapshot.Reference).Base).Signature == "NPC_")
                    .Validate(records, validated, snapshot.Reference, travel);
            if (snapshot.PackageMotion?.Guard is { } guard)
                FalloutGuardPackage.Read(records.GetEffective(snapshot.PackageMotion.Package))
                    .Validate(records, validated, snapshot.Reference, guard);
            if (snapshot.PackageMotion?.AccompanyPortal is { } accompanyPortal)
                accompanyPortal.Validate(records, validated, snapshot.Reference, records.GetEffective(snapshot.PackageMotion.Package));
            if (snapshot.PackageMotion?.Wander is { } wander)
                validated.ValidatePackageWander(snapshot.Reference, wander);
            if (snapshot.PackageMotion?.Eat is { } meal) validated.ValidateEatProgress(snapshot.Reference, meal);
            if (snapshot.PackageMotion?.Sandbox is { Activity: FalloutSandboxActivity.Dialogue, Target: { } conversationTarget } &&
                records.GetEffective(conversationTarget).Signature is not ("ACHR" or "ACRE"))
                throw new InvalidDataException("Saved Sandbox conversation target is not an actor reference.");
        }
        validated.PackageEvents.Restore(snapshots);
        _packageEvents = validated._packageEvents;
        validated._packageEvents = null;
        foreach (var (key, instance) in validated._instances)
        {
            // Validation owns a temporary world. Retained sound ledgers must
            // query the destination owner after the successful atomic transfer.
            instance.BindReferenceEnableQuery(() => IsEnabled(key));
            _instances.Add(key, instance);
            instance.BindStateChanges(_instances.Count, TrackReferenceWork);
            BindInventoryRemoval(instance);
        }
        foreach (var (key, definition) in validated._definitions) _definitions.Add(key, definition);
        foreach (var (seat, actor) in validated._furnitureSeats) _furnitureSeats.Add(seat, actor);
        foreach (var (marker, actor) in validated._idleMarkerReservations) _idleMarkerReservations.Add(marker, actor);
    }

    public void Dispose()
    {
        _transientExplosions?.RetirePresentation();
        UnloadedPackages = null;
        _beforeActorHit = null;
        _sounds?.Clear();
        _pipBoyRadio?.Off();
        _screenBlood?.Clear();
        PlayerMoves.Clear();
        _packageEvents?.Clear();
        _hitEvents?.Clear();
        Menus.Publish(true);
        _residentCells.Clear();
        _residentReferences.Clear();
        _residentSnapshot = null;
        _furnitureSeats.Clear();
        _idleMarkerReservations.Clear();
        _droppedCaptures.Clear();
        _droppedItems.Clear();
        _instances.Clear();
        _enableWork.Clear();
        _activationWork.Clear();
        _definitions.Clear();
        _baseScriptDefinitions.Clear();
        _healthSources.Clear();
        _bodyParts.Clear();
        _actorFoodSources.Clear();
        _sandboxPrimitiveReferences.Clear();
        _baseSignatures.Clear();
        _furnitureMarkerFlags.Clear();
        _encounterZones.Clear();
        _cellEncounterZones.Clear();
        _disposed = true;
    }
}
