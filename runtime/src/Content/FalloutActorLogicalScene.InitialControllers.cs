using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutInitialTransformUpdate(FalloutNifSceneKey Controller, FalloutNifSceneKey Target,
    ushort Flags, uint FirstTime, uint LastTime, uint WeightedTime, uint ScaledTime,
    int Interpolator, FalloutNifTransformComponents Components, bool Returned, long Changed);
internal sealed record FalloutInitialPlayerModelUpdate(FalloutReferenceModelDataInvocation Load,
    long Entered, long Changed, IReadOnlyList<FalloutInitialTransformUpdate> Controllers,
    bool WorldUpdated, bool Returned, string? Failure);

internal sealed partial class FalloutActorLogicalScene
{
    // The source initial model update supplies absolute time zero before any
    // AnimData or external sequence is constructed. These are the actual
    // embedded NiTimeController fields, independent of later KF clocks.
    internal const ushort InitialClampedTransformFlags = 0x004c;
    private FalloutInitialPlayerModelUpdate? _initialModelUpdate;

    internal void UpdateInitialPlayerModel(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load);
        if (_initialModelUpdate is not null)
            throw new InvalidOperationException("Initial model update cannot replay an entered source call.");
        var fault = _reentry;
        var calls = new List<FalloutInitialTransformUpdate>();
        _initialModelUpdate = new(load, Next(), _sequence, calls.AsReadOnly(), false, false, null);
        _busy = true;
        try
        {
            var anchor = FalloutNifSceneMath.Identity with { Scale = _model.Appearance.Height };
            foreach (var node in _order)
            {
                foreach (var controller in ReadVisualControllerChain(node))
                {
                    Check();
                    if (controller is not FalloutNifTransformController transform)
                        throw new NotSupportedException("source-initial-model-controller-class-unowned:" + controller.Block.TypeName);
                    var time = transform.Time;
                    if (time.Flags != InitialClampedTransformFlags || time.Frequency != 1 || time.Phase != 0 ||
                        !float.IsFinite(time.StartTime) || !float.IsFinite(time.StopTime))
                        throw new NotSupportedException("source-initial-model-transform-clock-branch-unowned");
                    // The original clamp tests the upper end first. An empty
                    // constructor range (+MAX,-MAX) therefore returns -MAX.
                    var scaled = InitialTransformTime(time);
                    var call = new FalloutInitialTransformUpdate(new(node.Key.Asset, transform.Block.Index), node.Key,
                        time.Flags, 0, 0, 0, BitConverter.SingleToUInt32Bits(scaled), transform.Interpolator,
                        FalloutNifTransformComponents.None, false, Next());
                    calls.Add(call); _initialModelUpdate = _initialModelUpdate with { Changed = Next() };
                    if (transform.Interpolator >= 0)
                    {
                        var sample = new FalloutNifAnimationSampler(Asset(node.Key.Asset), transform.Interpolator).Sample(scaled);
                        if (sample.Components != FalloutNifTransformComponents.None)
                        {
                            var local = node.Local;
                            var pose = new FalloutNifTransform(sample.Translation ?? local.Translation,
                                sample.Rotation is { } rotation ? FalloutNifSceneMath.Rotation(rotation) : local.RotationRowMajor,
                                sample.Scale is { } scale ? MathF.Abs(scale) : local.Scale);
                            FalloutNifSceneMath.Validate(pose);
                            node.Local = FalloutNifSceneMath.Copy(pose); node.PoseStores = checked(node.PoseStores + 1); Next();
                        }
                        call = call with { Components = sample.Components };
                    }
                    calls[^1] = call with { Returned = true, Changed = Next() };
                    _initialModelUpdate = _initialModelUpdate with { Changed = Next() }; Check();
                }
                node.World = FalloutNifSceneMath.Compose(node.Parent?.World ?? anchor, node.Local); Next();
            }
            UpdateCurrentBounds();
            _initialModelUpdate = _initialModelUpdate with { WorldUpdated = true, Changed = Next() };
            Check();
            _initialModelUpdate = _initialModelUpdate with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _initialModelUpdate = _initialModelUpdate with { Failure = error.Message, Changed = Next() };
            throw;
        }
        finally { _busy = false; }
        void Check()
        {
            requireActual(load);
            if (_reentry != fault) throw new InvalidOperationException("Initial model update swallowed source scene reentry.");
        }
    }

    private static float InitialTransformTime(FalloutNifTimeController time) =>
        0 > time.StopTime ? time.StopTime : MathF.Max(time.StartTime, 0);

    private void RestoreInitialModelUpdate(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.InitialModelUpdate is not { } update) return;
        var declared = _order.SelectMany(node => ReadVisualControllerChain(node)
            .Select(controller => (Controller: new FalloutNifSceneKey(node.Key.Asset, controller.Block.Index), Target: node.Key))).ToArray();
        if (update.Controllers.Count > declared.Length || update.Returned && update.Controllers.Count != declared.Length)
            throw new InvalidDataException("Cold initial update omitted or invented source controllers.");
        for (var index = 0; index < update.Controllers.Count; ++index)
        {
            var call = update.Controllers[index];
            if (call.Controller != declared[index].Controller || call.Target != declared[index].Target)
                throw new InvalidDataException("Cold initial update reordered its actual source controllers.");
            if (Asset(call.Controller.Asset).ReadObject(call.Controller.Block) is not FalloutNifTransformController controller ||
                controller.Time.Flags != call.Flags || controller.Time.Target != call.Target.Block ||
                controller.Time.Frequency != 1 || controller.Time.Phase != 0 || controller.Interpolator != call.Interpolator ||
                BitConverter.SingleToUInt32Bits(InitialTransformTime(controller.Time)) != call.ScaledTime)
                throw new InvalidDataException("Cold initial model update changed its original controller or clock.");
        }
        _initialModelUpdate = update; // History and fields only; never sample or run the old loader.
    }

    private static void ValidateInitialModelUpdate(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.InitialModelUpdate is not { } update) return;
        if (update.Load is null || update.Load.Call == Guid.Empty || update.Load.ReferenceProcess == Guid.Empty ||
            update.Load.ReferenceConstructor == Guid.Empty || update.Load.CommonProcess == Guid.Empty || update.Load.Epoch < 1 ||
            update.Load.ThreadId < 1 || update.Load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
            update.Entered < 1 || update.Changed < update.Entered || update.Changed > saved.Sequence ||
            update.Controllers is null || update.Returned != (update.Failure is null) || update.Returned && !update.WorldUpdated ||
            update.Failure is not null && (string.IsNullOrWhiteSpace(update.Failure) || saved.Failure != update.Failure) ||
            update.Controllers.Select(call => call.Controller).Distinct().Count() != update.Controllers.Count)
            throw new InvalidDataException("Cold initial model update lost its actual call or failed prefix.");
        var previous = update.Entered;
        var targets = saved.Objects.Select(node => node.Key).ToHashSet();
        foreach (var call in update.Controllers)
        {
            if (call.Flags != InitialClampedTransformFlags || call.FirstTime != 0 || call.LastTime != 0 || call.WeightedTime != 0 ||
                call.Controller.Asset != call.Target.Asset || call.Controller.Block < 0 || call.Interpolator < -1 ||
                call.Changed <= previous || call.Changed >= update.Changed ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(call.ScaledTime)) ||
                (call.Components & ~FalloutNifTransformComponents.All) != 0 ||
                call.Interpolator < 0 && call.Components != FalloutNifTransformComponents.None ||
                !call.Returned && (update.Returned || !ReferenceEquals(call, update.Controllers[^1])) ||
                !targets.Contains(call.Target))
                throw new InvalidDataException("Cold initial transform update lost ordered clock or pose fields.");
            previous = call.Changed;
        }
    }
}
