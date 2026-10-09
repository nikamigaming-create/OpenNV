using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunSandboxRuntimeProducers()
    {
        var receipt = SandboxRuntimeReceipt("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57");
        var timerSource = FalloutSourceFrameTimer.Read(receipt);
        var counter = new AuthoredSandboxTickCounter { Now = 1000 };
        using (var timer = new FalloutSourceFrameTimerState(timerSource, counter, Guid.NewGuid()))
        {
            timer.Update();
            var first = timer.Capture();
            Require(first.CachedMilliseconds == 0 && first.ZeroElapsed && counter.Waits.SequenceEqual(new uint[] { 9 }) &&
                BitConverter.SingleToInt32Bits(first.UnscaledSeconds) == 0x3c23d70b,
                "Source minimum wait changed cached raw time or the independently authored Float32 delta.");
            counter.Now = 2000; timer.Update();
            Require(timer.ReadCached().Milliseconds == 1000 && counter.Waits.Count == 1,
                "Source maximum-delta cap incorrectly capped the independent cached UInt32 clock.");
            timer.SetFixedMilliseconds(.25f); timer.Update(); timer.Update();
            var half = timer.Capture();
            Require(half.CachedMilliseconds == 1000 && half.FractionalMilliseconds == .5f,
                "Fixed timer consumed whole milliseconds before their source fraction crossed one.");
            timer.SetFixedMilliseconds(2.75f); timer.Update();
            var saved = timer.Capture();
            Require(saved.CachedMilliseconds == 1003 && saved.FractionalMilliseconds == .25f,
                "Source fixed fraction did not retain its truncation remainder.");
            var coldCounter = new AuthoredSandboxTickCounter { Now = 50000 };
            using var cold = new FalloutSourceFrameTimerState(timerSource, coldCounter, Guid.NewGuid(), saved);
            var rebound = cold.Capture();
            Require(rebound.CachedMilliseconds == saved.CachedMilliseconds && rebound.Epoch == saved.Epoch &&
                rebound.Sequence == saved.Sequence && rebound.FractionalMilliseconds == saved.FractionalMilliseconds && coldCounter.Waits.Count == 0,
                "Cold timer replayed a writer, relabelled the retained epoch or advanced by load wall time.");
            counter.Now = 2010; timer.Pause(); timer.Pause();
            var paused = timer.Capture(); timer.Update();
            Require(timer.Capture().CachedMilliseconds == paused.CachedMilliseconds && timer.ScaledSeconds == 0,
                "Nested paused update advanced cached source time.");
            counter.Now = 2020; timer.Resume();
            Require(timer.Capture().PauseCount == 1, "Inner source resume consumed the outer pause.");
            timer.Resume();
            Require(timer.Capture().PauseCount == 0, "Outer source resume did not return its actual byte owner.");
        }
        var failingCounter = new AuthoredSandboxTickCounter { Now = 5, FailWait = true };
        using (var failed = new FalloutSourceFrameTimerState(timerSource, failingCounter, Guid.NewGuid()))
        {
            try { failed.Update(); throw new Exception("Failed original wait was accepted."); }
            catch (IOException) { }
            Require(failed.SaveBlocker == "source-cached-timer-writer-failed" &&
                JsonSerializer.Serialize(failed.State).Contains("cached-timer-update", StringComparison.Ordinal),
                "A failed wait lost its real entered writer prefix.");
            try { failed.Update(); throw new Exception("Failed timer writer replayed."); }
            catch (InvalidOperationException) { }
        }

        var deadline = FalloutSandboxActionDeadline.Read(receipt);
        Require(deadline.Elapsed(10.5f, 10.75f) == 15 && deadline.Elapsed(10.5f, 11.5f) == 61,
            "Sandbox source hour comparator was replaced with seconds or a normalized calendar difference.");
        var foreign = SandboxRuntimeReceipt("c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e");
        try { _ = FalloutSourceFrameTimer.Read(foreign); throw new Exception("Unowned FO3 clock became the selected FNV clock."); }
        catch (NotSupportedException) { }
        try { _ = FalloutSandboxActionDeadline.Read(foreign); throw new Exception("Unowned FO3 deadline became an admitted source comparator."); }
        catch (NotSupportedException) { }

        var linksSource = FalloutSourceCellReferenceLinksDeclaration.Read(receipt);
        var cellA = Key(0x800); var cellB = Key(0x801);
        var sourceA = new FalloutCellProcessIdentity(cellA, 0, new('1', 64), null, null);
        var sourceB = new FalloutCellProcessIdentity(cellB, 0, new('2', 64), null, null);
        var a = new FalloutCellProcessReference(Key(0xa00), cellA, "REFR", 0, new('3', 64), Key(0x80), new('4', 64));
        var b = new FalloutCellProcessReference(Key(0xa01), cellA, "REFR", 0, new('5', 64), Key(0x81), new('6', 64));
        FalloutCellProcessData Cell(FalloutFormKey key) => key == cellA
            ? new(sourceA, [a, b], new('7', 64)) : new(sourceB, [], new('8', 64));
        FalloutCellProcessReference Reference(FalloutFormKey key) => key == a.Reference ? a : b;
        var ingestion = new AuthoredSandboxCellIngestion(receipt.EngineSha256, [a.Reference, b.Reference]);
        using (var links = new FalloutSourceCellReferenceLinks(linksSource, "authored-links", Cell, Reference, ingestion))
        {
            Require(links.Read(cellA).Members.Select(member => member.Source.Reference).SequenceEqual([b.Reference, a.Reference]),
                "CELL loader insertions became sorted/final graph order instead of source head insertion.");
            var published = false;
            links.Insert(a.Reference, cellA, cellB, () => published = true, "authored-actual-ParentCELL-setter");
            Require(published && links.Read(cellA).Members.Single().Source.Reference == b.Reference &&
                links.Read(cellB).Members.Single().Source == a,
                "Ordinary transfer lost old unlink, immutable ancestry or destination head publication.");
            var saved = links.Capture(); var reads = ingestion.Reads;
            using var cold = new FalloutSourceCellReferenceLinks(linksSource, "authored-links", Cell, Reference, ingestion, saved);
            Require(cold.Read(cellA).Members.SequenceEqual(links.Read(cellA).Members) &&
                cold.Read(cellB).Members.SequenceEqual(links.Read(cellB).Members) && ingestion.Reads == reads &&
                cold.Capture().Membership == saved.Membership,
                "Cold source links replayed loader insertion, changed membership or sorted their retained order.");
        }
        using (var missing = new FalloutSourceCellReferenceLinks(linksSource, "authored-links", Cell, Reference))
        {
            try { _ = missing.Read(cellA); throw new Exception("A winning graph fabricated initial insertion order."); }
            catch (NotSupportedException) { }
            Require(missing.SaveBlocker is not null, "Missing CELL ingestion was not retained as a current failure.");
        }
        using (var failed = new FalloutSourceCellReferenceLinks(linksSource, "authored-links", Cell, Reference, ingestion))
        {
            _ = failed.Read(cellA);
            try { failed.Insert(a.Reference, cellA, cellB, () => throw new IOException("authored-parent-publication"), "authored-source-parent"); }
            catch (IOException) { }
            using var state = JsonDocument.Parse(JsonSerializer.Serialize(failed.State));
            var attempt = state.RootElement.GetProperty("attempts").EnumerateArray().Single(value =>
                value.GetProperty("Operation").GetString() == "ordinary-CELL-insertion");
            Require(attempt.GetProperty("FailedAtPhase").GetInt32() == (int)FalloutSourceCellLinkPhase.HeadInserted &&
                failed.SaveBlocker is not null && state.RootElement.GetProperty("cells").EnumerateArray().Single(value =>
                    value.GetProperty("Source").GetProperty("Cell").GetProperty("ObjectId").GetUInt32() == cellB.ObjectId)
                    .GetProperty("Members").GetArrayLength() == 1,
                "Failed ParentCELL publication rolled back or discarded its genuinely inserted head prefix.");
            try { _ = failed.Read(cellB); throw new Exception("Failed linked writer replayed."); }
            catch (NotSupportedException) { }
        }
        Console.WriteLine("OPENNV_SANDBOX_RUNTIME_PRODUCERS_PASS rawCachedClock=true fixedFraction=true failedWriterPrefix=true sourceHourDeadline=true headInsertion=true coldNoReplay=true missingIngestionRefused=true nativeCold=UNEXECUTED");
    }

    private static FalloutAdvancementRuntimeReceipt SandboxRuntimeReceipt(string engine) => new(engine,
        new('a', 64), new('b', 64), new('c', 64),
        new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
        new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk"));

    private sealed class AuthoredSandboxTickCounter : IFalloutSourceTickCounter
    {
        internal uint Now { get; set; }
        internal bool FailWait { get; set; }
        internal List<uint> Waits { get; } = [];
        public string Owner => "independently-authored-raw-tick-and-wait";
        public uint Read() => Now;
        public void Wait(uint milliseconds)
        {
            Waits.Add(milliseconds);
            if (FailWait) throw new IOException("authored-native-wait-failure");
        }
    }
    private sealed class AuthoredSandboxCellIngestion(string engine, IReadOnlyList<FalloutFormKey> order)
        : IFalloutSourceCellReferenceIngestion
    {
        public string EngineSha256 => engine;
        public string Owner => "independently-authored-entered-loader-sequence";
        internal int Reads { get; private set; }
        public IEnumerable<FalloutFormKey> ReadEnteredInsertions(FalloutCellProcessData cell)
        { ++Reads; return cell.References.Count == 0 ? [] : order; }
    }
}
