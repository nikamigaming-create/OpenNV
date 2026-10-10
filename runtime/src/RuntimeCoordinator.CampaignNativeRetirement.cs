using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private Exception? _nativeCampaignLoadFailure;

    private void RetireNativeMainDelivery()
    {
        var failures = new List<Exception>();
        try { _nativeOpeningStageDriver?.RetireSourceMainScriptCaller(); }
        catch (Exception failure) { failures.Add(failure); }
        try { RequireNativeMainDeliveryClosed(); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0)
            throw new AggregateException("Campaign Main delivery retirement retains its actual caller/fade owners.", failures);
    }

    private void RequireNativeMainDeliveryClosed()
    {
        _nativeReferences?.RequireCampaignMainNativeDeliveryClosed();
        if (_nativeMainPlayerBoundWorld is { } bound && !ReferenceEquals(bound, _nativeReferences))
            bound.RequireCampaignMainNativeDeliveryClosed();
    }

    private void RetireNativeCampaignSources()
    {
        RequireNativeMainDeliveryClosed();
        // Archives supply the still-owned world. A world disposal refusal must
        // retain those inputs instead of reporting an independent clean close.
        RetireNativeReferenceWorld();
        _nativePluginStack?.Dispose();
        GD.Print("OPENNV_NATIVE_SESSION_RETIRE phase=source-owners-disposed");
    }

    private Exception RetainNativeCampaignLoadFailure(Exception current)
    {
        if (_nativeCampaignLoadFailure is null) _nativeCampaignLoadFailure = current;
        else if (!Contains(current, _nativeCampaignLoadFailure) && !Contains(_nativeCampaignLoadFailure, current))
            _nativeCampaignLoadFailure = new AggregateException("Campaign load and subsequent cleanup retained independent failures.",
                _nativeCampaignLoadFailure, current);
        else if (Contains(current, _nativeCampaignLoadFailure)) _nativeCampaignLoadFailure = current;
        return _nativeCampaignLoadFailure;

        static bool Contains(Exception container, Exception wanted) => ReferenceEquals(container, wanted) ||
            container is AggregateException aggregate && aggregate.InnerExceptions.Any(error => Contains(error, wanted));
    }
}
