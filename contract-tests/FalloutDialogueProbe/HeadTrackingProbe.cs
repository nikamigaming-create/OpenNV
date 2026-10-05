using OpenNV.Runtime.Content;
using System.Text.Json;

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
        Persistence(state);
        state.SetTarget(0, second);
        var enabledEmpty = new FalloutHeadTrackingState(.3f);
        enabledEmpty.SetTarget(0, first); enabledEmpty.StopLook();
        Require(enabledEmpty.Slots.First().Enabled && enabledEmpty.Slots.First().Target is null && enabledEmpty.CachedTarget == first,
            "Empty script slot StopLook changed the independent default flag or stale cache.");
        Persistence(enabledEmpty);
        state.Look(first);
        state.StopLook();
        Require(state.SelectedTarget == first && state.Slots.First().Enabled, "StopLook did not preserve an enabled default slot.");
        state.SetTarget(4, second);
        state.Look(first);
        Require(state.SelectedTarget == second, "Script Look overrode a higher-priority owner.");
        state.Advance(0, target => target == first);
        Require(state.SelectedTarget == first && state.CachedTarget == first && !state.Slots.ElementAt(4).Enabled,
            "Unloaded target did not release priority/cache ownership.");
        Persistence(state);
        var commands = FalloutLookCommands.Read("; ignored\nactor.Look target 0\nset counter to 1\nactor.StopLook player\nLook target\nStopLook");
        Require(commands.Count == 4 && commands[0] == new FalloutLookCommand(0, "actor", "target") &&
            commands[1] == new FalloutLookCommand(2, "actor", null) && commands[2].Actor is null && commands[3].Target is null,
            "Look source order, optional actor or zero-parameter StopLook changed.");
        foreach (var invalid in new[] { "Look", "actor.Look target 1", "if enabled\nLook target\nendif", "StopLook target 1", "Look target invalid" })
            Reject(() => FalloutLookCommands.Read(invalid));
        Console.WriteLine("Head target priority, exact cold cache/timer/revisions, atomic refusal and script command contracts passed.");
    }

    private static void Persistence(FalloutHeadTrackingState state)
    {
        static string Json(FalloutHeadTrackingTargets value) => JsonSerializer.Serialize(value);
        var saved = state.Capture();
        var restored = JsonSerializer.Deserialize<FalloutHeadTrackingTargets>(Json(saved))!;
        var cold = new FalloutHeadTrackingState(saved.SourceHoldSeconds); cold.Restore(restored);
        Require(Json(cold.Capture()) == Json(saved), "Cold head targets lost stored/disabled refs, stale cache, negative hold or revisions.");
        Reject(() => cold.Restore(restored with { Slots = restored.Slots.Take(5).ToArray() }));
        Reject(() => cold.Restore(restored with { Slots = restored.Slots.Select((slot, index) => index == 0 ? null! : slot).ToArray() }));
        Reject(() => cold.Restore(restored with { SourceHoldSeconds = saved.SourceHoldSeconds + 1 }));
        Reject(() => cold.Restore(restored with { DefaultHoldSeconds = float.NaN }));
        Reject(() => cold.Restore(restored with { TargetRevision = restored.Revision + 1 }));
        Require(Json(cold.Capture()) == Json(saved), "Rejected head restoration mutated the live target owner.");
        var copied = saved.Copy(); ((FalloutHeadTrackingSlot[])copied.Slots)[0] = new(null, false);
        Require(Json(cold.Capture()) == Json(saved), "Head snapshot retained caller-owned slot storage.");
        var pose = new FalloutHeadTrackingPose([0, .1f, 0, .9949874f], [0, 0, 0, 1], [0, .2f, 0, .9797959f],
            true, true, true, false, .07f, [2, 1, -2]);
        var poseCopy = JsonSerializer.Deserialize<FalloutHeadTrackingPose>(JsonSerializer.Serialize(pose))!;
        poseCopy.Validate();
        Require(JsonSerializer.Serialize(poseCopy) == JsonSerializer.Serialize(pose), "Raw quaternion head history changed through JSON.");
        Reject(() => (poseCopy with { Current = [0, 0, 0, 0] }).Validate());
        Reject(() => (poseCopy with { Previous = null }).Validate());
        Reject(() => (poseCopy with { Target = [float.NaN, 0, 0] }).Validate());
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
