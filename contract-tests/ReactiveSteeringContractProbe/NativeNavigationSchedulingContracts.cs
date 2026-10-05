using System.Text.Json;
using OpenNV.Runtime.World.Cells;

internal static class NativeNavigationSchedulingContracts
{
    internal static void Run()
    {
        OverBudgetFirstCaller();
        ProcessAndPhysicsPhases();
        AbsentAndCancelledOwners();
        CursorLifetime();
        ThreadAndReentry();
        Console.WriteLine("Native navigation scheduling: over-budget caller fairness, shared bounds, process/physics phases, absent/cancelled owners, terminal/fault cleanup, exact cursor progress and thread ownership PASS.");
    }

    private static void OverBudgetFirstCaller()
    {
        var schedule = new NativeNavigationWorkSchedule();
        var searches = Enumerable.Range(0, 3).Select(_ => schedule.Register()).ToArray();
        var winners = new List<int>();
        for (ulong phase = 0; phase < 12; phase++)
        {
            var granted = 0;
            for (var index = 0; index < searches.Length; index++)
            {
                if (!schedule.TryBegin(searches[index], phase, phase)) continue;
                granted++; winners.Add(index);
                schedule.Finish(searches[index], 2.4);
            }
            Require(granted == 1 && schedule.UsedMilliseconds == 2.4,
                "An indivisible over-budget query received additional work in the same physics frame.");
        }
        Require(winners.SequenceEqual(Enumerable.Range(0, 12).Select(index => index % 3)) &&
            searches.All(search => search.Snapshot.Grants == 4) && searches[0].Snapshot.DeniedTurn > 0 &&
            searches[2].Snapshot.DeniedBudget > 0, "Early callback order starved a registered late caller.");
        foreach (var search in searches) search.Retire("cancelled");
        Require(schedule.Count == 0, "Completed scheduling fixture retained registrations.");
    }

