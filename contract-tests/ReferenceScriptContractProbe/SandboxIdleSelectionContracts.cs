using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class SandboxIdleSelectionContracts
{
    internal static void Run()
    {
        FalloutFormKey Key(uint id) => new("SandboxSelection.esm", id);
        var first = Key(1); var second = Key(2); var third = Key(3); var fourth = Key(4);
        var owner = new FalloutSandboxIdleSelection();
        var randomCalls = 0; var playbackCalls = 0;
        uint Random() { ++randomCalls; return 7; }
        void Played(FalloutFormKey idle)
        {
            ++playbackCalls;
            Require(owner.SelectedIdle == idle && owner.Cursor >= 0,
                "Playback preceded publication of the selected process IDLE/cursor.");
        }
        Require(owner.Cursor == -1 && owner.SelectedIdle is null, "Fresh process invented a selected idle.");
        Require(!owner.TrySelect(1, [], Random, Played) && owner.Cursor == -1 && owner.SelectedIdle is null &&
            randomCalls == 0 && playbackCalls == 0, "Empty initial eligibility selected or drew a random value.");
        Require(owner.TrySelect(1, [first, second, third], Random, Played) && owner.Cursor == 0 && owner.SelectedIdle == first,
            "Initial sequential Sandbox selection did not choose zero.");
        Require(owner.TrySelect(1, [first, second, third], Random, Played) && owner.Cursor == 1 && owner.SelectedIdle == second,
            "Sequential Sandbox selection did not advance its numeric process cursor.");
        Require(owner.TrySelect(1, [fourth, third, second, first], Random, Played) && owner.Cursor == 2 && owner.SelectedIdle == second,
            "A rebuilt filtered list followed old IDLE identity instead of numeric cursor.");
        Require(owner.TrySelect(1, [first, fourth], Random, Played) && owner.Cursor == 0 && owner.SelectedIdle == first,
            "A shorter fresh list failed to wrap the retained numeric cursor.");
        var retained = owner.Capture(); var playsBeforeEmpty = playbackCalls;
        Require(!owner.TrySelect(0, [], Random, Played) && owner.Capture() == retained && randomCalls == 0 &&
            playbackCalls == playsBeforeEmpty, "Temporary empty eligibility cleared the process selection or advanced playback/RNG.");
        Require(owner.TrySelect(5, [first, second], Random, Played) && owner.Cursor == 1 && owner.SelectedIdle == second &&
            owner.TrySelect(5, [first, second], Random, Played) && owner.Cursor == 0 && owner.SelectedIdle == first && randomCalls == 0,
            "IDLF DoOnce created completion/wait policy in the Sandbox sequential dispatcher.");
        Require(owner.TrySelect(4, [fourth], Random, Played) && owner.SelectedIdle == fourth && owner.Cursor == 0 && randomCalls == 1,
            "Random singleton Sandbox selection skipped its required draw or acquired DoOnce behavior.");
        Require(owner.TrySelect(0, [first, second, third], Random, Played) && owner.Cursor == 1 && owner.SelectedIdle == second && randomCalls == 2,
            "Random Sandbox selection did not use the raw uint draw modulo fresh eligible count.");
        var snapshot = JsonSerializer.Deserialize<FalloutSandboxIdleSelectionSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutSandboxIdleSelection(); cold.Restore(snapshot);
        Require(cold.Capture() == owner.Capture(), "Cold process selection changed the cursor or selected IDLE.");
        Require(cold.TrySelect(1, [fourth, third, first], () => throw new InvalidDataException("Unexpected sequential draw."), _ => { }) &&
            cold.Cursor == 2 && cold.SelectedIdle == first, "Cold selection restarted or rebound the cursor to a marker/list identity.");
        var largeCursor = new FalloutSandboxIdleSelection();
        largeCursor.Restore(new(int.MaxValue, fourth));
        Require(largeCursor.TrySelect(1, [first, second], Random, _ => { }) && largeCursor.Cursor == 0 && randomCalls == 2,
            "A process cursor beyond the fresh list overflowed or failed to wrap.");
        const string refusal = "Source playback fixture refused.";
        try
        {
            _ = owner.TrySelect(0, [third, fourth], Random, idle =>
            {
                Require(idle == fourth && owner.SelectedIdle == fourth && owner.Cursor == 1 && randomCalls == 3,
                    "Playback refusal observed an uncommitted selection.");
                throw new NotSupportedException(refusal);
            });
            throw new InvalidDataException("Sandbox playback refusal was swallowed.");
        }
        catch (NotSupportedException error) when (error.Message == refusal) { }
        Require(owner.Cursor == 1 && owner.SelectedIdle == fourth && randomCalls == 3,
            "Playback refusal rolled back the source process selection or replayed its RNG.");
        foreach (var invalid in new FalloutSandboxIdleSelectionSnapshot[]
        {
            new(-2, null), new(-1, first), new(0, null),
            new(0, new FalloutFormKey("", 1)), new(0, Key(0)), new(0, Key(0x1000000))
        }) Reject(() => new FalloutSandboxIdleSelection().Restore(invalid));
        var empty = new FalloutSandboxIdleSelection(); empty.Restore(new(-1, null));
        Require(empty.Capture() == new FalloutSandboxIdleSelectionSnapshot(-1, null), "Initial snapshot failed its exact empty relation.");
        Console.WriteLine("OPENNV_SANDBOX_IDLE_SELECTION_CONTRACT_PASS processCursor=true freshListWrap=true singletonRandomDraw=true " +
            "emptyRetains=true noPatrolWaitOrDoOnce=true commitBeforePlayback=true refusalRetains=true coldExact=true gameplay=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void Reject(Action request)
    {
        try { request(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid Sandbox process selection snapshot was admitted.");
    }
}
