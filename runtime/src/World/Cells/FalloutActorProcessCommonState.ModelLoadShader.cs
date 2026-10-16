using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessLoadedShaderStore(FalloutReferenceModelDataInvocation Load,
    FalloutProcessAnimationLoadOwner Owner, long Entered, long Changed, Guid? Before,
    FalloutSourceSharedShaderIdentity? After, bool OldReleased, bool PointerStored, bool FieldRetained,
    bool Returned, string? Failure, Guid? PointerAfter);
internal sealed partial class FalloutActorProcessCommonState
{
    private readonly Dictionary<FalloutFormKey, FalloutSourceSharedShaderProperty.Lease> _loadedShaders = new(FalloutFormKeyComparer.Instance);
    internal FalloutProcessLoadedShaderStore? LastLoadedShaderStore { get; private set; }
    internal FalloutProcessLoadedShaderStore StorePlayerLoadedShader(FalloutReferenceModelDataInvocation load,
        FalloutSourceSharedShaderProperty? supplied, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireNotBusy(); requireActual(load); var owner = ReadAnimationLoadOwner(load); var current = Require(load.Actor.Reference);
        var fields = current.SceneFields ?? throw new NotSupportedException("source-Common-shader-field-constructor-unavailable");
        _loadedShaders.TryGetValue(load.Actor.Reference, out var old);
        if (fields.PostActionContext != old?.Resource.Source.Identity)
            throw new NotSupportedException("source-Common-current-shader-payload-owner-unbound");
        var model = fields.ModelLoad ?? throw new NotSupportedException("source-Common-model-load-fields-constructor-unbound");
        if (model.Writer is { } writer) throw new NotSupportedException("source-Common-model-load-writer-unowned:" + writer);
        var call = new FalloutProcessLoadedShaderStore(load, owner, Next(), _sequence, fields.PostActionContext,
            supplied?.Source, false, false, false, false, null, fields.PostActionContext);
        var faults = _callbackFault; _enteredPlayerModelStore = load; _busy = true;
        try
        {
            Check();
            if (!ReferenceEquals(old?.Resource, supplied))
            {
                old?.Dispose(); _loadedShaders.Remove(load.Actor.Reference); call = call with { OldReleased = old is not null, Changed = Next() };
                fields = fields with { PostActionContext = supplied?.Source.Identity };
                _current[load.Actor.Reference] = current = current with { SceneFields = fields, Changed = Next() };
                call = call with { PointerStored = true, PointerAfter = fields.PostActionContext, Changed = _sequence };
                if (supplied is not null) _loadedShaders.Add(load.Actor.Reference, supplied.Retain());
                call = call with { FieldRetained = supplied is not null, Changed = Next() };
            }
            Check(); call = call with { Returned = true, Changed = Next() }; return call;
        }
        catch (Exception error)
        { call = call with { Failure = error.Message, Changed = Next() }; throw; }
        finally
        {
            LastLoadedShaderStore = call;
            var live = Require(load.Actor.Reference); var state = live.SceneFields!.ModelLoad!;
            _current[load.Actor.Reference] = live with
            {
                SceneFields = live.SceneFields with
                { ModelLoad = state with { ShaderStores = [.. state.ShaderStores, call] } },
                Changed = _sequence
            };
            _busy = false; _enteredPlayerModelStore = null;
        }
        void Check()
        {
            requireActual(load);
            if (_callbackFault != faults) throw new InvalidOperationException("Loaded shader callback swallowed actual Common reentry.");
        }
    }
    internal void RetirePlayerLoadedShaderFields()
    {
        // Source owner teardown is independent from logical model/presentation
        // health. These are actual property leases, not controller contexts.
        foreach (var lease in _loadedShaders.Values) lease.Dispose(); _loadedShaders.Clear();
    }
    private static void ValidatePlayerLoadedShaderFields(FalloutProcessCommonEntry entry)
    {
        if (entry.SceneFields is not { ModelLoad: { } model } scene) return;
        Guid? pointer = null; long changed = 0;
        foreach (var row in model.ShaderStores)
        {
            if (row.Load.Actor != entry.Source || row.Load.Epoch != entry.Epoch || row.Owner.Actor != entry.Source ||
                row.Owner.Process != row.Load.CommonProcess || row.Owner.Epoch != entry.Epoch || row.Before != pointer ||
                row.Entered <= changed || row.Changed <= row.Entered || row.Changed > entry.Changed || row.Returned != (row.Failure is null) || row.PointerAfter == Guid.Empty ||
                row.After is { } property && (property.Identity == Guid.Empty || property.ModelFactory == Guid.Empty || property.LogicalLifetime == Guid.Empty ||
                    property.Kind is < 7 or > 11 || string.IsNullOrWhiteSpace(property.Type)))
                throw new InvalidDataException("Common post-action shader changed its actual property/class/store lifetime.");
            if (row.PointerStored)
            {
                if (row.PointerAfter != row.After?.Identity || row.Returned && row.After is not null && !row.FieldRetained)
                    throw new InvalidDataException("Shader pointer store changed its actual retained property.");
                pointer = row.PointerAfter;
            }
            else if (row.PointerAfter != pointer || row.Returned && row.After?.Identity != pointer || row.FieldRetained || row.OldReleased)
                throw new InvalidDataException("Unchanged shader setter fabricated a different current property.");
            changed = row.Changed;
        }
        if (scene.PostActionContext != pointer)
            throw new InvalidDataException("Common post-action pointer has no actual shader-property publication.");
    }
}
