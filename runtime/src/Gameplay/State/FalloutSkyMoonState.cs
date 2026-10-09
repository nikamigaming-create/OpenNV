using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// The calendar/phase and Moon fields belong to the actual shared Sky. Rendering
// borrows these fields; it cannot create an independent phase or simulation clock.
internal sealed partial class FalloutSkyMoonState
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutSkyTransferState _sky;
    private readonly string _stack;
    private readonly Guid _process;
    private readonly Dictionary<FalloutMoonRole, FalloutMoonSnapshot> _moons = [];
    private readonly Dictionary<FalloutMoonRole, IFalloutNativeMoon> _native = [];
    private readonly Dictionary<Guid, IFalloutNativeMoon> _failedNative = [];
    private FalloutMoonClimate? _climate;
    private long _changed = 1;
    private int _phase;
    private uint _storedDays;
    private int? _thread;
    private bool _factoryEntered, _executing, _retired;
    private string? _failure;
    private object? _projection;
    internal FalloutMoonSource Source { get; }
    internal int Phase => _phase;
    internal Guid Sky => _sky.Identity;
    internal string? SaveBlocker => _failure ?? (_retired ? "source-Moons-retired" :
        _executing ? "source-Moon-factory-or-frame-entered" :
        _moons.Values.Any(row => row.Native is not null && !_native.ContainsKey(row.Role)) ?
        "source-Moon-cold-native-republication-pending" : null);
    internal object State => new
    {
        source = Source,
        sky = Sky,
        process = _process,
        changed = _changed,
        phase = _phase,
        storedDays = _storedDays,
        climate = _climate,
        factoryEntered = _factoryEntered,
        moons = _moons.Values.OrderBy(row => row.Role).ToArray(),
        failure = _failure,
        deferredBy = SaveBlocker,
        nativeDraw = "source-mode-and-shader-inputs-required;pixels-unverified"
    };
    internal FalloutSkyMoonState(FalloutSkyTransferState sky, FalloutPluginStack records, string stack, Guid process)
    {
        ArgumentNullException.ThrowIfNull(sky); ArgumentNullException.ThrowIfNull(records);
        if (string.IsNullOrWhiteSpace(stack) || process == Guid.Empty || records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("Moon source omitted its actual selected stack/process.");
        _sky = sky; _records = records; _stack = stack; _process = process; Source = FalloutMoonSource.Read(sky.Source);
    }
    private long Next() => _changed = checked(_changed + 1);
    private void Require(bool retirement = false)
    {
        Source.Require(_sky.Source);
        if (_retired || !retirement && _failure is not null)
            throw new InvalidOperationException("Moon source retains a retired/failed operation: " + _failure);
        if (_thread is { } thread && thread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Moon source left its actual scene publication thread.");
        if (!retirement && _executing) throw new InvalidOperationException("Moon source factory/frame cannot be reentered.");
    }
    internal void BindThread()
    {
        Require(); _sky.BindPresentationThread(); _thread ??= System.Environment.CurrentManagedThreadId;
    }
    internal void BindProjection(object lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime); BindThread();
        if (_projection is not null && !ReferenceEquals(_projection, lifetime))
            throw new InvalidOperationException("Another actual native scene still owns the source Moon fields.");
        _projection = lifetime;
    }
    internal void SynchronizeFactories(object lifetime, FalloutFormKey climate, Func<FalloutMoonSnapshot, int, IFalloutNativeMoon> construct)
    {
        ArgumentNullException.ThrowIfNull(construct); BindThread();
        if (!ReferenceEquals(_projection, lifetime))
            throw new InvalidOperationException("Moon factory changed its actual native scene publication lease.");
        if ((_sky.Flags & 0x40) == 0) return; // Actual dirty-climate factory predicate.
        if (_sky.Climate != climate)
            throw new InvalidDataException("Moon factory changed the actual retained Sky climate field.");
        var declaration = FalloutMoonClimate.Read(_records.GetEffective(climate));
        _climate = declaration; _factoryEntered = true; Next(); _executing = true;
        try
        {
            foreach (var role in Enum.GetValues<FalloutMoonRole>())
            {
                if (!declaration.Enabled(role))
                {
                    if (_native.TryGetValue(role, out var old)) { old.Retire(); _native.Remove(role); }
                    _moons.Remove(role); Next(); continue;
                }
                if (_native.ContainsKey(role)) continue;
                if (!_moons.TryGetValue(role, out var row))
                {
                    row = new(role, FalloutMoonSettings.Read(_records, role), Guid.NewGuid(), Next(),
                        0, BitConverter.SingleToUInt32Bits(float.MaxValue), 0, null, null, null);
                    // Retain actual source allocation before entering its native
                    // child. A later Secunda failure never loses Masser.
                    _moons.Add(role, row);
                }
                IFalloutNativeMoon? candidate = null;
                try
                {
                    candidate = construct(row, _sky.Mode);
                    if (candidate is null || candidate.Identity != row.CapturedIdentity || candidate.Role != role)
                        throw new InvalidDataException("Moon factory returned another role/source lifetime.");
                    var fields = candidate.Capture();
                    RequireNativeFields(fields);
                    if (row.Native is { } saved && fields != saved)
                        throw new InvalidDataException("Cold Moon factory did not reproduce its retained resource/property fields.");
                    _native.Add(role, candidate);
                    // Both original generated-geometry loaders request the
                    // first phase only inside their mode-two-or-three arm.
                    var pending = row.Native is null && _sky.Mode is 2 or 3 ? 2 : row.Pending;
                    _moons[role] = row with { Native = fields, Pending = pending, Changed = Next() };
                }
                catch (Exception original)
                {
                    if (candidate is not null)
                    {
                        try { candidate.Retire(); }
                        catch (Exception cleanup)
                        {
                            _failedNative[candidate.Identity] = candidate;
                            throw new AggregateException("Moon factory retains original construction and resource retirement failures.", original, cleanup);
                        }
                    }
                    throw;
                }
            }
        }
        catch (Exception error) { _failure = error.ToString(); Next(); throw; }
        finally { _executing = false; }
    }
    internal void RetainFailedFactory(IFalloutNativeMoon child, Exception error)
    {
        if (child.Identity == Guid.Empty || !Enum.IsDefined(child.Role) ||
            !_moons.TryGetValue(child.Role, out var row) || row.CapturedIdentity != child.Identity)
            throw new InvalidOperationException("A foreign Moon reported native constructor retention.", error);
        _failedNative[child.Identity] = child; _failure = error.ToString(); Next();
    }
    internal void RetireNativeFields(object? lifetime = null)
    {
        if (_retired) return; Require(retirement: true);
        if (lifetime is not null && _projection is null) return;
        if (lifetime is not null && !ReferenceEquals(_projection, lifetime))
            throw new InvalidOperationException("A foreign/old scene cannot retire current Moon fields.");
        var failures = new List<Exception>();
        foreach (var (role, child) in _native.ToArray())
        {
            try { child.Retire(); _native.Remove(role); }
            catch (Exception error) { failures.Add(error); }
        }
        foreach (var (identity, child) in _failedNative.ToArray())
        {
            try { child.Retire(); _failedNative.Remove(identity); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0)
        {
            var retained = new AggregateException("Independent Moon fields retain their actual native retirement failures.", failures);
            _failure = retained.ToString(); Next(); throw retained;
        }
        // Resource/projection retirement does not turn a living source Moon
        // into a constructor null. Its captured fields await real republication.
        _projection = null; Next();
    }
    internal void Retire()
    {
        if (_retired) return; RetireNativeFields(); _retired = true; Next();
    }
    private static void RequireNativeFields(FalloutMoonNativeFields fields)
    {
        if (!fields.ParentPublished || !fields.PrimaryChildPublished || !fields.ShadowChildPublished ||
            !fields.PrimaryGeometryPublished || !fields.ShadowGeometryPublished ||
            fields.PrimaryHasTexture && fields.PrimaryTexture is null || fields.ShadowHasTexture && fields.ShadowTexture is null ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(fields.PrimaryAlphaBits)) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(fields.ShadowAlphaBits)))
            throw new InvalidDataException("Moon native publication omitted an actual parent/child/geometry/property field.");
    }
}
