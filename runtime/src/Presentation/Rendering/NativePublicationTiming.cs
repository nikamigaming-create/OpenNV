namespace OpenNV.Runtime.Presentation.Rendering;

// One accumulator belongs to one publication stream. Step measurements are the
// same elapsed values charged to shared admission; batch wall time also includes
// queue/admission overhead and must never be attributed to its last source.
internal sealed class NativePublicationTiming
{
    internal double LastStepMilliseconds { get; private set; }
    internal double MaximumStepMilliseconds { get; private set; }
    internal string? LastStepSource { get; private set; }
    internal string? MaximumStepSource { get; private set; }
    internal long Steps { get; private set; }
    internal long FaultedSteps { get; private set; }
    internal double LastBatchMilliseconds { get; private set; }
    internal double MaximumBatchMilliseconds { get; private set; }
    internal long Batches { get; private set; }

    internal void RecordStep(string source, double milliseconds, bool faulted)
    {
        LastStepMilliseconds = milliseconds;
        LastStepSource = source;
        ++Steps;
        if (faulted) ++FaultedSteps;
        if (MaximumStepSource is null || milliseconds > MaximumStepMilliseconds)
        {
            MaximumStepMilliseconds = milliseconds;
            MaximumStepSource = source;
        }
    }

    internal void RecordBatch(double milliseconds)
    {
        LastBatchMilliseconds = milliseconds;
        MaximumBatchMilliseconds = Math.Max(MaximumBatchMilliseconds, milliseconds);
        ++Batches;
    }
}
