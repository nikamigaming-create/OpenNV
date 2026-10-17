using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutTransientExplosionReference(uint Id, FalloutFormKey Explosion,
    FalloutFormKey Caller, FalloutReferencePlacement Placement);
internal sealed record FalloutTransientExplosionsSnapshot(uint LastId,
    IReadOnlyList<FalloutTransientExplosionReference> Retired);
internal sealed record FalloutTransientExplosionRequest(FalloutFormKey Caller, FalloutExplosion Explosion);

// Preparation may decode owned resources, but cannot emit a blast. Start is
// called once after the world has allocated its authoritative runtime identity.
internal sealed class FalloutPreparedTransientExplosion(FalloutReferencePlacement placement,
    Action<uint, Action, Action<string>> start, Action dispose) : IDisposable
{
    internal FalloutReferencePlacement Placement { get; } = placement;
    internal void Start(uint id, Action completed, Action<string> failed) => start(id, completed, failed);
    public void Dispose() => dispose();
}

// EXPL is a transient runtime reference, not a fabricated winning REFR record.
// This owner deliberately does not implement general item/actor spawning.
internal sealed class FalloutTransientExplosions(FalloutPluginStack records)
{
    private const uint Prefix = 0xff000000;
    private const uint MaximumId = 0x00ffffff;
    private sealed class Instance(FalloutTransientExplosionReference reference)
    {
        internal FalloutTransientExplosionReference Reference { get; } = reference;
        internal bool Retired { get; set; }
        internal string? Error { get; set; }
    }
    private readonly Dictionary<uint, Instance> _instances = [];
    private uint _lastId;
    private int _activeCount;
    private string? _failure;
    internal Func<FalloutTransientExplosionRequest, FalloutPreparedTransientExplosion>? Prepare { get; set; }
    internal string? SaveBlocker => _failure is not null ? "transient-explosion-failure" : _activeCount != 0 ? "transient-explosion" : null;
    internal object State
    {
        get
        {
            _instances.TryGetValue(Prefix | _lastId, out var last);
            return new
            {
                lastId = _lastId,
                active = _activeCount,
                retired = _instances.Count - _activeCount,
                saveBlocker = SaveBlocker,
                failure = _failure,
                lastReference = last?.Reference,
                lastRetired = last?.Retired,
                lastError = last?.Error
            };
        }
    }
    internal static bool UsesTransientNamespace(uint id) => (id & Prefix) == Prefix;

    internal FalloutScriptFunction Function(Func<FalloutFormKey> caller) => FalloutScriptFunction.Typed(
        [FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.OptionalNumber,
            FalloutScriptArgumentKind.OptionalNumber, FalloutScriptArgumentKind.OptionalNumber],
        arguments => Place(caller(), arguments));