    private static void ProcessAndPhysicsPhases()
    {
        var schedule = new NativeNavigationWorkSchedule();
        var actor = schedule.Register(); var bot = schedule.Register();
        Require(schedule.TryBegin(actor, 10, 100), "First actor did not get a slice."); schedule.Finish(actor, 2.1);
        Require(!schedule.TryBegin(bot, 10, 100), "Bot exceeded the already consumed budget.");
        Require(!schedule.TryBegin(actor, 11, 100) && !schedule.TryBegin(actor, 12, 100),
            "Extra physics ticks stole a late Process caller's reserved turn.");
        Require(schedule.TryBegin(bot, 12, 100), "Late Process caller never received its reserved turn.");
        schedule.Finish(bot, .5);
        Require(schedule.TryBegin(actor, 12, 100), "Remaining budget was not available to the next owner.");
        schedule.Finish(actor, .75);
        Require(schedule.UsedMilliseconds == 1.25 && schedule.Count == 2, "Sharing a frame changed the global budget or retired a live cursor.");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(bot.Snapshot));
        Require(json.RootElement.GetProperty("DeniedBudget").GetInt64() == 1 &&
            json.RootElement.GetProperty("Grants").GetInt64() == 1,
            "Read-only telemetry hid actual budget denial or progress.");
        actor.Retire("cancelled"); bot.Retire("cancelled");
    }

    private static void AbsentAndCancelledOwners()
    {
        var schedule = new NativeNavigationWorkSchedule();
        var active = schedule.Register(); var absent = schedule.Register();
        Require(schedule.TryBegin(active, 1, 1), "Initial active query was refused."); schedule.Finish(active, 2.1);
        Require(!schedule.TryBegin(absent, 1, 1), "Absent query bypassed the budget.");
        Require(!schedule.TryBegin(active, 2, 2), "A reserved query was skipped before a whole process phase elapsed.");
        Require(schedule.TryBegin(active, 3, 3), "An absent head permanently stalled active queries."); schedule.Finish(active, .5);
        Require(absent.Snapshot.SkippedInactiveTurns == 1 && absent.Snapshot.Grants == 0,
            "Skipping an inactive owner ran or cancelled its query.");
        Require(schedule.TryBegin(absent, 3, 3), "An absent owner could not resume its original cursor."); schedule.Finish(absent, .5);
        Require(schedule.TryBegin(active, 3, 3), "A valid next owner lost the remaining budget."); schedule.Finish(active, 2.1);
        absent.Retire("cancelled");
        Require(schedule.TryBegin(active, 4, 4), "Cancellation left a stale head blocking the queue."); schedule.Finish(active, .5);
        active.Retire("cancelled");
        var unused = schedule.Register(); var used = schedule.Register();
        Require(schedule.TryBegin(used, 5, 5) && unused.Snapshot.SkippedInactiveTurns == 1,
            "An unrequested synchronous cursor blocked asynchronous work."); schedule.Finish(used, .5);
        unused.Retire("cancelled"); used.Retire("cancelled");
    }

    private static void CursorLifetime()
    {
        var schedule = new NativeNavigationWorkSchedule();
        var steps = 0; var disposals = 0;
        IEnumerable<string?> Route(NativeNavigationWorkSchedule.Registration work)
        {
            try
            {
                steps++; work.GuidedSamples++; yield return null;
                steps++; work.ObserveNode(4); yield return null;
                steps++; work.SmoothingSteps++; yield return "checked-route";
            }
            finally { disposals++; }
        }
        using (var search = new NativeNavigationScheduledSearch<string?>(schedule, work => Route(work).GetEnumerator(), () => true, value => value is not null))
        {
            Require(search.TryBegin(1, 1), "Scheduled cursor did not acquire work.");
            Require(search.MoveNext() && search.Current is null && search.MoveNext() && search.Current is null &&
                search.MoveNext() && search.Current == "checked-route", "Scheduling replaced a cursor or lost its terminal result.");
            search.Finish(.7);
            Require(search.State.State == "completed" && schedule.Count == 0 && disposals == 1 &&
                search.State.IteratorSteps == 3 && search.State.ExpandedNodes == 1 && search.State.GuidedSamples == 1 &&
                search.State.SmoothingSteps == 1 && search.State.NearestTargetDistance == 4,
                "Completion lost cleanup or confused iterator yields with expanded collision nodes.");
        }
        Require(steps == 3 && disposals == 1, "Completed cursor was replayed or disposed twice.");
        Action? retire = null; var unbound = 0; var reads = 0;
        using (var search = new NativeNavigationScheduledSearch<string?>(schedule, work => Route(work).GetEnumerator(),
            () => { reads++; return true; }, value => value is not null,
            callback => { retire = callback; return () => unbound++; }))
        {
            Require(search.MoveNext(), "Synchronous query could not use its owned cursor.");
            retire!();
            var previousReads = reads; var previousSteps = steps;
            Refused(() => search.MoveNext());
            Require(schedule.Count == 0 && search.State.State == "owner-retired" && reads == previousReads &&
                steps == previousSteps && unbound == 1, "Owner retirement reread disposed state or retained work.");
        }
        IEnumerable<string?> Broken()
        {
            yield return null;
            throw new InvalidOperationException("actual-query-refusal");
        }
        using (var search = new NativeNavigationScheduledSearch<string?>(schedule, _ => Broken().GetEnumerator(), () => true, value => value is not null))
        {
            Require(search.TryBegin(2, 2) && search.MoveNext(), "Fault fixture could not start.");
            Refused(() => search.MoveNext()); search.Finish(.5);
            Require(search.State.State == "faulted" && schedule.Count == 0 && search.State.IteratorSteps == 2,
                "A native query failure retained a scheduler lease or replayed its prefix.");
        }
        using (var search = new NativeNavigationScheduledSearch<string?>(schedule, _ => Broken().GetEnumerator(), () => false, value => value is not null))
        {
            Refused(() => search.TryBegin(3, 3));
            Require(search.State.State == "owner-retired" && search.State.IteratorSteps == 0 && schedule.Count == 0,
                "Invalid source/native ownership was treated as a route.");
        }
    }

    private static void ThreadAndReentry()
    {
        var schedule = new NativeNavigationWorkSchedule();
        var first = schedule.Register(); var second = schedule.Register();
        Require(schedule.TryBegin(first, 1, 1), "Reentry fixture did not start.");
        Refused(() => schedule.TryBegin(second, 1, 1));
        Refused(() => schedule.RequireQuery(second));
        Require(Task.Run(() =>
        {
            try { schedule.Register(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult(), "Native work was admitted on another thread.");
        schedule.Finish(first, .5); first.Retire("cancelled"); second.Retire("cancelled");
    }

    private static void Refused(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid navigation scheduling operation was accepted.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
