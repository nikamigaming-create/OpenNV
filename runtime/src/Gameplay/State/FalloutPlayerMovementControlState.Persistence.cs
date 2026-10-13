using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerMovementControlState
{
    internal static void Validate(FalloutPlayerMovementControlSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.CapturedProcess == Guid.Empty || saved.Factory == Guid.Empty || saved.Sequence < 0 || saved.Mode != 0 ||
            saved.Inhibition != FalloutPlayerControlInhibition.Constructed || saved.JoystickAvailable != 0 ||
            saved.JoystickSuppressed != 0 || saved.CameraMode != 0 || saved.ControllerCameraMode != 0 || saved.FirstCacheWord != 0 || saved.SecondCacheWord != 0 ||
            saved.CacheStores < 0 || saved.OrientationStores < 0 || saved.Fields is not { } fields || fields.Process != saved.CapturedProcess)
            throw new InvalidDataException("Player movement/control changed its independent source fields or current owner identity.");
        saved.Source.Validate(); FalloutPlayerControlFieldsState.Validate(fields, saved.Source, saved.Stack);
        saved.Configuration?.Validate(saved.Source, saved.Stack);
        if (saved.Call is not { } call)
        {
            if (saved.Sequence != 0 || saved.CacheStores != 0 || saved.OrientationStores != 0 ||
                saved.YawCache != 0 || saved.PitchCache != 0 || saved.Failure is not null)
                throw new InvalidDataException("Uncalled Player controls contain fabricated control stores or a failure.");
            return;
        }
        if (call.Main == Guid.Empty || call.Process == Guid.Empty || call.MainOrdinal < 1 || call.Entered < 1 ||
            call.Changed <= call.Entered || call.Changed != saved.Sequence || call.FirstArgument != 0 || call.LastArgument != 0 ||
            (call.MoverWord & ~FalloutPlayerMovementControlSource.MoverMask) != 0 || call.Failure != saved.Failure ||
            call.Returned != (call.Step == FalloutPlayerMovementControlStep.Returned) || call.Returned != (call.Failure is null) ||
            call.Result != (call.Returned ? false : null) || call.MouseReads is null || call.MouseReads.Count > 3 ||
            call.CacheStores is < 0 or > 2 || call.CacheStores > saved.CacheStores ||
            (saved.CacheStores - call.CacheStores) % 2 != 0 || !Enum.IsDefined(call.Step))
            throw new InvalidDataException("Player movement/control lost its actual invocation, arguments or entered/returned prefix.");
        if (call.InputFactory is null)
        {
            if (call.Step != FalloutPlayerMovementControlStep.InputOwner || call.InputWindow is not null ||
                call.InputProvider is not null || call.CacheStores != 0 || call.MouseReads.Count != 0 || HasLaterReads(call))
                throw new InvalidDataException("Failed current-input getter admitted later cache, configuration or orientation reads.");
            return;
        }
        if (call.InputFactory == Guid.Empty || call.InputWindow is not { } window || call.InputProvider is not { } provider ||
            !FalloutDirectInputState.ValidProvider(provider, window) || call.CacheStores != 2 ||
            call.Step < FalloutPlayerMovementControlStep.MouseAxes)
            throw new InvalidDataException("Player controls lost the actual current input factory before their ordered cache stores.");
        for (var index = 0; index < call.MouseReads.Count; ++index)
        {
            var read = call.MouseReads[index];
            if (read.Axis != index + 1 || read.Sample == 0 || read.Revision < 1 ||
                index != 0 && (read.Sample != call.MouseReads[0].Sample || read.Revision != call.MouseReads[0].Revision))
                throw new InvalidDataException("Player controls changed cached-axis order or mixed independent input samples.");
        }
        if (call.Step == FalloutPlayerMovementControlStep.MouseAxes)
        {
            if (call.MouseReads.Count == 3 || HasLaterReads(call))
                throw new InvalidDataException("Failed cached mouse read fabricated later configuration or a complete axis extent.");
            return;
        }
        if (call.MouseReads.Count != 3)
            throw new InvalidDataException("Player controls skipped actual cached mouse reads before configuration.");
        if (call.Configuration is not { } configuration)
        {
            if (call.Step != FalloutPlayerMovementControlStep.Configuration || call.Failure != ConfigurationBoundary || HasLaterReads(call))
                throw new InvalidDataException("Player controls skipped their independent current invert-Y setting.");
            return;
        }
        configuration.Validate(saved.Source, saved.Stack);
        if (configuration != saved.Configuration || call.Step < FalloutPlayerMovementControlStep.PlayerFlags)
            throw new InvalidDataException("Player control read changed its actual registered/layered configuration.");
        if (call.FieldsRead is not { } readFields)
        {
            var failure = fields.WordAvailable ? fields.Failure : FalloutPlayerControlFieldsState.ObservedWriterBoundary;
            if (call.Step != FalloutPlayerMovementControlStep.PlayerFlags || failure != call.Failure || call.Axes is not null ||
                call.WorldFov is not null || call.Yaw is not null || call.Pitch is not null)
                throw new InvalidDataException("Player controls bypassed a failed source flag writer or admitted later getters.");
            return;
        }
        FalloutPlayerControlFieldsState.Validate(readFields, saved.Source, saved.Stack);
        if (readFields.Process != call.Process || readFields.Failure is not null || !readFields.WordAvailable || call.Step < FalloutPlayerMovementControlStep.Wheel)
            throw new InvalidDataException("Player control flags borrowed another actual invocation or a failed producer.");
        var axes = new FalloutPlayerControlAxes(call.MouseReads[0].Value,
            configuration.InvertY.Number != 0 ? unchecked(-call.MouseReads[1].Value) : call.MouseReads[1].Value,
            call.MouseReads[2].Value);
        if ((readFields.DisabledWord & FalloutPlayerControlFieldsState.LookingDisabled) != 0) axes = new(0, 0, 0);
        if (call.Axes != axes)
            throw new InvalidDataException("Player controls changed source Int32 inversion or independent flag suppression.");
        if (call.WorldFov is not { } fov)
        {
            if (call.FovScaleBits is not null || call.Yaw is not null || call.YawBits is not null || call.Pitch is not null || call.PitchBits is not null ||
                (axes.Wheel != 0 ? call.Step != FalloutPlayerMovementControlStep.Wheel || call.Failure != WheelBoundary :
                    call.Step != FalloutPlayerMovementControlStep.WorldFov))
                throw new InvalidDataException("Player controls bypassed the selected wheel service or actual current world-projection FOV.");
            return;
        }
        Lease(fov.Lease);
        if (axes.Wheel != 0 || call.Step < FalloutPlayerMovementControlStep.Yaw ||
            call.FovScaleBits != BitConverter.SingleToUInt32Bits(FalloutPlayerControlOrientation.WorldFovScale((float)configuration.WorldFov.Number!.Value, fov.Bits)))
            throw new InvalidDataException("Player controls fabricated a world-FOV getter or changed its exact Float32 scaling.");
        if (call.Yaw is not { } yaw)
        {
            if (call.Step != FalloutPlayerMovementControlStep.Yaw || call.YawBits is not null || call.Pitch is not null || call.PitchBits is not null)
                throw new InvalidDataException("Player controls skipped their actual yaw getter before the independent pitch getter.");
            return;
        }
        Lease(yaw.Lease);
        if (call.Step < FalloutPlayerMovementControlStep.Pitch || call.YawBits != FalloutPlayerControlOrientation.Yaw(yaw, readFields.YawOffsetBits) ||
            saved.YawCache != call.YawBits || call.Pitch is { } pitch && (call.PitchBits != pitch.Bits || saved.PitchCache != pitch.Bits) ||
            saved.OrientationStores < (call.Pitch is null ? 1 : 2) ||
            (saved.OrientationStores - (call.Pitch is null ? 1 : 2)) % 2 != 0)
            throw new InvalidDataException("Player controls lost genuine ordered orientation getter/cache stores.");
        if (call.Pitch is not { } currentPitch)
        {
            if (call.Step != FalloutPlayerMovementControlStep.Pitch || call.PitchBits is not null)
                throw new InvalidDataException("Player controls skipped a failed current pitch getter.");
            return;
        }
        Lease(currentPitch.Lease); _ = FalloutPlayerControlOrientation.Finite(currentPitch.Bits);
        if (call.Step < FalloutPlayerMovementControlStep.CameraMode || call.Returned && axes != new FalloutPlayerControlAxes(0, 0, 0) ||
            call.Step == FalloutPlayerMovementControlStep.MotionWriters && call.Failure != MotionBoundary)
            throw new InvalidDataException("Player controls bypassed camera/motion writers or fabricated their completed Boolean return.");

        void Lease(FalloutPlayerControlReadLease lease)
        {
            lease.Validate();
            if (lease.Source != saved.Source.Identity || lease.Stack != saved.Stack || lease.Process != call.Process ||
                lease.Main != call.Main || lease.MainOrdinal != call.MainOrdinal || lease.Factory != fov.Lease.Factory)
                throw new InvalidDataException("Player orientation reads changed source/process/factory or actual Main invocation.");
        }
        static bool HasLaterReads(FalloutPlayerMovementControlCall entered) => entered.Configuration is not null ||
            entered.FieldsRead is not null || entered.Axes is not null || entered.WorldFov is not null || entered.FovScaleBits is not null ||
            entered.Yaw is not null || entered.YawBits is not null || entered.Pitch is not null || entered.PitchBits is not null;
    }
}
