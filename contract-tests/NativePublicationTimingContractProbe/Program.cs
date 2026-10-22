using OpenNV.Runtime.Presentation.Rendering;

static void Require(bool condition, string failure)
{
    if (!condition) throw new InvalidDataException(failure);
}

var near = new NativePublicationTiming();
var lod = new NativePublicationTiming();
Require(near.Steps == 0 && near.Batches == 0 && near.MaximumStepSource is null &&
    near.MaximumStepMilliseconds == 0, "Empty timing invented an admitted source.");

// The former cumulative attribution labels B with the whole 1.9ms prefix.
near.RecordStep("A", 1.6, false);
near.RecordStep("B", .3, false);
near.RecordBatch(2.1);
Require(near.LastStepSource == "B" && near.LastStepMilliseconds == .3 &&
    near.MaximumStepSource == "A" && near.MaximumStepMilliseconds == 1.6 &&
    near.LastBatchMilliseconds == 2.1 && near.MaximumBatchMilliseconds == 2.1 &&
    near.Steps == 2 && near.Batches == 1, "Batch cost was attributed to its final source.");

near.RecordStep("C", .2, false);
near.RecordBatch(.25);
Require(near.LastBatchMilliseconds == .25 && near.MaximumBatchMilliseconds == 2.1 &&
    near.MaximumStepSource == "A" && near.MaximumStepMilliseconds == 1.6,
    "A smaller later batch reset the lifetime maximum or its source.");
near.RecordStep("tie", 1.6, false);
Require(near.MaximumStepSource == "A", "A tied step replaced the first maximum source.");

// The stream owner chooses callback/requeue behavior; recording a step must
// neither execute a callback nor retry/consume its false return.
var queue = new Queue<(string Source, Func<bool> Upload, double Elapsed)>();
var attempts = 0;
queue.Enqueue(("pending", () => { ++attempts; return false; }, .4));
var remaining = queue.Count;
var charged = 0.0;
while (remaining-- > 0 && queue.TryDequeue(out var upload))
{
    var faulted = true;
    try { if (!upload.Upload()) queue.Enqueue(upload); faulted = false; }
    finally
    {
        charged += upload.Elapsed;
        near.RecordStep(upload.Source, upload.Elapsed, faulted);
    }
}
Require(attempts == 1 && queue.Count == 1 && charged == .4 && near.LastStepMilliseconds == charged &&
    near.FaultedSteps == 0, "Timing changed a pending callback's requeue or charged a different duration.");

var beforeFault = near.Steps;
var completions = 0;
try
{
    var faulted = true;
    try { throw new NotSupportedException("source fixture failure"); }
    finally
    {
        ++completions;
        charged += 3;
        near.RecordStep("fault", 3, faulted);
    }
}
catch (NotSupportedException error) when (error.Message == "source fixture failure") { }
Require(completions == 1 && near.Steps == beforeFault + 1 && near.FaultedSteps == 1 &&
    near.MaximumStepSource == "fault" && near.MaximumStepMilliseconds == 3,
    "A fault was lost, counted twice or replaced the source failure.");

// Empty/denied batches have wall time but no new source step.
var beforeDenied = near.Steps;
near.RecordBatch(.01);
Require(near.Steps == beforeDenied && near.LastStepSource == "fault" && near.FaultedSteps == 1 &&
    near.LastBatchMilliseconds == .01, "Denied admission invented a source measurement.");
lod.RecordStep("LOD:tile", 7, false); lod.RecordBatch(7.2);
Require(lod.Steps == 1 && lod.MaximumStepSource == "LOD:tile" && lod.MaximumStepMilliseconds == 7 &&
    near.MaximumStepSource == "fault" && near.MaximumStepMilliseconds == 3 &&
    near.MaximumBatchMilliseconds == 2.1, "Near and LOD accumulators contaminated each other.");
Console.WriteLine("Native publication timing: source maxima, batch wall time, retained history, false/requeue, fault once, empty/denied and independent streams PASS.");
