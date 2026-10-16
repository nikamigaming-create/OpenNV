using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLoadedAnimationFirstSkeletonRead(string Name, string Value, Guid RecordsFactory, string RecordsSource);
internal sealed record FalloutLoadedAnimationBaseRead(FalloutPluginRecord Record, FalloutReferenceBaseAssignmentCall Assignment);
internal sealed record FalloutLoadedPlayerAnimationSnapshot(string Source, Guid Factory,
    FalloutReferenceModelDataInvocation? EnteredLoad, FalloutLoadedAnimationFirstSkeletonRead? FirstSkeleton,
    FalloutSourceAnimationKindScope? Scope, IReadOnlyList<FalloutSourceAnimationContextSnapshot> Contexts,
    FalloutActorAnimationResourceSnapshot? FirstResources, FalloutActorAnimationResourceSnapshot? CommonResources,
    FalloutReferenceAnimationLoadWriter? FirstPointer, IReadOnlyList<FalloutProcessAnimationContextStore> CommonPointers,
    IReadOnlyList<FalloutReferenceAnimationFlagWrite> Flags, string? Failure, bool Retired,
    int RetainedModels, int RetiredContexts, string? RetirementFailure = null, bool RetirementStarted = false);

// These are callable source children of Player 3D loading. No method returns
// the enclosing Player loader: its following camera/weapon children remain
// independent. Root composes these calls at their actual entered positions.
internal sealed class FalloutPlayerLoadedAnimationState
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutActorPackageCommandFields _fields;
    private readonly FalloutActorProcessCommonState _common;
    private readonly FalloutReferenceScriptExtraInitializer _flags;
    private readonly FalloutPlayerThreadContextState _thread;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly Dictionary<Guid, FalloutSourceAnimationContextState> _contexts = [];
    private readonly List<FalloutProcessAnimationContextStore> _pointers = [];
    private readonly List<FalloutReferenceAnimationFlagWrite> _flagStores = [];
    private FalloutReferenceModelDataInvocation? _load;
    private FalloutSourceAnimationKindScope? _scope;
    private FalloutLoadedAnimationFirstSkeletonRead? _firstSkeleton;
    private FalloutSourceAnimationContextState? _first, _third;
    private FalloutActorAnimationResourceState? _firstResources, _commonResources;
    private FalloutReferenceAnimationLoadWriter? _firstPointer;
    private string? _failure, _retirementFailure;
    private bool _commonInitialized, _retired, _retirementStarted;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutLoadedAnimationSource Source { get; }
    internal string? SaveBlocker => _retired ? "source-loaded-Player-animation-retired" :
        _retirementStarted ? "source-loaded-Player-animation-retirement-remains-entered" : _failure ??
        (_scope is { Scope.Returned: false } ? "source-enclosing-Player-3D-loader-remains-entered" : null);
    internal bool RetainsModel(Guid modelFactory) => _contexts.Values.Any(value => value.RetainedModelFactory == modelFactory);
    internal FalloutPlayerLoadedAnimationState(FalloutLoadedAnimationSource source, FalloutPluginStack records,
        FalloutActorPackageCommandFields fields, FalloutActorProcessCommonState common,
        FalloutReferenceScriptExtraInitializer flags, FalloutPlayerThreadContextState thread,
        Action<FalloutReferenceModelDataInvocation> requireActual, Action requireLifetime)
    {
        source.Validate(); Source = source; _records = records; _fields = fields; _common = common;
        _flags = flags; _thread = thread; _requireActual = requireActual; _requireLifetime = requireLifetime;
        if (flags.FieldsFactory != fields.Factory || fields.Constructor != flags.Constructor ||
            source.Tail.Movement.Mover.Controller.Fields.Identity != fields.Source.Identity ||
            source.Tail.Scene.Main.Identity != thread.Source.Main.Identity)
            throw new InvalidDataException("Loaded animation composition changed the shared reference/fields/thread owners.");
        fields.BindAnimationLoadSource(source.Tail);
    }
    internal void EnterPlayerLoad(FalloutReferenceModelDataInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_retired || _retirementStarted, this);
        _requireLifetime(); _requireActual(invocation);
        if (_load is not null || _first is not null) throw new NotSupportedException("source-Player-live-animation-reload-retirement-unowned");
        _load = invocation;
        try { _scope = _thread.EnterAnimationInitialization(Guid.NewGuid(), Factory, FalloutLoadedAnimationScopeKind.PlayerLoad, Check); }
        catch (Exception error) { Fail(error); throw; }
    }
    internal FalloutLoadedAnimationFirstSkeletonRead ReadFirstSkeletonForPlayerLoad(FalloutReferenceModelDataInvocation invocation)
    {
        RequireLoad(invocation);
        if (_firstSkeleton is not null) throw new InvalidOperationException("First-skeleton input repeated its actual loader phase.");
        try
        {
            var returned = _records.LoadReceipt; _records.RequireLiveLoadReceipt(returned);
            var selected = FalloutGameSettingStrings.Read(_records, FalloutLoadedAnimationSource.FirstSkeletonSetting);
            _firstSkeleton = new(FalloutLoadedAnimationSource.FirstSkeletonSetting, selected, returned.Factory, returned.SourceHash);
            Check(); return _firstSkeleton;
        }
        catch (Exception error) { Fail(error); throw; }
    }
    internal FalloutLoadedAnimationFirstSkeletonRead BeginPlayerLoad(FalloutReferenceModelDataInvocation invocation)
    { EnterPlayerLoad(invocation); return ReadFirstSkeletonForPlayerLoad(invocation); }
    // Called after the actual source first root publication, before inherited
    // loading temporarily changes the source view byte to one.
    internal FalloutSourceAnimationContextState ConstructFirst(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceModelDataNode root)
    {
        RequireLoad(invocation); root.RequireCurrent();
        if (_first is not null || _firstSkeleton is null) throw new InvalidOperationException("First animation allocation repeated its source phase.");
        var selected = Canonical(_firstSkeleton.Value);
        if (!selected.StartsWith("meshes/", StringComparison.Ordinal)) selected = "meshes/" + selected;
        if (Canonical(root.LogicalScene.SkeletonPath) != selected)
            throw new InvalidDataException("Returned first model differs from the actual current first-skeleton setting.");
        try
        {
            _firstResources = new(Source, invocation, FalloutLoadedAnimationRole.FirstPerson, root.LogicalScene, RequireLoad, _requireLifetime);
            _fields.ObserveEnteredFirstPersonAnimationLoad(invocation, RequireLoad);
            _first = new(Source.Tail, invocation, RequireLoad, _requireLifetime, FalloutLoadedAnimationRole.FirstPerson);
            _contexts.Add(_first.Identity, _first);
            _firstPointer = _fields.StoreConstructedFirstPersonAnimation(invocation, _first, RequireLoad);
            Check(); return _first;
        }
        catch (Exception error) { Fail(error); throw; }
    }
    // The genuine inherited model factory constructs/publishes the independent
    // Actor resource object before Reference's animation initializer consumes it.
    internal FalloutActorAnimationResourceState ConstructCommonModelResources(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceModelDataNode root)
    {
        RequireLoad(invocation); root.RequireCurrent();
        if (_commonResources is not null) throw new InvalidOperationException("Actor model resources repeated their actual returned factory.");
        _commonResources = new(Source, invocation, FalloutLoadedAnimationRole.Common, root.LogicalScene, RequireLoad, _requireLifetime);
        return _commonResources;
    }
    internal FalloutSourceAnimationContextState? InitializeCommon(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceModelDataNode root, FalloutLoadedAnimationBaseRead currentBase,
        FalloutLoadedAnimationResources resources, FalloutLoadedAnimationUpdateOwners updateOwners,
        Action requireModelLifetime)
    {
        RequireLoad(invocation); root.RequireCurrent();
        if (_third is not null || _commonResources is null || _first is null)
            throw new InvalidOperationException("Inherited animation initialization changed its actual constructor/resource order.");
        var assignment = currentBase.Assignment;
        if (!ReferenceEquals(_records.GetEffective(currentBase.Record.FormKey), currentBase.Record) ||
            currentBase.Record.Signature != "NPC_" || !assignment.Returned || !assignment.PointerStored || assignment.Failure is not null ||
            assignment.After != currentBase.Record.FormKey || assignment.Invocation.Actor != invocation.Actor ||
            resources.Model.Appearance.Npc != currentBase.Record.FormKey)
            throw new InvalidDataException("Inherited NPC animation borrowed identity instead of the actual current Base assignment.");
        try
        {
            _common.ObserveEnteredPlayerAnimationLoad(invocation, RequireLoad);
            _flagStores.Add(_flags.SetLoadedAnimationFlag(Source, invocation, false, null, RequireLoad));
            _pointers.Add(_common.StoreLoadedAnimationContext(invocation, null, RequireLoad));
            var referenceFlags = _flags.ReadLoadedAnimationFlags(invocation, RequireLoad);
            if ((referenceFlags.Flags & FalloutLoadedAnimationSource.ReferenceAnimationInhibitedFlag) != 0) { _commonInitialized = true; return null; }
            _commonResources.ClearForAnimationLoad(invocation, RequireLoad);
            _third = new(Source.Tail, invocation, RequireLoad, _requireLifetime, FalloutLoadedAnimationRole.Common);
            _contexts.Add(_third.Identity, _third);
            _flagStores.Add(_flags.SetLoadedAnimationFlag(Source, invocation, true, _third, RequireLoad));
            _pointers.Add(_common.StoreLoadedAnimationContext(invocation, _third, RequireLoad));
            _third.BindLoadedModel(Source, invocation, root, resources, updateOwners, _thread, RequireLoad, requireModelLifetime);
            _third.UpdateAfterLoad(invocation, RequireLoad); _commonInitialized = true; Check(); return _third;
        }
        catch (Exception error) { Fail(error); throw; }
    }
    internal FalloutLoadedAnimationBindCall BindFirst(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceModelDataNode root, FalloutLoadedAnimationResources resources,
        FalloutLoadedAnimationUpdateOwners updateOwners, Action requireModelLifetime)
    {
        RequireLoad(invocation);
        if (_first is null || !_commonInitialized || _third is not null && _third.LastBind is not { Returned: true })
            throw new InvalidOperationException("First animation bind preceded returned inherited context initialization.");
        try
        {
            _third?.StopInitialGroupZero(invocation, RequireLoad);
            return _first.BindLoadedModel(Source, invocation, root, resources, updateOwners, _thread, RequireLoad, requireModelLifetime);
        }
        catch (Exception error) { Fail(error); throw; }
    }
    internal FalloutSourceAnimationContextState RequireContext(Guid identity)
    { _requireLifetime(); return _contexts.GetValueOrDefault(identity) ?? throw new InvalidOperationException("No actual loaded context owns this pointer."); }
    internal void ObserveEnteredLoadedAnimationWriter(string producer, Action requireActual)
    {
        requireActual(); _requireLifetime();
        foreach (var context in _contexts.Values) context.ObserveUnownedWriter(producer, requireActual);
    }
    internal FalloutLoadedPlayerAnimationSnapshot Capture() => new(Source.Identity, Factory, _load, _firstSkeleton,
        _scope, _contexts.Values.Select(value => value.Capture()).ToArray(), _firstResources?.Capture(),
        _commonResources?.Capture(), _firstPointer, _pointers.ToArray(), _flagStores.ToArray(), _failure, _retired,
        _contexts.Values.Count(value => value.RetainedModelFactory is not null), _contexts.Values.Count(value => value.Retired), _retirementFailure, _retirementStarted);
    internal void Retire()
    {
        if (_retired) return;
        if (_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player animation retirement left its actual source thread.");
        // This is resource-owner teardown, not an enclosing retail loader
        // return. Failed TLS scopes and source stores remain in the snapshot.
        _retirementStarted = true;
        try { RetireContexts(_contexts.Values); _retired = true; }
        catch (Exception error) { _retirementFailure ??= error.Message; throw; }
    }
    internal static void RetireContexts(IEnumerable<FalloutSourceAnimationContextState> contexts) =>
        NativeNifSceneRetirement.Drain("Player loaded animation contexts",
            contexts.Where(context => !context.Retired).Select(context => (Action)context.Retire));
    private void RequireLoad(FalloutReferenceModelDataInvocation invocation)
    { if (_load != invocation) throw new InvalidOperationException("Animation child left its genuine entered source load."); Check(); }
    private void Check() { ObjectDisposedException.ThrowIf(_retired || _retirementStarted, this); if (_failure is { } failure) throw new NotSupportedException(failure); _requireLifetime(); _requireActual(_load ?? throw new InvalidOperationException("No current entered Player load.")); }
    private void Fail(Exception error)
    {
        _failure ??= error.Message;
        if (_scope is { Scope.Returned: false, Scope.Failure: null } scope) _scope = _thread.RetainAnimationInitializationFailure(scope, error);
    }
    private static string Canonical(string path) => FalloutBsaArchive.CanonicalPath(path).Replace('\\', '/').ToLowerInvariant();
}
