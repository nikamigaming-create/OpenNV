namespace OpenNV.Runtime.Content;

// A null ancillary word denotes storage for which the selected constructor
// and actual writers have not supplied a value. It is never a numeric zero.
// The renderer can skip that field only through the real strength predicate;
// a requested effect must reject any still undefined input it consumes.
internal sealed record FalloutSourceImageSpaceRadialOutput(uint Strength, uint[] Parameters, uint[] Center);
internal sealed record FalloutSourceImageSpaceOptionOutput(uint Strength, uint First, uint Second, uint Third,
    uint Mode, bool Alternate);
internal sealed record FalloutSourceImageSpaceDofOutput(uint Strength, uint? First, uint? Second,
    uint? FocusDistance, uint? FocusRange, uint? Mode);
internal sealed record FalloutSourceImageSpaceOutput(Guid Invocation, Guid Process, long Mutation, uint Delta,
    FalloutSourceImageSpaceProfile Profile, uint Blur, uint DoubleVision,
    FalloutSourceImageSpaceRadialOutput Radial, FalloutSourceImageSpaceOptionOutput Options,
    FalloutSourceImageSpaceDofOutput Depth, uint Scalar);

// The same real RetailHdrCompositorEffect which receives the output owns its
// constructor fields. Calls are synchronous on the actual Main/native thread.
// Publication, attachment, returned children and independent retirement are
// separate boundaries. The authority never fabricates a native factory ID.
internal interface IFalloutSourceImageSpaceNative
{
    Guid NativeIdentity { get; }
    Guid Process { get; }
    string SourceIdentity { get; }
    void RequireCurrentFactory();
    IDisposable EnterSourceImageContext();
    void ResetOutputScalars();
    void PublishEffects(FalloutSourceImageSpaceOutput value);
    void PublishProfile(FalloutSourceImageSpaceOutput value);
}
