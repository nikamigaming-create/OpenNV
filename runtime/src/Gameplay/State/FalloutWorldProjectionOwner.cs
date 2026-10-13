using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutWorldProjectionOwner
{
    internal const string Schema = "opennv-source-world-projection/v1";
    internal const string FrustumBoundary = "source-world-projection-frustum-consumer-unowned";
    private readonly Func<bool> _living;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence, _stores, _reentry;
    private uint? _fov;
    private bool _writing, _constructing, _retired, _constructorEntered, _constructorReturned, _ordinary;
    private FalloutIniValue? _constructorSetting;
    private FalloutWorldProjectionInputs? _constructorInputs;
    private FalloutWorldProjectionCall? _call;
    private string? _failure;
    private FalloutWorldProjectionConstructorStep _constructorStep;
    private uint _sceneFlags = 0x0008000e;
    private FalloutWorldProjectionBounds? _bounds;
    internal FalloutPlayerWorldOrientationSource Source { get; }
    internal string Stack { get; }
    internal Guid World { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal string Name => "World";
    internal FalloutWorldProjectionCamera Camera { get; }
    internal FalloutWorldProjectionSceneChild Scene { get; }
    internal FalloutWorldProjectionCullingOwner Culling { get; }
    internal bool ConstructorReturned => _constructorReturned;
    internal string? SaveBlocker => _retired ? "source-current-World-projection-retired" :
        _writing || _constructing ? "source-current-World-projection-child-entered" :
        _failure ?? (!_constructorReturned || _fov is null ? "source-current-World-projection-constructor-unreturned" : null);
    internal object State => new
    {
        schema = Schema,
        source = Source.Identity,
        stack = Stack,
        world = World,
        factory = Factory,
        name = Name,
        sequence = _sequence,
        fovBits = _fov,
        stores = _stores,
        constructorEntered = _constructorEntered,
        constructorReturned = _constructorReturned,
        ordinaryConstructor = _ordinary,
        constructorStep = _constructorStep,
        constructorSetting = _constructorSetting,
        constructorInputs = _constructorInputs,
        call = _call,
        failure = _failure,
        sceneFlags = _sceneFlags,
        bounds = _bounds,
        camera = Camera.ReadState(),
        culling = Culling.State,
        writing = _writing,
        constructing = _constructing,
        retired = _retired,
        saveBlocker = SaveBlocker
    };

    internal FalloutWorldProjectionOwner(FalloutPlayerWorldOrientationSource source, string stack, Guid world,
        Func<bool> living, FalloutWorldProjectionSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(living);
        if (world == Guid.Empty) throw new ArgumentException("Projection root requires its actual world lifetime.", nameof(world));
        Source = source; Stack = stack; World = world; _living = living;
        Culling = new(this, saved?.Culling); Scene = new(this); Camera = new(this, construct: saved is null);
        if (saved is null) return;
        Validate(saved);
        if (saved.Source != source || saved.Stack != stack || saved.CapturedWorld == world || saved.Retired)
            throw new InvalidDataException("Cold world projection changed source/stack or resurrected its old world.");
        _sequence = saved.Sequence; _stores = saved.Stores; _fov = saved.CurrentFovBits; Camera.RatioBits = saved.CameraRatioBits;
        _ordinary = saved.OrdinaryConstructor; _constructorEntered = saved.ConstructorEntered; _constructorReturned = saved.ConstructorReturned;
        _constructorSetting = saved.ConstructorSetting; _call = saved.Call; _failure = saved.Failure;
        _constructorInputs = saved.ConstructorInputs;
        _constructorStep = saved.ConstructorStep; _sceneFlags = saved.SceneFlags; _bounds = saved.Bounds;
        Camera.Restore(saved.Camera);
        if (saved.Camera.Attached) Scene.AttachCamera(Camera);
    }

    internal void ConstructOrdinary(FalloutNumericIniSettings settings, IFalloutWorldProjectionConsumers? consumers = null)
    {
        RequireOwner();
        if (_constructorEntered) throw new InvalidOperationException("World projection constructor was replayed.");
        _constructorSetting = Source.ReadConstructorFov(settings, Stack);
        _constructorEntered = _ordinary = true; Next();
        Camera.InitializeOrdinary(); _constructorStep = FalloutWorldProjectionConstructorStep.OrdinaryCamera; Next();
        var constructorFault = _reentry; _constructing = true;
        try
        {
            _constructorInputs = FalloutWorldProjectionInputs.Read(Source, Stack, settings);
            Camera.MaximumFarNearRatio = BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Divide(
                BitConverter.UInt32BitsToSingle(_constructorInputs.ReadBaseFarBits()), (float)_constructorInputs.NearDistance.Number!.Value));
            _constructorStep = FalloutWorldProjectionConstructorStep.ForcedFov; Next();
            SetFov(Guid.NewGuid(), "actual-source-World-no-supplied-camera-constructor", FalloutWorldFovIngress.WorldConstructor,
                BitConverter.SingleToUInt32Bits((float)_constructorSetting.Number!.Value), true, null, false, settings, consumers);
            Scene.AttachCamera(Camera); _constructorStep = FalloutWorldProjectionConstructorStep.AttachedCamera; Next();
            _constructorStep = FalloutWorldProjectionConstructorStep.CameraUpdate; Next();
            if (consumers is null) throw new NotSupportedException(FrustumBoundary);
            var updates = Camera.Capture().Updates;
            consumers.RequireCurrent(); consumers.UpdateCamera(_call!, Camera); consumers.RequireCurrent(); RequireCurrent();
            if (_reentry != constructorFault || Camera.Capture().Updates != checked(updates + 1))
                throw new InvalidOperationException("World constructor camera child reentered or omitted the actual attached camera update.");
            _sceneFlags |= 0x800; _bounds = new(new(0, 0, 0), 0x3f800000); _sceneFlags |= 0x2000;
            _constructorStep = FalloutWorldProjectionConstructorStep.Bounds; Next();
            _constructorReturned = true; _constructorStep = FalloutWorldProjectionConstructorStep.Returned; Next();
        }
        catch (Exception error) { _failure ??= error.Message; Next(); throw; }
        finally { _constructing = false; }
    }

    internal void ConstructWithSuppliedCamera()
    {
        RequireOwner();
        if (_constructorEntered) throw new InvalidOperationException("World projection constructor was replayed.");
        _constructorEntered = true; _ordinary = false; Next();
        _constructorStep = FalloutWorldProjectionConstructorStep.SuppliedCamera;
        _failure = "source-world-scenegraph-supplied-camera-construction-unowned";
        throw new NotSupportedException(_failure);
    }

    internal uint ReadWrittenFov()
    {
        RequireCurrent();
        return _fov ?? throw new NotSupportedException("source-world-projection-current-FOV-unwritten");
    }

    internal void SetFov(Guid invocation, string producer, FalloutWorldFovIngress ingress, uint requested,
        bool force, FalloutWorldProjectionCamera? suppliedCamera, bool updateRatio, FalloutNumericIniSettings settings,
        IFalloutWorldProjectionConsumers? consumers = null)
    {
        RequireOwner(allowConstructor: ingress == FalloutWorldFovIngress.WorldConstructor); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (!_constructorEntered || invocation == Guid.Empty || _call?.Invocation == invocation || !Enum.IsDefined(ingress))
            throw new InvalidOperationException("World projection setter has no fresh actual source ingress.");
        if (ingress == FalloutWorldFovIngress.WorldConstructor && (!_constructing || _call is not null))
            throw new InvalidOperationException("World projection constructor setter was replayed outside its actual first construction.");
        var camera = suppliedCamera ?? Camera; camera.RequireCurrent();
        if (camera.Root.Source != Source || camera.Root.Stack != Stack || camera.Root.World != World)
            throw new InvalidOperationException("World projection setter selected a foreign current camera.");
        var before = _fov; var entered = Next(); var fault = _reentry; _writing = true;
        _call = new(invocation, producer, ingress, entered, entered, requested, force, suppliedCamera is not null,
            updateRatio, before, false, camera.Factory, FalloutWorldProjectionStep.CameraSelected, null, false, null);
        try
        {
            var equal = OrderedEqual(before, requested);
            if (ShouldStore(before, requested, force, suppliedCamera is not null))
            {
                _fov = requested; _stores = checked(_stores + 1);
                _call = _call with { Stored = true, Step = FalloutWorldProjectionStep.Stored, Changed = Next() };
            }
            if (suppliedCamera is null && !force && equal)
            {
                if (updateRatio) Ratio();
                Return(); return;
            }
            Step(FalloutWorldProjectionStep.Frustum);
            if (consumers is null) throw new NotSupportedException(FrustumBoundary);
            consumers.RequireCurrent(); Check();
            if (consumers.Source != Source || consumers.Stack != Stack || consumers.World != World || consumers.Factory == Guid.Empty)
                throw new InvalidOperationException("World projection selected foreign source consumers.");
            _call = _call with { Consumers = consumers.Factory, Changed = Next() };
            var frustumStores = camera.Capture().FrustumStores;
            consumers.BuildFrustum(_call, camera); Check(); consumers.RequireCurrent(); Check();
            if (camera.Capture().FrustumStores != checked(frustumStores + 1))
                throw new InvalidOperationException("World projection child omitted its actual current camera frustum store.");
            var updates = camera.Capture().Updates;
            Step(FalloutWorldProjectionStep.Camera); consumers.UpdateCamera(_call, camera); Check();
            consumers.RequireCurrent(); Check();
            if (camera.Capture().Updates != checked(updates + 1))
                throw new InvalidOperationException("World projection child omitted its actual current camera matrix/bound update.");
            if (updateRatio) Ratio();
            Return();
        }
        catch (Exception error)
        {
            _failure ??= error.Message; _call = _call with { Failure = error.Message, Changed = Next() }; throw;
        }
        finally { _writing = false; }
        void Step(FalloutWorldProjectionStep step) => _call = _call! with { Step = step, Changed = Next() };
        void Return() => _call = _call! with { Step = FalloutWorldProjectionStep.Returned, Returned = true, Changed = Next() };
        void Ratio()
        {
            Step(FalloutWorldProjectionStep.Ratio);
            var setting = Source.ReadConstructorFov(settings, Stack);
            camera.RatioBits = BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Divide(
                BitConverter.UInt32BitsToSingle(requested), (float)setting.Number!.Value));
        }
        void Check()
        {
            if (Environment.CurrentManagedThreadId != _thread || _reentry != fault || !_living())
                throw new InvalidOperationException("World projection child changed thread, reentered or retired its actual root.");
        }
    }

    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId || !_living())
            throw new InvalidOperationException("World projection lost its actual thread/world lifetime.");
    }
    internal static bool OrderedEqual(uint? before, uint requested) => before is { } previous &&
        BitConverter.UInt32BitsToSingle(previous) == BitConverter.UInt32BitsToSingle(requested);
    internal static bool ShouldStore(uint? before, uint requested, bool force, bool supplied) => !supplied && (force || !OrderedEqual(before, requested));
    private void RequireOwner(bool allowFailure = false, bool allowConstructor = false)
    {
        RequireCurrent();
        if (_writing || _constructing && !allowConstructor)
        { _reentry = checked(_reentry + 1); throw new InvalidOperationException("World projection setter/constructor reentered its source owner."); }
        if (!allowFailure && _failure is { } failure) throw new NotSupportedException(failure);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal void Retire() { if (_retired) return; RequireOwner(allowFailure: true); _retired = true; }
}
