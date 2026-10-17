using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutControllerBodyNameStore(FalloutNifSceneKey Scene, FalloutNifSceneKey Body, string Name, long Changed);
internal sealed record FalloutControllerModelPreparation(FalloutReferenceModelDataInvocation Load,
    long Entered, long Changed, IReadOnlyList<FalloutControllerBodyNameStore> Stores,
    int Visited, bool Returned, string? Failure);

internal sealed partial class FalloutActorLogicalScene
{
    private FalloutControllerModelPreparation? _controllerModelPreparation;
    internal bool HasControllerBodyNames => _controllerModelPreparation is { Returned: true, Failure: null };

    internal bool ReadControllerModelMotionEnabled()
    {
        RequireIdle();
        var extras = ((FalloutNifNode)Root.Source).ExtraData.Where(index => index >= 0)
            .Select(index => Asset(Root.Key.Asset).ReadObject(index)).OfType<FalloutNifBsxFlags>()
            .Where(extra => extra.Name == FalloutPlayerModelLoadContinuationSource.ModelMotionFlagsName).ToArray();
        if (extras.Length > 1) throw new InvalidDataException("Current controller model repeats its source BSX flag field.");
        return extras.Length == 1 && (extras[0].Flags & FalloutPlayerModelLoadContinuationSource.ModelMotionEnabledFlag) != 0;
    }

    internal void PrepareControllerModel(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(requireActual); requireActual(load);
        if (_controllerModelPreparation is not null)
            throw new InvalidOperationException("Controller model preparation replayed an entered source traversal.");
        var fault = _reentry; var stores = new List<FalloutControllerBodyNameStore>();
        _controllerModelPreparation = new(load, Next(), _sequence, stores.AsReadOnly(), 0, false, null);
        _busy = true;
        try
        {
            foreach (var node in _order)
            {
                Check();
                if (ControllerRigidBody(node) is { } body)
                    stores.Add(new(node.Key, new(node.Key.Asset, body.Block.Index), node.Name, Next()));
                _controllerModelPreparation = _controllerModelPreparation with
                { Visited = _controllerModelPreparation.Visited + 1, Changed = Next() };
                Check();
            }
            _controllerModelPreparation = _controllerModelPreparation with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _controllerModelPreparation = _controllerModelPreparation with { Failure = error.Message, Changed = Next() };
            throw;
        }
        finally { _busy = false; }

        void Check()
        {
            requireActual(load);
            if (_reentry != fault || _retired || load.ThreadId != _thread)
                throw new InvalidOperationException("Controller model traversal lost its actual source thread or swallowed reentry.");
        }
    }

    internal string ReadControllerBodyName(int asset, int body)
    {
        RequireIdle();
        var call = _controllerModelPreparation;
        if (call is not { Returned: true, Failure: null })
            throw new NotSupportedException("source-controller-model-body-name-traversal-unreturned");
        return call.Stores.LastOrDefault(store => store.Body == new FalloutNifSceneKey(asset, body))?.Name ??
            throw new InvalidDataException("Current controller model has no actual body-name store.");
    }

    internal FalloutNifCollisionObject? ReadCurrentCollision(FalloutNifSceneObject node)
    {
        RequireIdle();
        if (!_objects.TryGetValue(node.Key, out var current) || !ReferenceEquals(node, current))
            throw new InvalidDataException("Current scene collision read selected another actual logical object.");
        return CollisionDeclaration(node);
    }
    private FalloutNifRigidBody? ControllerRigidBody(FalloutNifSceneObject node)
    {
        // Internal constructor/restore traversal already owns the scene's busy
        // scope. The declaration read does not enter a second operation.
        var collision = CollisionDeclaration(node);
        if (collision is null || collision.Body < 0) return null;
        // The source type-chain test includes derived rigid bodies and skips
        // other physical objects, including actual shape phantoms.
        return Asset(node.Key.Asset).ReadObject(collision.Body) as FalloutNifRigidBody;
    }
    private FalloutNifCollisionObject? CollisionDeclaration(FalloutNifSceneObject node)
    {
        var index = node.Source switch
        {
            FalloutNifNode source => source.CollisionObject,
            FalloutNifGeometry source => source.CollisionObject,
            FalloutNifAmbientLight source => source.CollisionObject,
            FalloutNifPointLight source => source.Light.CollisionObject,
            _ => throw new NotSupportedException("source-controller-model-scene-class-unowned:" + node.Source.Block.TypeName)
        };
        if (index < 0) return null;
        var sourceFile = Asset(node.Key.Asset);
        return sourceFile.ReadObject(index) as FalloutNifCollisionObject ??
            throw new NotSupportedException("source-controller-model-collision-wrapper-class-unowned");
    }

    private void RestoreControllerModelPreparation(FalloutActorLogicalSceneSnapshot saved)
    {
        var call = saved.ControllerModelPreparation;
        if (call is null) return;
        var expected = _order.Take(call.Visited).Select(node => (Node: node, Body: ControllerRigidBody(node)))
            .Where(row => row.Body is not null).ToArray();
        if (call.Visited > _order.Count || call.Returned && call.Visited != _order.Count || call.Stores.Count != expected.Length)
            throw new InvalidDataException("Cold controller model changed its actual traversed source denominator.");
        foreach (var (store, actual) in call.Stores.Zip(expected))
            if (store.Scene != actual.Node.Key || store.Body != new FalloutNifSceneKey(actual.Node.Key.Asset, actual.Body!.Block.Index) ||
                store.Name != actual.Node.Name)
                throw new InvalidDataException("Cold controller body name changed its actual source target or ordered store.");
        _controllerModelPreparation = call with { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
    }

    private static void ValidateControllerModelPreparation(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.ControllerModelPreparation is not { } call) return;
        if (call.Load.Call == Guid.Empty || call.Load.ThreadId < 1 || call.Entered < 1 || call.Changed <= call.Entered ||
            call.Changed > saved.Sequence || call.Stores is null || call.Visited < 0 || call.Visited > saved.Objects.Count ||
            call.Returned && (call.Visited != saved.Objects.Count || call.Failure is not null) ||
            !call.Returned && call.Failure is null || call.Failure is not null && call.Failure != saved.Failure ||
            call.Stores.Any(store => store.Changed <= call.Entered || store.Changed >= call.Changed ||
                store.Body.Asset != store.Scene.Asset || store.Body.Block < 0 ||
                !saved.Objects.Any(node => node.Key == store.Scene && node.Name == store.Name)) ||
            !call.Stores.Select(store => store.Changed).SequenceEqual(call.Stores.Select(store => store.Changed).Order()))
            throw new InvalidDataException("Cold controller model lost its ordered name fields or failed traversal prefix.");
    }
}
