using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRestAutoSaveSite(long RequestOrdinal, FalloutRestRequest Request,
    string RestSourceSha256, string PolicySha256, FalloutRestLocation Location);
internal sealed record FalloutRestAutoSaveSnapshot(string Schema, string RestSourceSha256, string PolicySha256,
    long LastRequestOrdinal, FalloutRestKind? Kind, ulong? SaveOrder, string? Failure);

// Original rest/wait autosave is a distinct native request to the one campaign
// queue. Enqueueing never becomes writer completion, and rest cannot coalesce,
// cancel or retry a committed/failed persistent request.
internal sealed class FalloutRestAutoSave
{
    internal const string Schema = "opennv-rest-autosave/v1";
    private readonly FalloutPluginStack _records;
    private readonly FalloutSleepWait _rest;
    private readonly FalloutRestAutoSavePolicy _policy;
    private readonly FalloutScriptManualSaveRequests _saves;
    private readonly Func<RuntimeSaveNativeSite> _site;
    private long _lastRequestOrdinal;
    private FalloutRestKind? _kind;
    private ulong? _order;
    private string? _failure;
    internal string? Failure => _failure;

    internal FalloutRestAutoSave(FalloutPluginStack records, FalloutSleepWait rest, FalloutRestAutoSavePolicy policy,
        FalloutScriptManualSaveRequests saves, Func<RuntimeSaveNativeSite> actualNativeSite,
        FalloutRestAutoSaveSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(actualNativeSite);
        policy.RequireSource(rest.Source);
        _records = records; _rest = rest; _policy = policy; _saves = saves; _site = actualNativeSite;
        if (restore is null) return;
        if (restore.Schema != Schema || restore.RestSourceSha256 != rest.Source.Identity ||
            restore.PolicySha256 != policy.Identity || restore.LastRequestOrdinal < 0 ||
            restore.LastRequestOrdinal > rest.RequestOrdinal || (restore.LastRequestOrdinal == 0) != (restore.Kind is null) ||
            restore.Kind is { } kind && !Enum.IsDefined(kind) || restore.SaveOrder == 0 ||
            restore.SaveOrder is not null && restore.Kind is null || restore.Failure is not null && string.IsNullOrWhiteSpace(restore.Failure) ||
            restore.LastRequestOrdinal == 0 && restore.Failure is not null ||
            restore.Kind is { } enabledKind && policy.Enabled(enabledKind) && restore.SaveOrder is null && restore.Failure is null)
            throw new InvalidDataException("Cold rest autosave has no complete actual evaluation/request prefix.");
        _lastRequestOrdinal = restore.LastRequestOrdinal; _kind = restore.Kind; _order = restore.SaveOrder; _failure = restore.Failure;
        if (_order is not null) _ = RequireRow();
        // A failed start may have committed enqueueing. Restore the actual row;
        // never call the source policy, native site factory or writer again.
    }

    internal FalloutRestObservation ObserveQueueAdmission(FalloutRestRequest request)
    {
        request.Validate(); _policy.RequireSource(_rest.Source);
        if (_failure is not null) return new(FalloutRestFactState.Unowned, "current-rest-autosave-prefix", _failure);
        if (!_policy.Enabled(request.Kind)) return new(FalloutRestFactState.Satisfied,
            "actual-disabled-rest-autosave-preference:" + _policy.Identity);
        if (!_saves.Order.Bound)
            return new(FalloutRestFactState.Unowned, "actual-rest-autosave-queue", "The selected campaign save queue is not bound.");
        if (_saves.Order.Failure is { } failure)
            return new(FalloutRestFactState.Unowned, "actual-rest-autosave-queue", failure);
        if (_saves.Order.Writing is not null)
            return new(FalloutRestFactState.Denied, "actual-rest-autosave-queue", "Another actual campaign head writer is active.");
        return new(FalloutRestFactState.Satisfied, "actual-rest-autosave-queue:" + _saves.Order.SourceCompatibilityId);
    }