    private FalloutScriptValue Place(FalloutFormKey caller, IReadOnlyList<FalloutScriptArgument> arguments)
    {
        if (arguments.Count is < 1 or > 4) throw new InvalidDataException("PlaceAtMe requires a base and up to three placement arguments.");
        if (arguments.Count > 1 && arguments[1].Number != 1 ||
            arguments.Count > 2 && arguments[2].Number != 0 || arguments.Count > 3 && arguments[3].Number != 0)
            throw new NotSupportedException("PlaceAtMe currently owns one EXPL at the caller; count/offset placement needs its source placement owner.");
        if (records.Plugins.Count == 256)
            throw new NotSupportedException("PlaceAtMe transient reference namespace is occupied by load-order index FF.");
        if (_failure is not null)
            throw new InvalidOperationException("A failed transient explosion cannot be retried in this world.");
        if (_lastId == MaximumId) throw new InvalidOperationException("Transient explosion identity space is exhausted.");
        if (records.RuntimeFormId(caller) != 0x14 && records.GetEffective(caller).Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("PlaceAtMe requires a spatial calling reference.");
        var key = arguments[0].Value.FormKey(records);
        var declaration = records.GetEffective(key);
        if (declaration.Signature != "EXPL")
            throw new NotSupportedException("PlaceAtMe base has no transient EXPL owner.");
        var explosion = FalloutExplosion.Read(records, key);
        explosion.RequireRuntimeDamageOwner();
        if (explosion.HasUnpresentedVisuals || explosion.ImageSpace is not null || explosion.Damage != 0 || explosion.RadiationLevel != 0 ||
            explosion.PushesSourceOnly || declaration.ReadSubrecords().Any(field => field.Signature == "SCRI"))
            throw new NotSupportedException("Placed EXPL requires an unowned light, impact, image-space preparation, damage/event attribution, source-only force or attached script owner.");
        if (explosion.FirstSound is not null || explosion.SecondSound is not null)
            throw new NotSupportedException("Placed EXPL sound preparation and transient emitter identity are not yet owned.");
        using var prepared = (Prepare ?? throw new NotSupportedException("PlaceAtMe has no active transient explosion presentation owner."))
            (new(caller, explosion));
        prepared.Placement.Validate();
        var id = Prefix | ++_lastId;
        var instance = new Instance(new(id, key, caller, prepared.Placement.Copy()));
        _instances.Add(id, instance);
        _activeCount++;
        try
        {
            prepared.Start(id, () => Complete(id), error => Fail(id, error));
            if (instance.Error is { } error) throw new InvalidOperationException("Placed explosion failed: " + error);
            return FalloutScriptValue.Form(id);
        }
        catch (Exception error)
        {
            Fail(id, error.Message);
            throw;
        }
    }

    private void Complete(uint id)
    {
        var instance = _instances[id];
        if (instance.Error is not null) return;
        if (instance.Retired)
            throw new InvalidOperationException("Transient explosion completion has no active successful lifetime.");
        instance.Retired = true;
        _activeCount--;
    }
    private void Fail(uint id, string error)
    {
        var instance = _instances[id];
        instance.Error ??= string.IsNullOrWhiteSpace(error) ? "Transient explosion lost its effect owner." : error;
        _failure ??= instance.Error;
    }
    internal void RetirePresentation()
    {
        Prepare = null;
        foreach (var instance in _instances.Values.Where(value => !value.Retired))
            Fail(instance.Reference.Id, "Transient explosion presentation retired before natural completion.");
    }
    internal void RequireCapture()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException($"Saving {blocker} requires its active effect continuation.");
    }
    internal FalloutTransientExplosionsSnapshot Capture()
    {
        RequireCapture();
        return new(_lastId, _instances.Values.OrderBy(value => value.Reference.Id)
            .Select(value => value.Reference with { Placement = value.Reference.Placement.Copy() }).ToArray());
    }
    internal void Restore(FalloutTransientExplosionsSnapshot? snapshot)
    {
        if (_lastId != 0 || _instances.Count != 0) throw new InvalidOperationException("Transient explosion restore requires a fresh owner.");
        if (snapshot is null) return;
        ValidateSnapshot(snapshot);
        if (records.Plugins.Count == 256 && snapshot.LastId != 0)
            throw new InvalidDataException("Saved transient explosion allocator collides with loaded forms.");
        var restored = new Dictionary<uint, Instance>();
        foreach (var value in snapshot.Retired)
        {
            if (records.GetEffective(value.Explosion).Signature != "EXPL" || records.GetEffective(value.Placement.Cell).Signature != "CELL" ||
                records.RuntimeFormId(value.Caller) != 0x14 && records.GetEffective(value.Caller).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Saved transient explosion source identity is invalid.");
            restored.Add(value.Id, new(value with { Placement = value.Placement.Copy() }) { Retired = true });
        }
        foreach (var (id, value) in restored) _instances.Add(id, value);
        _lastId = snapshot.LastId;
    }
    internal static void ValidateSnapshot(FalloutTransientExplosionsSnapshot snapshot)
    {
        if (snapshot.LastId > MaximumId || snapshot.Retired is null || snapshot.Retired.Count != snapshot.LastId)
            throw new InvalidDataException("Saved transient explosion allocator is invalid.");
        var ids = new HashSet<uint>();
        foreach (var value in snapshot.Retired)
        {
            if (value is null || value.Placement is null || !UsesTransientNamespace(value.Id) || (value.Id & MaximumId) is 0 ||
                (value.Id & MaximumId) > snapshot.LastId || !ids.Add(value.Id))
                throw new InvalidDataException("Saved transient explosion identity is invalid or duplicated.");
            value.Placement.Validate();
        }
    }
    internal void ValidateFormHandle(double raw)
    {
        var value = FalloutScriptValue.Form(raw);
        var id = (uint)value.Number;
        if (id is 0 or 0x14) return;
        if (UsesTransientNamespace(id) && records.Plugins.Count < 256)
        {
            if (!_instances.ContainsKey(id)) throw new InvalidDataException($"Script contains an unowned transient reference {id:x8}.");
            return;
        }
        _ = records.GetEffective(records.RuntimeFormKey(id));
    }
}
