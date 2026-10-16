using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerModelLoadContinuationStep
{ WeaponRead, WeaponSetter, ThirdRootFlag, CameraLookup, CameraGlobal, CameraDiagnostic, MagicReset, SceneMask, FirstShader, ShaderStore, Returned, Failed }
internal sealed record FalloutPlayerModelLoadDiagnosticRead(FalloutReferenceModelDataInvocation Load,
    string? CurrentModelName, bool GetterReturned, bool DiagnosticReturned);
internal sealed record FalloutPlayerModelLoadContinuationCall(FalloutReferenceModelDataInvocation Load,
    Guid Factory, long Entered, long Changed, FalloutPlayerModelLoadContinuationStep Step,
    FalloutPlayerModelLoadContinuationStep? FailedAt, Guid FirstFactory, Guid ThirdFactory,
    FalloutPlayerLoadWeaponRead? WeaponRead, FalloutPlayerLoadWeaponSetter? WeaponSetter,
    FalloutPlayerLoadedRootFlag? ThirdFlag, FalloutNifSceneKey? Camera,
    FalloutPlayerModelGlobalStore? CameraGlobal, FalloutPlayerModelLoadDiagnosticRead? Diagnostic,
    FalloutPlayerModelMagicReset? Magic, FalloutPlayerLoadMaskStore? Mask,
    FalloutPlayerLoadedShaderSelection? Shader, FalloutProcessLoadedShaderStore? ShaderStore,
    bool Returned, string? Failure)
{
    internal bool WholePlayerLoaderReturned => false;
}

