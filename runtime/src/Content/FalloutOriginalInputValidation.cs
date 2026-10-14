namespace OpenNV.Runtime.Content;

internal static class FalloutOriginalInputValidation
{
    internal static void ValidateQuery(FalloutOriginalInputSource source, string stack, FalloutOriginalInputQuery query)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(query);
        if (query.Source != source.Identity || query.Stack != stack || query.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(query.Provider, query.Window) || !Invocation(query.Invocation) ||
            query.Sequence < 1 || query.InputRevision < 0 || query.BindingsRevision < 0 || query.BufferRevision < 0 ||
            query.Observations is null || query.Observations.Count > 2 || query.Count is < 0 or > 2 ||
            !Enum.IsDefined(query.Step) || query.Step is not (FalloutOriginalInputQueryStep.Returned or FalloutOriginalInputQueryStep.Failed) ||
            query.Step == FalloutOriginalInputQueryStep.Returned && (query.FailedAt is not null || query.Failure is not null || query.ControllerSelected is null) ||
            query.Step == FalloutOriginalInputQueryStep.Failed && (query.FailedAt is null || !Enum.IsDefined(query.FailedAt.Value) ||
                query.FailedAt is FalloutOriginalInputQueryStep.Returned or FalloutOriginalInputQueryStep.Failed || string.IsNullOrWhiteSpace(query.Failure)))
            throw new InvalidDataException("Original input query changed its actual source, owner, invocation or returned/failed prefix.");
        if (query.ControllerSelected != false && query.Observations.Count != 0 ||
            query.Observations.Count == 2 && (query.Observations[0].Device != FalloutOriginalInputDevice.Keyboard || query.Observations[1].Device != FalloutOriginalInputDevice.Mouse))
            throw new InvalidDataException("Original input query changed its selected device or ordered count observations.");
        var sum = 0;
        foreach (var observation in query.Observations)
        {
            if (observation is null || !Enum.IsDefined(observation.Device) || observation.Binding == byte.MaxValue || observation.Result is < 0 or > 1)
                throw new InvalidDataException("Original input query contains an invalid or skipped device observation.");
            ValidateObservation(query, observation); sum += observation.Result;
        }
        if (query.Step == FalloutOriginalInputQueryStep.Returned)
        {
            var menu = query.Control == 14 && sum > 0 && (query.BeforeMenuWord & 8) != 0;
            var word = menu && query.Mode is not (0 or 1) ? (ushort)(query.BeforeMenuWord & ~8) : query.BeforeMenuWord;
            if (query.Count != (menu ? 0 : sum) || query.AfterMenuWord != word ||
                query.ControllerSelected == true && query.Control < 29)
                throw new InvalidDataException("Original input query changed its actual count or source menu-word stores.");
        }
        else if (query.Count != sum && query.FailedAt != FalloutOriginalInputQueryStep.MenuWord)
            throw new InvalidDataException("Failed original input query lost its preceding device count.");
    }
    private static void ValidateObservation(FalloutOriginalInputQuery query, FalloutOriginalInputObservation observation)
    {
        var mode = query.Control == 28 ? 1 : query.Mode;
        var invalidMode = observation.Device == FalloutOriginalInputDevice.Keyboard ? (uint)mode > 3 : (uint)mode > 4;
        if (observation.Device == FalloutOriginalInputDevice.Mouse && observation.Binding is 8 or 9)
        {
            if (observation.Wheel is not { } wheel || observation.Current is not null || observation.Previous is not null ||
                observation.EffectiveButton is not null || observation.Swap is not null ||
                observation.Result != ((observation.Binding == 8 ? wheel > 0 : wheel < 0) ? 1 : 0))
                throw new InvalidDataException("Original wheel observation replaced signed source bytes with a button/hook value.");
            return;
        }
        if (observation.Wheel is not null) throw new InvalidDataException("Original button observation has a wheel value.");
        if (observation.Device == FalloutOriginalInputDevice.Mouse)
        {
            var button = (int)observation.Binding;
            if (button > 7 || button <= 1 && observation.Swap is null || button > 1 && observation.Swap is not null)
                throw new InvalidDataException("Original mouse observation lost its current swap producer.");
            if (button <= 1 && observation.Swap != 0) button ^= 1;
            if (observation.EffectiveButton != button) throw new InvalidDataException("Original mouse observation changed its swapped button.");
        }
        else if (observation.Swap is not null || observation.EffectiveButton is not null)
            throw new InvalidDataException("Original keyboard observation has foreign mouse fields.");
        if (query.Control is 29 or 30)
        {
            if (observation.Device != FalloutOriginalInputDevice.Keyboard || observation.Current is null || observation.Previous is null ||
                observation.Result != (((observation.Current.Value | observation.Previous.Value) & 0x80) != 0 ? 1 : 0))
                throw new InvalidDataException("Extra original keyboard query changed its mode-independent source bytes.");
            return;
        }
        if (invalidMode)
        {
            if (observation.Current is not null || observation.Previous is not null || observation.Result != 0)
                throw new InvalidDataException("Original invalid mode read a device buffer.");
            return;
        }
        var modeFour = observation.Device == FalloutOriginalInputDevice.Mouse && mode == 4;
        if (observation.Current is not { } current || (mode == 0 || modeFour ? observation.Previous is not null : observation.Previous is null) ||
            observation.Result != (modeFour ? current != 0 ? 1 : 0 : FalloutDirectInputState.Edge(current, observation.Previous, mode)))
            throw new InvalidDataException("Original device query changed bit7 edge or independent mode-four semantics.");
    }
    internal static void ValidateSnapshot(FalloutOriginalInputSource source, string stack, FalloutOriginalInputSnapshot saved)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Source != source.Identity || saved.Stack != stack || saved.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(saved.Provider, saved.Window) || !Enum.IsDefined(saved.HookLane) ||
            saved.BufferRevision < 0 || saved.PollSequence < 0 || saved.QuerySequence < 0 || saved.Known is null || saved.Construction is null ||
            saved.Construction.Platform == Guid.Empty || string.IsNullOrWhiteSpace(saved.Construction.Owner) ||
            saved.Keyboard?.Count != 256 || saved.PreviousKeyboard?.Count != 256 || saved.Mouse?.Count != 20 || saved.PreviousMouse?.Count != 20 ||
            saved.ModeFourMouse?.Count != 8 || saved.Controller?.Count != 16 || saved.PreviousController?.Count != 16 ||
            saved.Construction.Returned && (saved.Construction.Failure is not null || saved.Construction.Swap is null || saved.Construction.DoubleClickMilliseconds is null))
            throw new InvalidDataException("Original input snapshot changed its actual source, owner, extents or constructor prefix.");
        if (saved.Known.Swap && saved.Swap != saved.Construction.Swap ||
            saved.Construction.DoubleClickMilliseconds is { } click && saved.DoubleClickMilliseconds != click ||
            saved.Known.Suppression && saved.ControllerSuppression != 0 || saved.Known.MenuWord && saved.MenuWord != 0)
            throw new InvalidDataException("Original input snapshot changed a current field without its actual admitted writer.");
        if (!saved.Construction.Returned && saved.Construction.DoubleClickMilliseconds is null &&
            (saved.Known.Keyboard || saved.Known.PreviousKeyboard || saved.Known.Mouse || saved.Known.PreviousMouse || saved.Known.Suppression))
            throw new InvalidDataException("Failed platform construction established unreached input buffer/suppression stores.");
        if (saved.Query is { } query)
        {
            ValidateQuery(source, stack, query);
            if (query.Factory != saved.Factory || query.Provider != saved.Provider || query.Window != saved.Window ||
                query.Sequence != saved.QuerySequence || query.BufferRevision > saved.BufferRevision)
                throw new InvalidDataException("Original input snapshot has a foreign or future query.");
        }
        else if (saved.QuerySequence != 0) throw new InvalidDataException("Original input snapshot lost its entered query history.");
        if (saved.Poll is { } poll)
        {
            if (poll.Source != source.Identity || poll.Stack != stack || poll.Factory != saved.Factory || poll.Sequence != saved.PollSequence ||
                poll.HookLane != saved.HookLane || poll.Sequence < 1 || poll.Returned && poll.Failure is not null ||
                !poll.Returned && string.IsNullOrWhiteSpace(poll.Failure) ||
                poll.DeviceScope != "first-party-keyboard-mouse-provider;original-joystick-enumeration-and-poll-unowned")
                throw new InvalidDataException("Original input snapshot has a foreign or incomplete poll prefix.");
            ValidateSelection(poll.Selection); ValidateDevice(poll.Keyboard, FalloutOriginalInputDevice.Keyboard, poll);
            ValidateDevice(poll.Mouse, FalloutOriginalInputDevice.Mouse, poll);
        }
        else if (saved.PollSequence != 0) throw new InvalidDataException("Original input snapshot lost its entered poll history.");
        ValidateSelection(saved.Selection);
        if (saved.UnownedWriter is { } writer && (!Invocation(writer.Invocation) || !Enum.IsDefined(writer.Field) ||
            string.IsNullOrWhiteSpace(writer.Producer) || writer.BufferRevision < 1 || writer.BufferRevision > saved.BufferRevision))
            throw new InvalidDataException("Original input snapshot has an invalid current writer prefix.");
    }
    private static void ValidateSelection(FalloutOriginalInputSelection? selection)
    {
        if (selection is null) return;
        if (selection.Sequence < 1 || selection.Failure is null && (selection.Status is null || selection.Selected is null ||
            selection.Status == 0 && selection.Suppression is null || selection.Status != 0 && selection.Suppression is not null ||
            selection.Selected != (selection.Status == 0 && selection.Suppression == 0)) ||
            selection.Failure is not null && (string.IsNullOrWhiteSpace(selection.Failure) || selection.Selected is not null))
            throw new InvalidDataException("Original input selection changed its actual current status/suppression branch.");
    }
    private static void ValidateDevice(FalloutOriginalInputDevicePoll? device, FalloutOriginalInputDevice expected, FalloutOriginalInputPoll poll)
    {
        if (device is null) return;
        if (device.Device != expected || device.Sequence != poll.Sequence ||
            device.Step is not (FalloutOriginalInputPollStep.Returned or FalloutOriginalInputPollStep.Failed) ||
            device.Step == FalloutOriginalInputPollStep.Returned && (device.Failure is not null || device.FailedAt is not null || device.AcquireStatus is null ||
                device.AcquireStatus is 0 or 1 && (device.PollStatus is null || device.ReadStatus is null) ||
                device.AcquireStatus is not (0 or 1) && (device.PollStatus is not null || device.ReadStatus is not null)) ||
            device.Step == FalloutOriginalInputPollStep.Failed && (device.FailedAt is null || !Enum.IsDefined(device.FailedAt.Value) || string.IsNullOrWhiteSpace(device.Failure)) ||
            device.HookApplied && (poll.HookLane != FalloutOriginalInputHookLane.ManagedControls || device.ReadStatus != 0))
            throw new InvalidDataException("Original input poll changed its actual acquire/read/hook branch prefix.");
    }
    private static bool Invocation(FalloutInputInvocation invocation) => invocation.PlayerProcess != Guid.Empty && invocation.Main != Guid.Empty &&
        invocation.Ordinal > 0 && invocation.Invocation != Guid.Empty;
}
