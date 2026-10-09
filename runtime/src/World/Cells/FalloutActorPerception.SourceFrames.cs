using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPerceptionSourceFrame(string Owner, float Seconds,
    IReadOnlyList<FalloutPerceptionSourcePair> Pairs, IReadOnlyList<FalloutFormKey> CommitReceivers,
    IReadOnlyList<FalloutFormKey> LightReceivers, IReadOnlyList<FalloutPerceptionProcessRequest> ProcessRequests,
    string? Failure = null);
internal sealed record FalloutPerceptionSourcePair(FalloutFormKey Receiver, FalloutFormKey Target,
    long ReceiverProcessEpoch, long TargetProcessEpoch, bool CallerStagesNonplayer);
internal sealed record FalloutPerceptionProcessRequest(FalloutFormKey Actor, long ExpectedEpoch,
    FalloutDetectionProcessPresence? Presence, string Owner, bool CopiesDetection);
internal sealed record FalloutPerceptionSourceFrameReceipt(long Revision, float Seconds, string Owner,
    int CompletedPairs, int CompletedCommits, int CompletedLights, bool Complete);

// This receipt is issued by the original process manager/cohort scheduler. A
// caller cannot construct it from renderer residency, nearest candidates, or
// an arbitrary cadence. Operation order is retained when a later input fails.
internal sealed partial class FalloutActorPerception
{
    private FalloutPerceptionSourceFrameReceipt? _lastSourceFrame;
    internal FalloutPerceptionSourceFrameReceipt? LastSourceFrame => _lastSourceFrame;

    internal void ExecuteSourceFrame(FalloutPerceptionSourceFrame frame)
    {
        RequireStable(); RequireIdle();
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(frame.Owner) || !float.IsFinite(frame.Seconds) || frame.Seconds < 0 ||
            frame.Pairs is null || frame.CommitReceivers is null || frame.LightReceivers is null || frame.ProcessRequests is null)
            throw new InvalidDataException("Detection frame has no complete original scheduler receipt.");
        if (frame.Failure is { } missing)
        {
            RetainSourceBoundary(null, null, frame.Owner, missing);
            throw new NotSupportedException(missing);
        }
        var pairs = 0; var commits = 0; var lights = 0;
        _lastSourceFrame = new(Next(), _seconds, frame.Owner, 0, 0, 0, false);
        try
        {
            foreach (var request in frame.ProcessRequests)
                RequestProcess(request.Actor, request.ExpectedEpoch, request.Presence, request.Owner, request.CopiesDetection);
            foreach (var pair in frame.Pairs)
            {
                if (ProcessEpoch(pair.Receiver) != pair.ReceiverProcessEpoch || ProcessEpoch(pair.Target) != pair.TargetProcessEpoch)
                    throw new InvalidDataException("Original source frame contains a retired process pair.");
                _ = ComputeSourcePair(pair.Receiver, pair.Target, pair.CallerStagesNonplayer);
                _lastSourceFrame = _lastSourceFrame with { CompletedPairs = ++pairs };
            }
            foreach (var actor in frame.CommitReceivers)
            {
                CommitSourceCache(actor);
                _lastSourceFrame = _lastSourceFrame with { CompletedCommits = ++commits };
            }
            foreach (var actor in frame.LightReceivers)
            {
                AdvanceSourceLight(actor, frame.Seconds);
                _lastSourceFrame = _lastSourceFrame with { CompletedLights = ++lights };
            }
            _lastSourceFrame = _lastSourceFrame with { Complete = true };
        }
        catch (Exception error)
        {
            RetainSourceBoundary(null, null, frame.Owner,
                string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message);
            throw;
        }
    }
}
