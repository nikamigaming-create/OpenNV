using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessModelLoadScalarStore(FalloutReferenceModelDataInvocation Load,
    long Changed, string Field, uint Before, uint After);
internal sealed record FalloutProcessModelLoadFields(uint WeaponWord, uint WeaponScalar, uint Selection,
    IReadOnlyList<FalloutProcessModelLoadScalarStore> Stores)
{
    internal IReadOnlyList<FalloutProcessLoadedShaderStore> ShaderStores { get; init; } = [];
    internal string? Writer { get; init; }
    internal static FalloutProcessModelLoadFields Constructed => new(0, 0, uint.MaxValue, []);
}
internal sealed record FalloutPlayerLoadWeaponRead(FalloutReferenceModelDataInvocation Load,
    FalloutProcessAnimationLoadOwner Owner, FalloutProcessWeaponEntry? Entry);
internal sealed record FalloutPlayerLoadWeaponSetter(FalloutReferenceModelDataInvocation Load,
    FalloutPlayerLoadWeaponRead CallerRead, FalloutPlayerLoadWeaponRead? SetterRead,
    FalloutPlayerModelLoadKindScope? Scope, IReadOnlyList<FalloutProcessModelLoadScalarStore> Stores,
    FalloutPlayerModelGlobalStore? Global, bool Returned, string? Failure);
internal sealed record FalloutPlayerLoadMaskStore(FalloutReferenceModelDataInvocation Load,
    FalloutProcessAnimationLoadOwner Owner, long Changed, byte Before, byte After);
