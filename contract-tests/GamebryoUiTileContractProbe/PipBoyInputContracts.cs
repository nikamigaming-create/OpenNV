using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PipBoyInputContracts
{
    internal static void Run()
    {
        var controls = FalloutPlayerControlState.AllEnabled;
        var disabled = new FalloutPlayerControlCommand(false, [false, true]).Apply(controls);
        var enabled = new FalloutPlayerControlCommand(true, [false, true]).Apply(disabled);
        if (!disabled.Movement || disabled.PipBoy || !enabled.PipBoy)
            throw new InvalidOperationException("The actual source Pip-Boy mask changed unrelated controls.");
        static FalloutPipBoyInputBlocker Access(FalloutPlayerControlState state) =>
            FalloutPipBoyInputPolicy.Read(state, false, false, true);
        if (Access(disabled) != FalloutPipBoyInputBlocker.SourceControl || Access(enabled) != FalloutPipBoyInputBlocker.None ||
            FalloutPipBoyInputPolicy.Read(controls, true, false, true) != FalloutPipBoyInputBlocker.Furniture ||
            FalloutPipBoyInputPolicy.Read(controls, false, true, true) != FalloutPipBoyInputBlocker.Loading ||
            FalloutPipBoyInputPolicy.Read(controls, false, false, false) != FalloutPipBoyInputBlocker.Equipment)
            throw new InvalidOperationException("Pip-Boy permission lost an actual control/equipment/world consumer.");

        var requests = new List<bool>();
        var input = new FalloutPipBoyHeldInput();
        input.Observe(true, true, Access(controls), requests.Add);
        if (requests.Count != 0) throw new InvalidOperationException("New/cold attachment invented a held opening request.");
        input.Observe(true, false, Access(controls), requests.Add);
        input.Observe(true, true, Access(controls), requests.Add);
        // Actual scripts can write controls between early XR sampling and late
        // SkeletonUpdated wrist publication, while the grip remains held.
        input.Observe(true, true, Access(disabled), requests.Add);
        input.Observe(true, true, Access(enabled), requests.Add);
        if (!requests.SequenceEqual([true, false]) || !input.AwaitRelease || input.RequestedFocus)
            throw new InvalidOperationException("Late source revocation retained focus or replayed its blocked hold.");
        input.Observe(true, false, Access(enabled), requests.Add);
        input.Observe(true, true, Access(enabled), requests.Add);
        input.Observe(false, true, Access(enabled), requests.Add);
        input.Observe(true, true, Access(enabled), requests.Add);
        if (!requests.SequenceEqual([true, false, true, false]) || input.RequestedFocus)
            throw new InvalidOperationException("Tracking recovery replayed a held wrist request.");
        input.Observe(true, false, Access(enabled), requests.Add);
        input.Observe(true, true, FalloutPipBoyInputBlocker.Unbound, requests.Add);
        input.Observe(true, true, Access(enabled), requests.Add);
        if (requests.Count != 4) throw new InvalidOperationException("Missing producer input became a ready opening.");
        input.Observe(true, false, Access(enabled), requests.Add);
        input.Observe(true, true, Access(enabled), requests.Add);
        input.Retire(requests.Add); input.Retire(requests.Add);
        if (!requests.SequenceEqual([true, false, true, false, true, false]) || !input.Retired || !input.LastRequestReturned)
            throw new InvalidOperationException("Wrist request retirement did not deliver exactly one release.");

        var fault = new IOException("authored failure after an opening prefix");
        var prefix = new List<bool>();
        var failed = new FalloutPipBoyHeldInput();
        void Deliver(bool focus) { prefix.Add(focus); if (focus) throw fault; }
        failed.Observe(true, false, Access(controls), Deliver);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { failed.Observe(true, true, Access(controls), Deliver); throw new InvalidOperationException("Faulted request was accepted."); }
            catch (IOException observed) when (ReferenceEquals(observed, fault)) { }
        }
        if (!prefix.SequenceEqual([true]) || failed.Failure is null || failed.RequestOrdinal != 1 || failed.LastRequestReturned)
            throw new InvalidOperationException("Failed callback lost its committed request prefix or replayed.");
        failed.Retire(Deliver);
        if (!prefix.SequenceEqual([true, false]) || failed.Failure is null || failed.RequestOrdinal != 2 || !failed.Retired)
            throw new InvalidOperationException("Fault cleanup changed the original request failure.");

        var external = new FalloutPipBoyHeldInput();
        var externalRequests = new List<bool>();
        external.Observe(true, false, Access(disabled), externalRequests.Add, actualFocus: true);
        if (!externalRequests.SequenceEqual([false]) || external.RequestOrdinal != 1)
            throw new InvalidOperationException("A source-denied actual page request retained its independent pointer owner.");
        var closeFault = new IOException("authored close failure after a committed prefix");
        var failedClose = new FalloutPipBoyHeldInput();
        var closeCalls = 0;
        void Close(bool focus) { closeCalls++; if (focus) throw new InvalidOperationException("Denied close became an open."); throw closeFault; }
        try { failedClose.Observe(true, false, Access(disabled), Close, actualFocus: true); }
        catch (IOException observed) when (ReferenceEquals(observed, closeFault)) { }
        failedClose.Retire(Close, actualFocus: true);
        if (closeCalls != 1 || failedClose.Failure is null || !failedClose.Retired)
            throw new InvalidOperationException("A failed source-denied close was replayed during retirement.");

        Console.WriteLine("OPENNV_PIPBOY_INPUT_CONTRACTS_PASS actualControlMasks=true lateRevocation=true heldReplay=false trackingRecovery=true unboundRefusal=true callbackPrefix=true retirement=true gameplayAcceptance=false");
    }
}
