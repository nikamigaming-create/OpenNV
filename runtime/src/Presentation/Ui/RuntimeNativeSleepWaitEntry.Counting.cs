using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class RuntimeNativeSleepWaitEntry
{
    internal void BeginSourceCounting(FalloutRestRequest request)
    {
        request.Validate();
        if (_released || _attachmentError is not null || !IsInsideTree() || !_session.Published ||
            _session.RequestOrdinal != _request || request != _session.Request || _menu is null)
            throw new InvalidOperationException("Source rest Start has no living matching native menu lease.");
        _session.BeginSourceMenuCounting(_menu.PublishSourceCountingTarget);
    }
}