internal sealed partial class FalloutActorProcessCommonState
{
    private FalloutReferenceModelDataInvocation? _enteredPlayerModelStore;
    internal FalloutPlayerLoadWeaponSetter? LastPlayerLoadWeaponSetter { get; private set; }
    internal FalloutPlayerLoadWeaponRead ReadPlayerLoadWeapon(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireNotBusy(); requireActual(invocation); var owner = ReadAnimationLoadOwner(invocation);
        var fields = Require(invocation.Actor.Reference).SceneFields ?? throw new InvalidOperationException("Actual weapon field is unconstructed.");
        if (fields.ModelLoad?.Writer is { } writer) throw new NotSupportedException("source-Common-model-load-writer-unowned:" + writer);
        if (owner.Tier is not (0 or 1)) throw new NotSupportedException("source-Player-model-load-current-lower-weapon-setter-unowned");
        var result = new FalloutPlayerLoadWeaponRead(invocation, owner, fields.WeaponEntry); requireActual(invocation); return result;
    }
    internal FalloutPlayerLoadWeaponSetter SetPlayerLoadWeapon(FalloutPlayerModelLoadContinuationSource source,
        FalloutReferenceModelDataInvocation invocation, FalloutPlayerLoadWeaponRead caller,
        FalloutPlayerModelLoadGlobals globals, FalloutPlayerThreadContextState thread,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); RequireNotBusy(); requireActual(invocation);
        if (source.Animation.Tail.Scene.Main.EngineSha256 != _source.ExecutableSha256 || caller.Load != invocation ||
            caller.Owner.Process != _process || caller.Owner.Actor != invocation.Actor || caller.Owner.Epoch != invocation.Epoch)
            throw new InvalidDataException("Weapon setter changed its actual selected caller or source process.");
        var scope = thread.EnterPlayerLoadWeaponSetter(invocation, Guid.NewGuid(), caller.Owner.TierFactory, requireActual);
        var writes = new List<FalloutProcessModelLoadScalarStore>();
        var result = new FalloutPlayerLoadWeaponSetter(invocation, caller, null, scope, [], null, false, null);
        var faults = _callbackFault; _enteredPlayerModelStore = invocation; _busy = true;
        try
        {
            Check(); var current = Require(invocation.Actor.Reference);
            var fields = current.SceneFields ?? throw new NotSupportedException("source-Common-weapon-field-constructor-unqualified");
            var model = fields.ModelLoad ?? throw new NotSupportedException("source-Common-model-load-fields-constructor-unqualified");
            if (model.Writer is { } writer) throw new NotSupportedException("source-Common-model-load-writer-unowned:" + writer);
            var actual = new FalloutPlayerLoadWeaponRead(invocation, ReadAnimationLoadOwner(invocation), fields.WeaponEntry);
            result = result with { SetterRead = actual };
            if (!ReferenceEquals(caller.Entry, actual.Entry))
                throw new NotSupportedException("source-Common-weapon-replacement-release-and-inventory-factory-unowned");
            // Equality skips replacement only. The original zero second
            // argument still executes both scalar stores and Player global.
            Store("weapon-word", 0); Store("weapon-scalar", 0);
            result = result with { Global = globals.StoreCurrentPlayerWeaponNull(invocation, actual.Owner) };
            Store("weapon-selection", uint.MaxValue);
            scope = thread.ReturnPlayerModelLoadKind(scope, requireActual);
            Check(); return LastPlayerLoadWeaponSetter = result with { Scope = scope, Stores = writes.ToArray(), Returned = true };
        }
        catch (Exception error)
        {
            if (!scope.Scope.Returned) scope = thread.RetainPlayerModelLoadKindFailure(scope, error);
            LastPlayerLoadWeaponSetter = result with { Scope = scope, Stores = writes.ToArray(), Failure = error.Message }; throw;
        }
        finally { _busy = false; _enteredPlayerModelStore = null; }
        void Check()
        {
            requireActual(invocation);
            if (_callbackFault != faults) throw new InvalidOperationException("Player model-load setter swallowed actual Common reentry.");
        }
        void Store(string field, uint value)
        {
            Check(); var current = Require(invocation.Actor.Reference); var scene = current.SceneFields!;
            var old = scene.ModelLoad ?? throw new NotSupportedException("source-Common-model-load-fields-constructor-unqualified");
            if (old.Writer is { } writer) throw new NotSupportedException("source-Common-model-load-writer-unowned:" + writer);
            var before = field switch { "weapon-word" => old.WeaponWord, "weapon-scalar" => old.WeaponScalar, _ => old.Selection };
            var row = new FalloutProcessModelLoadScalarStore(invocation, Next(), field, before, value); writes.Add(row);
            var next = field switch { "weapon-word" => old with { WeaponWord = value }, "weapon-scalar" => old with { WeaponScalar = value }, _ => old with { Selection = value } };
            _current[invocation.Actor.Reference] = current with { SceneFields = scene with { ModelLoad = next with { Stores = [.. old.Stores, row] } }, Changed = row.Changed };
            Check();
        }
    }
    internal FalloutPlayerLoadMaskStore ClearPlayerLoadedSceneMask(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireNotBusy(); requireActual(invocation); var owner = ReadAnimationLoadOwner(invocation); var current = Require(invocation.Actor.Reference);
        if (owner.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            throw new NotSupportedException("source-model-loader-selected-mask-setter-class-unowned");
        var fields = current.SceneFields!; var faults = _callbackFault; _enteredPlayerModelStore = invocation; _busy = true;
        try
        {
            requireActual(invocation);
            if (_callbackFault != faults) throw new InvalidOperationException("Player model-load mask callback swallowed actual Common reentry.");
            var result = new FalloutPlayerLoadMaskStore(invocation, owner, Next(), fields.SceneUpdateMask, 0);
            _current[invocation.Actor.Reference] = current with { SceneFields = fields with { SceneUpdateMask = 0 }, Changed = result.Changed };
            requireActual(invocation);
            if (_callbackFault != faults) throw new InvalidOperationException("Player model-load mask callback swallowed actual Common reentry.");
            return result;
        }
        finally { _busy = false; _enteredPlayerModelStore = null; }
    }
    internal void ObservePlayerModelLoadFieldWriter(FalloutFormKey actor, string writer, Action requireActual)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(writer); Callback(() => { requireActual(); return true; });
        var current = Require(actor); var scene = current.SceneFields ?? throw new InvalidOperationException("Common source fields are absent.");
        var fields = scene.ModelLoad ?? throw new InvalidOperationException("Common loader fields are absent.");
        _current[actor] = current with { SceneFields = scene with { ModelLoad = fields with { Writer = fields.Writer ?? writer } }, Changed = Next() };
    }
    private static void ValidatePlayerModelLoadFields(FalloutProcessCommonEntry entry)
    {
        if (entry.SceneFields is not { } scene) return;
        ValidatePlayerLoadedShaderFields(entry);
        var fields = scene.ModelLoad ?? throw new InvalidDataException("Common model-load fields omitted their actual constructor.");
        var words = new Dictionary<string, uint> { ["weapon-word"] = 0, ["weapon-scalar"] = 0, ["weapon-selection"] = uint.MaxValue };
        long changed = 0;
        foreach (var store in fields.Stores)
        {
            if (store.Load.Actor != entry.Source || store.Load.Epoch != entry.Epoch || store.Load.Call == Guid.Empty ||
                store.Load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || store.Changed <= changed || store.Changed > entry.Changed ||
                !words.TryGetValue(store.Field, out var before) || store.Before != before ||
                store.After != (store.Field == "weapon-selection" ? uint.MaxValue : 0))
                throw new InvalidDataException("Common loader fields changed constructor/source/caller/store order.");
            words[store.Field] = store.After; changed = store.Changed;
        }
        if (words["weapon-word"] != fields.WeaponWord || words["weapon-scalar"] != fields.WeaponScalar || words["weapon-selection"] != fields.Selection)
            throw new InvalidDataException("Common loader scalar has no actual current store owner.");
    }
}
