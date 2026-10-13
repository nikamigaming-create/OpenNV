using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutWorldProjectionOwner
{
    internal FalloutWorldProjectionSnapshot Capture()
    {
        RequireOwner(allowFailure: true);
        var saved = new FalloutWorldProjectionSnapshot(Schema, Source, Stack, World, Factory, _sequence, _fov, _stores,
            Camera.RatioBits, _ordinary, _constructorEntered, _constructorReturned, _constructorSetting, _call, _failure, _retired,
            _constructorStep, Camera.Capture(), _sceneFlags, _bounds, Culling.Capture(), _constructorInputs);
        Validate(saved); return saved;
    }

    internal static void Validate(FalloutWorldProjectionSnapshot saved)
    {
        saved.Source.Validate(); FalloutWorldProjectionCamera.Validate(saved.Camera); FalloutWorldProjectionCullingOwner.Validate(saved.Culling);
        saved.ConstructorInputs?.Validate();
        if (saved.Schema != Schema || string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedWorld == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Sequence < 0 || saved.Stores < 0 || saved.Stores > saved.Sequence ||
            saved.ConstructorReturned && !saved.ConstructorEntered ||
            (saved.Stores == 0) != (saved.CurrentFovBits is null) ||
            saved.OrdinaryConstructor && !saved.ConstructorEntered ||
            !Enum.IsDefined(saved.ConstructorStep) ||
            saved.ConstructorReturned != (saved.ConstructorStep == FalloutWorldProjectionConstructorStep.Returned) ||
            saved.CameraRatioBits != saved.Camera.Ratio ||
            !saved.ConstructorReturned && (saved.SceneFlags != 0x0008000e || saved.Bounds is not null) ||
            saved.Camera.Attached != (saved.OrdinaryConstructor && (int)saved.ConstructorStep >= (int)FalloutWorldProjectionConstructorStep.AttachedCamera) ||
            !saved.ConstructorEntered && (saved.Call is not null || saved.ConstructorSetting is not null || saved.Failure is not null || saved.ConstructorStep != FalloutWorldProjectionConstructorStep.Allocated) ||
            saved.ConstructorReturned && (!saved.Camera.Attached || saved.Bounds != new FalloutWorldProjectionBounds(new(0, 0, 0), 0x3f800000) || saved.SceneFlags != 0x0008280e))
            throw new InvalidDataException("Cold world projection contains an invalid actual constructor/publication prefix.");
        if (!saved.ConstructorEntered && (saved.Stores != 0 || saved.Camera.Updates != 1 || saved.Camera.FrustumStores != 0 ||
                !saved.Camera.Local.Same(FalloutWorldProjectionPose.Identity()) || !saved.Camera.World.Same(FalloutWorldProjectionPose.Identity()) ||
                saved.Camera.MaximumFarNearRatio != 0x461c4000 || saved.ConstructorInputs is not null))
            throw new InvalidDataException("Cold allocated projection manufactured ordinary constructor or current camera state.");
        if (saved.OrdinaryConstructor)
        {
            if (saved.ConstructorSetting is not
                {
                    Declaration:
                    {
                        Name: FalloutPlayerWorldOrientationSource.ConstructorFovName,
                        Collection: FalloutIniCollection.Main, Payload: FalloutPlayerWorldOrientationSource.ConstructorFovDefaultBits
                    }, Number: { } number
                } ||
                !double.IsFinite(number) || (double)(float)number != number || string.IsNullOrWhiteSpace(saved.ConstructorSetting.Origin) ||
                saved.ConstructorInputs is not null && (saved.ConstructorInputs.Source != saved.Source || saved.ConstructorInputs.Stack != saved.Stack))
                throw new InvalidDataException("Cold ordinary projection omitted its forced effective Main FOV store.");
            if ((int)saved.ConstructorStep >= (int)FalloutWorldProjectionConstructorStep.ForcedFov &&
                (saved.ConstructorInputs is null || saved.CurrentFovBits is null || saved.Stores < 1 || saved.Call is null))
                throw new InvalidDataException("Cold ordinary projection lost its original clip setup before the forced FOV store.");
        }
        else if (saved.ConstructorEntered && (saved.ConstructorReturned || saved.CurrentFovBits is not null ||
            saved.Call is not null || saved.Failure != "source-world-scenegraph-supplied-camera-construction-unowned"))
            throw new InvalidDataException("Cold supplied-camera constructor manufactured a written or returned ordinary FOV arm.");
        if (saved.Call is not { } call) return;
        if (call.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(call.Producer) || !Enum.IsDefined(call.Ingress) ||
            !Enum.IsDefined(call.Step) || call.Entered < 1 || call.Changed < call.Entered || call.Changed > saved.Sequence ||
            call.Camera == Guid.Empty || call.Consumers == Guid.Empty || call.Returned != (call.Step == FalloutWorldProjectionStep.Returned) ||
            call.Returned && call.Failure is not null || !call.Returned && call.Failure is null || call.Failure is not null && saved.Failure is null)
            throw new InvalidDataException("Cold world setter lost its entered, returned or failed child prefix.");
        var equal = OrderedEqual(call.BeforeBits, call.RequestedBits);
        var stored = ShouldStore(call.BeforeBits, call.RequestedBits, call.Force, call.SuppliedCamera);
        if (stored != call.Stored || saved.CurrentFovBits != (stored ? call.RequestedBits : call.BeforeBits) ||
            stored && saved.Stores < 1 || call.SuppliedCamera && call.Ingress == FalloutWorldFovIngress.WorldConstructor ||
            call.Ingress == FalloutWorldFovIngress.WorldConstructor &&
                (!call.Force || call.UpdateRatio || call.BeforeBits is not null || saved.Stores != 1 ||
                 call.RequestedBits != BitConverter.SingleToUInt32Bits((float)saved.ConstructorSetting!.Number!.Value) || saved.ConstructorReturned && !call.Returned))
            throw new InvalidDataException("Cold world setter changed force, ordered equality, supplied camera or committed bits.");
        if (call.Returned && (call.SuppliedCamera || call.Force || !equal) && call.Consumers is null)
            throw new InvalidDataException("Cold world setter manufactured returned projection/camera children.");
        if (call.Returned && call.UpdateRatio && !call.SuppliedCamera && saved.CameraRatioBits is null)
            throw new InvalidDataException("Cold world setter omitted its actual camera ratio store.");
    }
}
