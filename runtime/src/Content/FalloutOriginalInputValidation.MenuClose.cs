namespace OpenNV.Runtime.Content;

internal static partial class FalloutOriginalInputValidation
{
    private static void ValidateMenuClose(FalloutOriginalInputSource source, string stack, Guid factory, long revision, FalloutOriginalMenuClose close)
    {
        if (close.Invocation == Guid.Empty || close.InterfaceProcess == Guid.Empty || close.Menu == Guid.Empty ||
            close.InputFactory != factory || close.Source != source.Identity || close.Stack != stack || close.BufferRevision > revision ||
            close.Observations is null || close.Observations.Count > 2 || close.Count is < 0 or > 2 ||
            close.Returned && (close.ControllerSelected != false || close.Failure is not null) ||
            !close.Returned && string.IsNullOrWhiteSpace(close.Failure))
            throw new InvalidDataException("Menu close retained a foreign or incomplete actual input query.");
        var devices = new HashSet<FalloutOriginalInputDevice>(); var sum = 0; var mouseRead = false;
        foreach (var observation in close.Observations)
        {
            if (observation.Device is not (FalloutOriginalInputDevice.Keyboard or FalloutOriginalInputDevice.Mouse) ||
                !devices.Add(observation.Device) || mouseRead || observation.Result is < 0 or > 1)
                throw new InvalidDataException("Menu close changed its ordered actual device observations.");
            ValidateObservation(14, 0, observation); sum += observation.Result;
            mouseRead = observation.Device == FalloutOriginalInputDevice.Mouse;
        }
        if (close.Returned)
        {
            var count = sum > 0 && (close.Before & 8) != 0 ? 0 : sum;
            if (close.Count != count || close.After != (count > 0 ? (ushort)(close.Before | 8) : close.Before))
                throw new InvalidDataException("Menu close changed its source count, MenuWord suppression or conditional destructor store.");
        }
    }

    internal static bool SameMenuClose(FalloutOriginalMenuClose first, FalloutOriginalMenuClose second) =>
        first.Invocation == second.Invocation && first.InterfaceProcess == second.InterfaceProcess && first.Menu == second.Menu &&
        first.InputFactory == second.InputFactory && first.Source == second.Source && first.Stack == second.Stack &&
        first.BufferRevision == second.BufferRevision && first.Sample == second.Sample && first.ControllerSelected == second.ControllerSelected &&
        first.Before == second.Before && first.After == second.After && first.Count == second.Count && first.Returned == second.Returned &&
        first.Failure == second.Failure && first.Observations.SequenceEqual(second.Observations);
}
