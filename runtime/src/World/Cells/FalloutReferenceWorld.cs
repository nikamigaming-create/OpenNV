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
    FalloutActorHeadTrackingSnapshot? HeadTracking = null)
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
                !float.IsFinite(snapshot.Opacity) || snapshot.Opacity is < 0 or > 1 ||
                (snapshot.Script is null ? snapshot.ScriptSha256 is not null || snapshot.Variables.Count != 0 :
                    !ValidKey(snapshot.Script.Value) || snapshot.ScriptSha256 is not { Length: 64 } || !snapshot.ScriptSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Saved reference state is invalid or duplicated.");
            foreach (var (name, value) in snapshot.ActorValues ?? new Dictionary<string, FalloutActorValue>())
                if ((name is not ("health" or "aggression") && FalloutActorValue.UserSlot(name) != name) || value is null || !value.IsFinite)
                    throw new InvalidDataException("Saved actor value is invalid.");
            if (snapshot.Animation is { } animation) FalloutActorAnimationState.Validate(animation);
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
            snapshot.DoorMotion?.Validate(snapshot.DoorOpen, snapshot.ObjectAnimations);
            snapshot.LockState?.Validate();
            snapshot.OwnershipOverride?.Validate();
            if (snapshot.TalkingActivatorActor is { } dialogueActor && !ValidKey(dialogueActor))
                throw new InvalidDataException("Saved talking activator actor identity is invalid.");
            snapshot.Placement?.Validate();
            snapshot.Engagement?.Validate();
            snapshot.HeadTracking?.Validate();
            if (snapshot.HeadTracking is { } head && head.Binding.Actor != snapshot.Reference)
                throw new InvalidDataException("Saved head tracking belongs to a different reference.");
            if (snapshot.Engagement?.AttackRandomState is { } attackRandom && snapshot.AttackRandomState != attackRandom)
                throw new InvalidDataException("Saved attack randomness differs between reference and active engagement.");
            snapshot.PackageMotion?.Validate();
            snapshot.PackageAssignment?.Validate();
            snapshot.PackageBindingFailure?.Validate();
            snapshot.SelectionFailure?.Validate();
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

internal sealed class FalloutReferenceInstance
{
    internal FalloutFormKey Reference { get; }
    internal FalloutFormKey Cell { get; }
    internal FalloutFormKey Base { get; }
    internal FalloutReferenceScriptDefinition? Script { get; private set; }
    internal FalloutActorTemplateSelection? Templates { get; set; }
    internal Dictionary<uint, double> Variables { get; }
    internal string? ScriptError { get; set; }
    internal FalloutReferenceScriptStoppedFrame? ScriptStoppedFrame { get; set; }
    internal FalloutReferenceScriptStoppedFrame? CompletedScriptContinuation { get; set; }
    internal bool Enabled { get; set; }
    internal FalloutReferenceEnableRequest? EnableRequest { get; set; }
    internal float Opacity { get; set; } = 1;
    internal bool NoFade { get; set; }
    internal bool Destroyed { get; set; }
    internal FalloutDestructionState? Destruction { get; set; }
    internal bool DeletePending { get; set; }
    internal bool Deleted { get; set; }
    internal bool Taken { get; set; }
    internal bool DoorOpen { get; set; }
    internal FalloutDoorMotionState? DoorMotion { get; set; }
    internal bool Unlocked { get; set; }
    internal FalloutReferenceLockState? LockState { get; set; }
    internal FalloutReferenceOwnershipOverride? OwnershipOverride { get; set; }
    internal bool Unconscious { get; set; }
    internal bool KnockedDown { get; set; }
    internal bool Restrained { get; set; }
    internal bool PlayerTeammate { get; set; }
    internal bool TalkedToPlayer { get; set; }
    internal bool? BroadcastState { get; set; }
    internal FalloutFormKey? TalkingActivatorActor { get; set; }
    internal FalloutActorPackageMotion? PackageMotion { get; set; }
    internal FalloutActorPackageAssignment? PackageAssignment { get; set; }
    internal Func<FalloutActorPackageAssignment?>? CapturePackageAssignment { get; set; }
    internal FalloutActorPackageBindingFailure? PackageBindingFailure { get; set; }
    internal Func<bool>? CanCapturePackageBindingFailure { get; set; }
    internal Func<FalloutActorPackageBindingFailure>? CapturePackageBindingFailure { get; set; }
    internal FalloutActorFurnitureContinuation? FurnitureContinuation { get; set; }
    internal FalloutActorSelectionFailure? SelectionFailure { get; set; }
    internal FalloutActorDialogueContinuation? DialogueContinuation { get; set; }
    internal Func<bool>? CanCaptureDialogue { get; set; }
    internal Func<FalloutActorDialogueContinuation?>? CaptureDialogue { get; set; }
    internal bool DialogueCaptureReady => CanCaptureDialogue?.Invoke() ??
        DialogueContinuation is not null && ProcedureCaptureBlocker == FalloutActorDialogueContinuation.CaptureBlocker;
    internal Func<bool>? CanCaptureSelectionFailure { get; set; }
    internal Func<FalloutActorSelectionFailure>? CaptureSelectionFailure { get; set; }
    internal bool SelectionFailureCaptureReady => CanCaptureSelectionFailure?.Invoke() ??
        SelectionFailure is { } failure && ProcedureCaptureBlocker == failure.Error;
    internal Func<bool>? CanCaptureFurniture { get; set; }
    internal Func<FalloutActorFurnitureContinuation?>? CaptureFurniture { get; set; }
    internal bool FurnitureCaptureReady => CanCaptureFurniture?.Invoke() ??
        FurnitureContinuation is not null && ProcedureCaptureBlocker == FalloutActorFurnitureContinuation.CaptureBlocker;
    internal bool PackageBindingFailureCaptureReady => CanCapturePackageBindingFailure?.Invoke() ??
        PackageBindingFailure is { } failure && ProcedureCaptureBlocker == failure.Error;
    internal List<FalloutPackageStart> PackageStarts { get; } = [];
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
    internal Action? StopCombat { get; set; }
    internal Func<FalloutActorEngagement?>? CaptureEngagement { get; set; }
    internal IReadOnlyList<FalloutObjectAnimationSnapshot>? ObjectAnimations { get; set; }
    private readonly List<Func<IReadOnlyList<FalloutObjectAnimationSnapshot>>> _objectAnimationCaptures = [];
    internal Func<IReadOnlyList<FalloutObjectAnimationSnapshot>>? CaptureObjectAnimations => _objectAnimationCaptures.LastOrDefault();

    internal void BindObjectAnimationCapture(Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> capture) =>
        _objectAnimationCaptures.Add(capture);

    internal void UnbindObjectAnimationCapture(Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> capture)
    {
        if (CaptureObjectAnimations == capture) ObjectAnimations = capture();
        if (!_objectAnimationCaptures.Remove(capture)) throw new InvalidOperationException("Object animation capture was not bound.");
    }

    internal FalloutReferenceInstance(FalloutPluginRecord reference, FalloutReferenceScriptDefinition? script)
    {
        if (reference.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new InvalidDataException($"{reference.FormKey} is not a placed reference.");
        Reference = reference.FormKey;
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

    internal FalloutReferenceSnapshot Capture()
    {
        if (HeadTrackingCaptureBlocker is not null || HeadTrackingRequired && CaptureHeadTracking is null && HeadTracking is null)
            throw new NotSupportedException($"Reference {Reference} cannot save head tracking: {HeadTrackingCaptureBlocker ?? "required owner is missing"}");
        var failureReady = PackageBindingFailureCaptureReady;
        if (ProcedureCaptureBlocker is { } blocker && !failureReady && !FurnitureCaptureReady && !SelectionFailureCaptureReady && !DialogueCaptureReady)
            throw new NotSupportedException($"Reference {Reference} cannot save: {blocker}");
        var bindingFailure = failureReady ? CapturePackageBindingFailure is { } captureFailure
            ? captureFailure() : PackageBindingFailure?.Copy() : null;
        return new(Reference, Cell, Base, Script?.Record.FormKey,
            Script?.Sha256, new Dictionary<uint, double>(Variables), ScriptError, Enabled, EnableRequest, Opacity, NoFade,
            new Dictionary<string, FalloutActorValue>(ActorValues), Destroyed, DeletePending, Deleted, Inventory?.Capture(), Taken, DoorOpen, Unlocked,
            _soundRandom?.State, Animation.Capture(), Unconscious, MapMarker,
            Injury is null ? null : Injury with { LimbDamage = new Dictionary<byte, float>(Injury.LimbDamage) }, CaptureRagdoll?.Invoke() ?? Ragdoll,
            CaptureEngagement?.Invoke() ?? Engagement, Templates?.Capture(), Placement?.Copy(), Restrained, PlayerTeammate,
            TalkedToPlayer, PackageMotion, HitReaction?.Copy(), _hitReactionRandom?.State, KnockedDown, Destruction,
            CaptureObjectAnimations?.Invoke() ?? ObjectAnimations, DoorMotion, LockState, OwnershipOverride,
            PackageStarts.Count == 0 ? null : PackageStarts.ToArray(), PackageIdle, TalkingActivatorActor,
            CapturePackageAssignment is { } captureAssignment ? captureAssignment() : PackageAssignment, bindingFailure, BroadcastState,
            FurnitureContinuation: CaptureFurniture is { } captureFurniture ? captureFurniture() : FurnitureContinuation?.Copy(),
            SelectionFailure: SelectionFailureCaptureReady ? CaptureSelectionFailure is { } captureSelection
                ? captureSelection() : SelectionFailure?.Copy() : null,
            DialogueContinuation: CaptureDialogue is { } captureDialogue ? captureDialogue() : DialogueContinuation?.Copy(),
            DeathCount: DeathCount == 0 ? null : DeathCount, AttackRandomState: _attackRandom?.State,
            ScriptStoppedFrame: ScriptStoppedFrame?.Copy(), CompletedScriptContinuation: CompletedScriptContinuation?.Copy(),
            HeadTracking: CaptureHeadTracking?.Invoke() ?? HeadTracking?.Copy());
    }
}

internal sealed class FalloutReferenceScriptDefinition(FalloutPluginRecord record)
{
    internal FalloutPluginRecord Record { get; } = record;
    internal string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    internal IReadOnlyDictionary<string, uint> Locals { get; } = FalloutScriptLocals.Read(record);
    internal IReadOnlyDictionary<string, FalloutScriptLocalDeclaration> Declarations { get; } =
        FalloutScriptLocals.ReadDeclarations(record);
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
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutReferenceInstance>> _residentCells = [];
    private readonly Dictionary<FalloutFormKey, int> _residentReferences = [];
    private bool _disposed;

    internal int InstanceCount => _instances.Count;
    internal int ResidentCellCount => _residentCells.Count;
    internal bool IsCellResident(FalloutFormKey cell) => _residentCells.ContainsKey(cell);
    internal int ScriptDefinitionCount => _definitions.Count;
    internal IEnumerable<FalloutReferenceInstance> ResidentInstances => _residentReferences.Keys.Select(key => _instances[key]);
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

    internal void ValidateValueHandles()
    {
        foreach (var instance in _instances.Values)
        {
            if (instance.Script is null) continue;
            foreach (var declaration in instance.Script.Declarations.Values)
            {
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
        _residentCells.Add(scene.Cell.FormKey, instances);
        foreach (var instance in instances)
            _residentReferences[instance.Reference] = _residentReferences.GetValueOrDefault(instance.Reference) + 1;
        return instances;
    }

    internal void UnloadCell(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_residentCells.Remove(cell, out var instances)) throw new InvalidOperationException($"Cell {cell} is not resident.");
        foreach (var instance in instances)
        {
            var remaining = _residentReferences[instance.Reference] - 1;
            if (remaining > 0) { _residentReferences[instance.Reference] = remaining; continue; }
            _residentReferences.Remove(instance.Reference);
            if (instance.DeletePending) { instance.Deleted = true; instance.DeletePending = false; }
        }
    }

    internal FalloutReferenceInstance Get(FalloutFormKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instances.TryGetValue(key, out var instance)) return instance;
        var record = records.GetEffective(key);
        var baseKey = FalloutDialogueTopic.RequiredForm(record, "NAME");
        // Engine primitive bases need no ESM base record; their XPRM reference
        // still has a real identity and lifetime. Other missing bases fail closed.
        var script = records.TryGetEffective(baseKey, out _) ? FalloutScriptLocals.AttachedScript(records, record) :
            record.ReadSubrecords().Any(field => field.Signature == "XPRM") ? null :
            throw new InvalidDataException($"Reference {key} has no winning base {baseKey}.");
        FalloutReferenceScriptDefinition? definition = null;
        if (script is not null && !_definitions.TryGetValue(script.FormKey, out definition))
            _definitions.Add(script.FormKey, definition = new(script));
        instance = new(record, definition);
        _instances.Add(key, instance);
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

    internal int PendingProcedureCaptureCount => _instances.Values.Count(instance =>
        instance.ProcedureCaptureBlocker is not null && !instance.PackageBindingFailureCaptureReady && !instance.FurnitureCaptureReady && !instance.SelectionFailureCaptureReady && !instance.DialogueCaptureReady);
    internal int StoppedPackageBindingCount => _instances.Values.Count(instance => instance.PackageBindingFailureCaptureReady);
    internal object PendingProcedureCaptures => _instances.Values.Where(instance =>
        instance.ProcedureCaptureBlocker is not null && !instance.PackageBindingFailureCaptureReady && !instance.FurnitureCaptureReady && !instance.SelectionFailureCaptureReady && !instance.DialogueCaptureReady)
        .Select(instance => new
        {
            reference = instance.Reference.ToString(),
            assignment = instance.PackageAssignment?.Package.ToString(),
            motion = instance.PackageMotion?.Package.ToString(),
            blocker = instance.ProcedureCaptureBlocker,
        }).ToArray();

    private FalloutScriptManualSaveRequests? _scriptManualSaves;
    internal FalloutScriptManualSaveRequests ScriptManualSaves => _scriptManualSaves ??= new(records);

    internal IReadOnlyList<FalloutReferenceSnapshot> Capture()
    {
        _scriptManualSaves?.RequireCapture();
        ObjectDisposedException.ThrowIf(_disposed, this);
        PlayerMoves.RequireSettled();
        if (PendingHitEventCount != 0)
            throw new NotSupportedException("Saving pending reference hit events requires their continuation state.");
        foreach (var actor in _packageEvents?.PendingActors ?? []) _ = Get(actor);
        return _instances.Values.OrderBy(instance => records.RuntimeFormId(instance.Reference))
            .Select(instance => instance.Capture() with
            {
                PackageEvents = _packageEvents?.Capture(instance.Reference) is { Count: > 0 } events ? events : null
            }).ToArray();
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
            if (snapshot.Destruction is { } destruction)
            {
                destruction.Validate(FalloutDestructible.Read(records, instance.Base) ??
                    throw new InvalidDataException("Saved destruction has no source DEST."));
                instance.Destruction = destruction;
            }
            instance.DeletePending = snapshot.DeletePending;
            instance.Deleted = snapshot.Deleted;
            instance.Taken = snapshot.Taken;
            instance.DoorOpen = snapshot.DoorOpen;
            instance.DoorMotion = snapshot.DoorMotion;
            validated.RestoreAccess(instance, snapshot);
            if (snapshot.BroadcastState is { } broadcast)
                validated.SetBroadcastState(snapshot.Reference, broadcast ? 1 : 0);
            if (snapshot.Placement is { } placement)
            {
                if (records.GetEffective(placement.Cell).Signature != "CELL") throw new InvalidDataException("Saved placement has no winning CELL.");
                instance.Placement = placement.Copy();
            }
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
                    if (motion.EditorTravel is { } editorTravel)
                        FalloutEditorTravelPackage.Read(package).Validate(validated, snapshot.Reference, editorTravel);
                    if (motion.DialogueCompleted)
                    {
                        var dialogue = FalloutDialoguePackage.Read(package);
                        var declaration = FalloutScriptPackage.Read(package);
                        if (dialogue.Type != 1 || declaration.LocationType is not (null or 2) || declaration.LocationRadius != 0)
                            throw new InvalidDataException("Saved dialogue completion has no supported source procedure.");
                    }
                }
                instance.PackageMotion = motion with
                {
                    Position = (float[])motion.Position.Clone(),
                    Rotation = (float[])motion.Rotation.Clone(),
                    Travel = motion.Travel is { } travel ? travel with
                    {
                        Location = (float[])travel.Location.Clone(),
                        RouteTarget = travel.RouteTarget is null ? null : (float[])travel.RouteTarget.Clone(),
                        RouteWaypoints = travel.RouteWaypoints?.Select(point => (float[])point.Clone()).ToArray()
                    } : null,
                    Guard = motion.Guard is { } guard ? guard with { Location = (float[])guard.Location.Clone() } : null
                };
            }
            validated.RestorePackageTiming(instance, snapshot);
            if (snapshot.SoundRandomState is { } soundRandom) instance.SoundRandom.Restore(soundRandom);
            if (snapshot.Animation is { } animation) instance.Animation.Restore(animation);
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
            FalloutActorStoppedPose.ValidateTarget(records, snapshot);
            instance.ObjectAnimations = snapshot.ObjectAnimations?.ToArray();
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
            if (snapshot.PackageMotion is { } staleMotion && snapshot.PackageAssignment is { } assignment &&
                assignment.Package != staleMotion.Package) continue;
            if (snapshot.PackageMotion?.Travel is { } travel)
                FalloutTravelPackage.Read(records.GetEffective(snapshot.PackageMotion.Package),
                    ownsIdleCollection: records.GetEffective(validated.Get(snapshot.Reference).Base).Signature == "NPC_")
                    .Validate(records, validated, snapshot.Reference, travel);
            if (snapshot.PackageMotion?.Guard is { } guard)
                FalloutGuardPackage.Read(records.GetEffective(snapshot.PackageMotion.Package))
                    .Validate(records, validated, snapshot.Reference, guard);
        }
        validated.PackageEvents.Restore(snapshots);
        _packageEvents = validated._packageEvents;
        validated._packageEvents = null;
        foreach (var (key, instance) in validated._instances) _instances.Add(key, instance);
        foreach (var (key, definition) in validated._definitions) _definitions.Add(key, definition);
        foreach (var (seat, actor) in validated._furnitureSeats) _furnitureSeats.Add(seat, actor);
    }

    public void Dispose()
    {
        UnloadedPackages = null;
        _sounds?.Clear();
        _pipBoyRadio?.Off();
        _screenBlood?.Clear();
        PlayerMoves.Clear();
        _packageEvents?.Clear();
        _hitEvents?.Clear();
        Menus.Publish(true);
        _residentCells.Clear();
        _residentReferences.Clear();
        _furnitureSeats.Clear();
        _instances.Clear();
        _definitions.Clear();
        _healthSources.Clear();
        _bodyParts.Clear();
        _encounterZones.Clear();
        _cellEncounterZones.Clear();
        _disposed = true;
    }
}
