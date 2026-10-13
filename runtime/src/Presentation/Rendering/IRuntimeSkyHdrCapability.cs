using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// Process-local capability lifecycle. Source gameplay consumes only the
// IFalloutSkyHdrCapability receipt; the host owns construction and retirement.
internal interface IRuntimeSkyHdrCapability : IFalloutSkyHdrCapability
{
    bool Returned { get; }
    bool Retired { get; }
    Task Retirement { get; }
    void Begin();
    void BeginRetirement();
}