internal sealed class FalloutPlayerModelLoadContinuationState
{
    private readonly FalloutPlayerModelLoadContinuationSource _source;
    private readonly FalloutActorProcessCommonState _common;
    private readonly FalloutPlayerThreadContextState _thread;
    private readonly FalloutPlayerModelLoadGlobals _globals;
    private readonly FalloutActorModelMagicResetState _magic;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _busy;
    private long _sequence, _reentry;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutPlayerModelLoadContinuationCall? LastCall { get; private set; }
    internal FalloutPlayerModelLoadContinuationState(FalloutPlayerModelLoadContinuationSource source,
        FalloutActorProcessCommonState common, FalloutPlayerThreadContextState thread,
        FalloutPlayerModelLoadGlobals globals, FalloutActorModelMagicResetState magic,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(common); ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(globals); ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(requireActual);
        if (globals.SourceIdentity != source.Identity || magic.SourceIdentity != source.Identity || thread.Source.Main != source.Animation.Tail.Scene.Main)
            throw new InvalidDataException("Model continuation changed its actual source children.");
        _source = source; _common = common; _thread = thread; _globals = globals; _magic = magic; _requireActual = requireActual;
    }
    // This callable body returns the completed weapon/magic/shared-property
    // prefix. Source controller/model attachment follows it; it cannot return
    // the enclosing Player loader or restore that loader's kind52 scope.
    internal FalloutPlayerModelLoadContinuationCall ContinueThroughSharedShader(
        FalloutReferenceModelDataInvocation load, FalloutReferenceModelDataNode first,
        FalloutReferenceModelDataNode third, FalloutSourceAnimationContextState firstContext,
        Func<FalloutReferenceModelDataInvocation, FalloutLoadedAnimationBaseRead> readBase,
        Func<FalloutReferenceModelDataInvocation, FalloutPluginRecord?> readRace,
        Func<FalloutReferenceModelDataInvocation, FalloutPlayerModelLoadDiagnosticRead> missingCameraDiagnostic)
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("Player model continuation reentered its actual call."); }
        Check();
        if (LastCall is not null || firstContext.Construction.Invocation != load ||
            firstContext.Construction.Role != FalloutLoadedAnimationRole.FirstPerson ||
            firstContext.RetainedModelFactory != first.Node.Factory || first.Node.Process != load.ReferenceProcess ||
            third.Node.Process != load.ReferenceProcess || ReferenceEquals(first, third) || first.Node.Factory == third.Node.Factory)
            throw new InvalidDataException("Player model continuation changed its actual independent returned model/context owners.");
        var fault = _reentry; _busy = true;
        LastCall = new(load, Factory, Next(), _sequence, FalloutPlayerModelLoadContinuationStep.WeaponRead,
            null, first.Node.Factory, third.Node.Factory, null, null, null, null, null, null, null, null, null, null, false, null);
        try
        {
            var weapon = _common.ReadPlayerLoadWeapon(load, _requireActual);
            LastCall = LastCall with { WeaponRead = weapon, Changed = Next() };
            Step(FalloutPlayerModelLoadContinuationStep.WeaponSetter);
            try { _ = _common.SetPlayerLoadWeapon(_source, load, weapon, _globals, _thread, _requireActual); }
            finally { LastCall = LastCall with { WeaponSetter = _common.LastPlayerLoadWeaponSetter?.Load == load ? _common.LastPlayerLoadWeaponSetter : null, Changed = Next() }; }
            Step(FalloutPlayerModelLoadContinuationStep.ThirdRootFlag);
            var graph = third.LogicalScene;
            LastCall = LastCall with { ThirdFlag = graph.StorePlayerModelActiveFlag(load, _requireActual), Changed = Next() };
            Step(FalloutPlayerModelLoadContinuationStep.CameraLookup);
            var camera = graph.ReadPlayerModelNamedObject(_source.CameraNode, load, _requireActual);
            LastCall = LastCall with { Camera = camera?.Key, Changed = Next() };
            Step(FalloutPlayerModelLoadContinuationStep.CameraGlobal);
            LastCall = LastCall with { CameraGlobal = _globals.StoreThirdCamera(load, graph, camera), Changed = Next() };
            if (camera is null)
            {
                Step(FalloutPlayerModelLoadContinuationStep.CameraDiagnostic);
                var diagnostic = missingCameraDiagnostic(load);
                LastCall = LastCall with { Diagnostic = diagnostic, Changed = Next() };
                if (diagnostic.Load != load || !diagnostic.GetterReturned || !diagnostic.DiagnosticReturned)
                    throw new InvalidOperationException("Missing Camera3rd did not return its actual name getter and diagnostic child.");
            }
            Step(FalloutPlayerModelLoadContinuationStep.MagicReset);
            try { _ = _magic.Reset(load, readBase, readRace); }
            finally { LastCall = LastCall with { Magic = _magic.LastCall?.Load == load ? _magic.LastCall : null, Changed = Next() }; }
            Step(FalloutPlayerModelLoadContinuationStep.SceneMask);
            LastCall = LastCall with { Mask = _common.ClearPlayerLoadedSceneMask(load, _requireActual), Changed = Next() };
            Step(FalloutPlayerModelLoadContinuationStep.FirstShader);
            var selected = first.LogicalScene.SelectPlayerFirstShader(_source, firstContext, load, _requireActual);
            LastCall = LastCall with { Shader = selected.Read, Changed = Next() };
            Step(FalloutPlayerModelLoadContinuationStep.ShaderStore);
            try { _ = _common.StorePlayerLoadedShader(load, selected.Property, _requireActual); }
            finally { LastCall = LastCall with { ShaderStore = _common.LastLoadedShaderStore?.Load == load ? _common.LastLoadedShaderStore : null, Changed = Next() }; }
            Step(FalloutPlayerModelLoadContinuationStep.Returned);
            return LastCall = LastCall with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutPlayerModelLoadContinuationStep.Failed,
                Failure = error.Message,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }
        void Check()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread || load.ThreadId != _ownerThread)
                throw new InvalidOperationException("Model continuation changed its actual source thread.");
            _requireActual(load); first.RequireCurrent(); third.RequireCurrent();
        }
        void Step(FalloutPlayerModelLoadContinuationStep step)
        {
            Check(); if (fault != _reentry) throw new InvalidOperationException("Model continuation callback swallowed real reentry.");
            LastCall = LastCall! with { Step = step, Changed = Next() };
        }
    }
    private long Next() => _sequence = checked(_sequence + 1);
}
