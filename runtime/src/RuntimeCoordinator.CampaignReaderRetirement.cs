using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private Exception? _nativeCampaignReaderDrainFailure;

    private async Task DrainActualCampaignReadersAndMainDelivery()
    {
        GetTree().Paused = true;
        var failures = new List<Exception>(); var pending = new List<Task>();
        void Request(Action action)
        {
            try { action(); } catch (Exception error) { failures.Add(error); }
        }
        Request(RequestNativeMainPlayerRetirement);
        Request(() => pending.AddRange(StopActualQueuedSourceReads()));
        if (_nativeOpeningStageDriver is { } actualDriver)
            Request(() => pending.Add(actualDriver.StopAndDrainSourceMainScriptCaller()));
        Request(() => pending.AddRange(RequestNativeQueuedActorCallerRetirement()));
        pending.AddRange(_nativeGridNpcPreparations.Select(item => item.ReadTask));
        if (_nativeMenuRead is { } menuRead) pending.Add(menuRead);
        if (_nativePlayerMoveRead is { } moveRead) pending.Add(moveRead);
        if (_nativeDoorRead is { } doorRead) pending.Add(doorRead);
        if (_nativeGridRead is { } gridRead) pending.Add(gridRead);
        Request(() =>
        {
            foreach (var lod in FindChildren("*", "", true, false).OfType<RuntimeNativeExteriorLod>())
                Request(() => pending.Add(lod.StopSourceReads()));
        });
        Request(CancelNativeGridRead);
        var all = Task.WhenAll(pending);
        try { await all; }
        catch (Exception readError)
        {
            // All submitted work has actually returned/unwound. Retain its
            // original aggregate (including sibling errors), without promoting
            // failure/cancellation into source completion or capture allowance.
            var original = (Exception?)all.Exception ?? readError;
            _nativeCampaignReaderDrainFailure = _nativeCampaignReaderDrainFailure is null ? original :
                new AggregateException("Source read drain retains earlier and current results.", _nativeCampaignReaderDrainFailure, original);
            GD.Print("OPENNV_SESSION_OLD_READ_FINISHED " + _nativeCampaignReaderDrainFailure);
        }
        Request(ReapExteriorQueuedNpcRetirements);
        // Transferred bodies belong to their actual CELL root. The source read
        // registry cannot retire ahead of those native borrowers.
        if (_nativeQueuedActorCallers.Count == 0) Request(RetireReturnedActualQueuedSourceReads);
        // A stopped callback remains a living binding. This is independent of
        // platform presence and of a preceding unrelated retirement failure.
        Request(RetireNativeMainDelivery);
        if (failures.Count != 0)
        {
            var failure = new AggregateException("Campaign read/Main retirement retained independent owners and failures.", failures);
            _nativeCampaignReaderDrainFailure = _nativeCampaignReaderDrainFailure is null ? failure :
                new AggregateException("Campaign read/Main retirement retains its earlier results.", _nativeCampaignReaderDrainFailure, failure);
            throw failure;
        }
    }
}