    internal void RequestBeforeCountdown(FalloutRestRequest request)
    {
        request.Validate();
        if (_failure is not null) throw new InvalidOperationException("Rest autosave prefix is closed: " + _failure);
        if (!_rest.Published || !_rest.MenuPending || _rest.Phase != FalloutRestPhase.Choosing ||
            _rest.RequestOrdinal <= _lastRequestOrdinal || request.Origin == FalloutRestOrigin.ScriptHours ||
            JsonSerializer.Serialize(request) != JsonSerializer.Serialize(_rest.Request))
            throw new InvalidOperationException("Rest autosave requires the genuine newly published pre-countdown menu request.");
        var admitted = ObserveQueueAdmission(request);
        if (admitted.State != FalloutRestFactState.Satisfied)
            throw new NotSupportedException("Rest autosave cannot enter its current queue: " + admitted.Reason);
        try
        {
            _lastRequestOrdinal = _rest.RequestOrdinal; _kind = request.Kind; _order = null;
            if (!_policy.Enabled(request.Kind)) return;
            var site = _site();
            if (site.Rest is not null || site.PreviousCell is not null || site.ArrivalDoor is not null ||
                site.ArrivalDoorSha256 is not null || site.Reference is not null || site.ReferenceSha256 is not null)
                throw new InvalidDataException("Rest native site factory substituted a previous interaction or transport request.");
            var restSite = new FalloutRestAutoSaveSite(_rest.RequestOrdinal, request, _rest.Source.Identity,
                _policy.Identity, FalloutRestLocation.Read(_records, site.Cell));
            var actualSite = site with { Reference = request.Bed, ReferenceSha256 = request.BedSha256, Rest = restSite };
            var row = _saves.RequestNative(RuntimeSaveRequestOrigin.NativeRestStart, actualSite);
            _order = row.Order;
            RequireRow();
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            _failure ??= error.GetType().Name + ": " + error.Message;
            throw;
        }
    }

    internal FalloutAdvancementActivityObservation ObserveCompletion()
    {
        if (_failure is not null) return new(FalloutAdvancementActivityState.Unowned, "actual-rest-autosave-prefix:" + _failure);
        if (_lastRequestOrdinal == 0 || _kind is null)
            return new(FalloutAdvancementActivityState.Unowned, "rest-autosave-policy-not-yet-consumed");
        if (_order is null)
        {
            if (_policy.Enabled(_kind.Value))
                return new(FalloutAdvancementActivityState.Unowned, "enabled-rest-autosave-has-no-actual-request");
            return new(FalloutAdvancementActivityState.Satisfied, "actual-disabled-rest-autosave-preference:" + _policy.Identity);
        }
        var row = RequireRow();
        if (row.Disposition is RuntimeSaveRequestDisposition.Pending or RuntimeSaveRequestDisposition.Writing &&
            _saves.Order.Failure is { } queueFailure)
            return new(FalloutAdvancementActivityState.Unowned, "actual-rest-autosave-earlier-head-failure:" + queueFailure);
        return row.Disposition switch
        {
            RuntimeSaveRequestDisposition.Completed when row.Committed is not null && row.CommittedSha256 is not null =>
                new(FalloutAdvancementActivityState.Satisfied, $"actual-rest-autosave-writer:{row.Order}:{row.Request}:{row.CommittedSha256}"),
            RuntimeSaveRequestDisposition.Pending or RuntimeSaveRequestDisposition.Writing =>
                new(FalloutAdvancementActivityState.Held, $"actual-rest-autosave-head-order:{row.Order}:{row.Disposition}"),
            _ => new(FalloutAdvancementActivityState.Unowned, $"actual-rest-autosave-writer:{row.Order}:{row.Disposition}:{row.Error}"),
        };
    }

    private RuntimeSaveRequest RequireRow()
    {
        var row = _saves.Order.Find(_order ?? throw new InvalidOperationException("Rest autosave has no original queue order."));
        var native = row.Native;
        if (row.Origin != RuntimeSaveRequestOrigin.NativeRestStart || row.Destination != RuntimeSaveRequestDestination.Continue ||
            native is not { Rest: { } site } || site.RequestOrdinal != _lastRequestOrdinal || site.Request.Kind != _kind ||
            site.RestSourceSha256 != _rest.Source.Identity || site.PolicySha256 != _policy.Identity || !_policy.Enabled(site.Request.Kind))
            throw new InvalidDataException("Rest autosave replaced its actual source policy/request/destination identity.");
        RuntimeSaveRequestOrder.ValidateNative(_records, row.Origin, native);
        return row;
    }

    internal FalloutRestAutoSaveSnapshot Capture()
    {
        if (_order is not null) _ = RequireRow();
        return new(Schema, _rest.Source.Identity, _policy.Identity, _lastRequestOrdinal, _kind, _order, _failure);
    }
}
