using OpenNV.Runtime.Content;

internal static class HeadlessInputContracts
{
    internal static void Run()
    {
        var controls = new FalloutInputControls(_ => null);
        var state = FalloutDirectInputState.CreateHeadless(new('2', 64), controls);
        var commands = new FalloutHeadlessInput(state);
        Require(state.Provider == FalloutInputProvider.HeadlessCommands && state.Window == 0 && state.Sample == 0,
            "Headless construction fabricated an acquired device/window.");
        Reject<InvalidDataException>(() => new FalloutDirectInputState(state.Source, 0, controls));
        Reject<InvalidDataException>(() => new FalloutHeadlessInput(new(state.Source, 123, controls)));
        Reject<InvalidDataException>(() => FalloutDirectInputState.CreateHeadless("foreign", controls));
        var edges = new List<(int Key, bool Down)>();
        state.GameEdges += changes => edges.AddRange(changes);
        commands.Publish();
        Require(state.Capture().Keys.All(key => !key.Raw && !key.Game), "Headless admission invented a held command.");
        commands.Key(17, true); commands.Key(256, true); commands.Mouse(7, -8, 120); commands.Publish();
        var first = state.Capture();
        Require(first.Provider == FalloutInputProvider.HeadlessCommands && first.Window == 0 && first.Acquired &&
            state.Pressed(17) && state.Pressed(256) && state.Pressed(264) && edges.Contains((17, true)) &&
            first.MouseX == 7 && first.MouseY == -8 && first.MouseWheel == 120,
            "Headless commands missed the shared script/native input owner.");
        commands.Publish();
        Require(state.Pressed(17) && !state.Pressed(264) && state.Capture().MouseX == 0 && edges.Count(value => value == (17, true)) == 1,
            "A held headless key repeated its edge or relative mouse input replayed.");
        state.SetDisable(17, true, 1); commands.Publish();
        Require(!state.Pressed(17) && state.Pressed(17, 2), "Headless input bypassed the existing user-disabled control lane.");
        state.Tap(17); commands.Publish();
        Require(state.Pressed(17, 4), "Source inserted input no longer reaches headless gameplay.");
        commands.Publish(); Require(!state.Pressed(17), "A one-sample inserted tap replayed.");
        commands.Key(17, false); commands.Key(256, false);
        Reject<NotSupportedException>(() => commands.Key(264, true));
        commands.Mouse(int.MaxValue, 3);
        Reject<OverflowException>(() => commands.Mouse(1, 7));
        commands.Publish();
        Require(state.Capture().MouseX == int.MaxValue && state.Capture().MouseY == 3 && !state.Pressed(256),
            "A rejected mouse accumulation changed another axis or left its held key.");
        void Fail(IReadOnlyList<(int Key, bool Down)> _) => throw new NotSupportedException("authored-script-callback-failure");
        state.GameEdges += Fail; commands.Mouse(19, 20); commands.Key(18, true);
        var beforeFailure = state.Sample;
        Reject<NotSupportedException>(commands.Publish);
        state.GameEdges -= Fail;
        Require(state.Sample == beforeFailure + 1 && state.Pressed(18) && state.Capture().MouseX == 19,
            "A reached script failure rolled back the committed headless sample.");
        commands.Publish(); Require(state.Capture().MouseX == 0, "A failed script callback replayed mouse motion.");
        Exception? foreignThread = null;
        var thread = new Thread(() => { try { commands.Key(19, true); } catch (Exception error) { foreignThread = error; } });
        thread.Start(); thread.Join(); Require(foreignThread is InvalidOperationException, "Headless input escaped its gameplay thread.");
        using (state.RetainNativeReader()) Reject<InvalidOperationException>(state.Retire);
        state.Retire(); Reject<ObjectDisposedException>(commands.Publish);
        Console.WriteLine("OPENNV_HEADLESS_INPUT_CONTRACT_PASS sharedControls=true explicitProvider=true window=none callbacks=true retirement=true gameplay=UNEXECUTED");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected refusal: " + typeof(T).Name);
    }
}
