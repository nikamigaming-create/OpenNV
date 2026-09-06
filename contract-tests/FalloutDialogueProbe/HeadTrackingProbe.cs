using OpenNV.Runtime.Content;

internal static class HeadTrackingProbe
{
    internal static void Run()
    {
        var first = new FalloutFormKey("Synthetic.esm", 101);
        var second = new FalloutFormKey("Synthetic.esm", 102);
        var state = new FalloutHeadTrackingState(0.3f);
        state.Look(first);
        Require(state.SelectedTarget == first && state.CachedTarget == first && !state.CanSelectDefault, "Script target was not selected.");
        state.StopLook();
        Require(state.SelectedTarget is null && state.CachedTarget == first &&
            state.Slots.First().Target == first && !state.Slots.First().Enabled, "StopLook enabled the previously disabled default slot or refreshed the cache.");
        state.Advance(0.2f, _ => true);
        state.Advance(0.2f, _ => true);
        Require(BitConverter.SingleToInt32Bits(state.DefaultHoldSeconds) == BitConverter.SingleToInt32Bits(0.3f - 0.2f - 0.2f) &&
            state.CanSelectDefault && state.SelectedTarget is null, "Default timer lost Float32 overshoot or invented automatic acquisition.");
        state.SetTarget(0, second);
        state.Look(first);
        state.StopLook();
        Require(state.SelectedTarget == first && state.Slots.First().Enabled, "StopLook did not preserve an enabled default slot.");
        state.SetTarget(4, second);
        state.Look(first);
        Require(state.SelectedTarget == second, "Script Look overrode a higher-priority owner.");
        state.Advance(0, target => target == first);
        Require(state.SelectedTarget == first && state.CachedTarget == first && !state.Slots.ElementAt(4).Enabled,
            "Unloaded target did not release priority/cache ownership.");
        var commands = FalloutLookCommands.Read("; ignored\nactor.Look target 0\nset counter to 1\nactor.StopLook player\nLook target\nStopLook");
        Require(commands.Count == 4 && commands[0] == new FalloutLookCommand(0, "actor", "target") &&
            commands[1] == new FalloutLookCommand(2, "actor", null) && commands[2].Actor is null && commands[3].Target is null,
            "Look source order, optional actor or zero-parameter StopLook changed.");
        foreach (var invalid in new[] { "Look", "actor.Look target 1", "if enabled\nLook target\nendif", "StopLook target 1", "Look target invalid" })
            Reject(() => FalloutLookCommands.Read(invalid));
        Console.WriteLine("Head target priority, StopLook lifetime, invalidation and script command contracts passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported Look source was admitted.");
    }
}
