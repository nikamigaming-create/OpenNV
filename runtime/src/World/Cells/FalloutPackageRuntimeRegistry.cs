using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// One actual mutable package object per winning form. Process slots borrow the
// same instance; a semantic driver or a second Actor never gets another count.
internal sealed class FalloutPackageRuntimeObject
{
    private int _lifetimeCount;
    internal FalloutPluginRecord Record { get; }
    internal FalloutFormKey Form => Record.FormKey;
    internal string RecordSha256 { get; }
    internal sbyte Kind { get; }
    internal uint SourceFlags { get; }
    internal int Procedure { get; set; } = -1;
    internal uint Flags { get; set; }
    internal int LifetimeCount => Volatile.Read(ref _lifetimeCount);
    internal bool ProcedureAvailable { get; set; } = true;
    internal bool FlagsAvailable { get; set; } = true;
    internal bool CountAvailable { get; set; } = true;
    internal readonly List<FalloutPackageRuntimeMutation> History = [];
    internal FalloutPackageRuntimeObject(FalloutPluginRecord record)
    {
        Record = record; var data = FalloutPackageData.Read(record);
        RecordSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
        Kind = unchecked((sbyte)data.Procedure); SourceFlags = data.Flags; Flags = SourceFlags;
    }
    internal int Retain() => Interlocked.Increment(ref _lifetimeCount);
    internal int Release() => Interlocked.Decrement(ref _lifetimeCount);
    internal void RestoreCount(int count) => Volatile.Write(ref _lifetimeCount, count);
}

internal sealed partial class FalloutPackageRuntimeRegistry : IDisposable
{
    internal const string Schema = "opennv-package-runtime/v1";
    internal const uint TemporaryFlag = 0x800, ScriptAssignedFlag = 0x4000;
    internal const uint NullGameHourBits = 0x41400000;
    private readonly FalloutPluginStack _records;
    private readonly string _executable, _stack;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<FalloutFormKey, FalloutPackageRuntimeObject> _packages = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutPackageRuntimePublication> _publications = [];
    private readonly List<FalloutPackageCountAuthorityObservation> _countAuthorityObservations = [];
    private bool _countAuthorityAvailable = true;
    private FalloutPackageRuntimeObject? _firstUnavailable;
    private long _sequence;
    private bool _disposed;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal string Stack => _stack;
    internal string Executable => _executable;
    internal string Source { get; }
    internal string? SaveBlocker => !_countAuthorityAvailable ? "source-package-global-count-writer-unbound" :
        _firstUnavailable is { } failed
        ? "source-mutable-package-writer-unbound:" + failed.Form : null;
    internal Guid? RestoredFactory { get; private set; }
    internal FalloutPackageRuntimeRegistry(FalloutPluginStack records, string executable, string stack, FalloutPackageRegistrySnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (!FalloutSourceMainFamily.IsFallout3(executable)) throw new NotSupportedException("Mutable source package constructors are unreviewed for this image.");
        _records = records; _executable = executable; _stack = stack;
        Source = SourceFor(executable);
        foreach (var record in records.EffectiveRecords("PACK")) _packages.Add(record.FormKey, new(record));
        if (restore is null) _publications.Add(new(Factory, null)); else Restore(restore);
    }
    internal static string SourceFor(string executable) => FalloutAdvancementRuntimeReceipt.Hash(executable +
        "\0source-mutable-winning-PACK/v1;constructor-count-zero-procedure-minus-one;PKDT-kind-flags-independent;" +
        "shared-atomic-count-source-ordered-writers;unknown-count-writer-retains-last-count;O1-unknown-old-pointer-authority-barrier;" +
        "fresh-cold-objects-no-counter-clock-or-command-replay");
    internal FalloutPackageRuntimeObject Require(FalloutFormKey form)
    {
        RequireCurrent();
        var package = _packages.TryGetValue(form, out var actual) ? actual :
            throw new NotSupportedException("Actual winning mutable PACK object is absent: " + form);
        RequireObject(package); return package;
    }
    internal void RequireObject(FalloutPackageRuntimeObject package)
    {
        RequireCurrent();
        if (!_packages.TryGetValue(package.Form, out var actual) || !ReferenceEquals(actual, package) ||
            !ReferenceEquals(_records.GetEffective(package.Form), package.Record))
            throw new InvalidOperationException("Package mutation borrowed a foreign or retired mutable instance.");
    }
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Package registry belongs to its actual product command thread.");
    }
    internal int Retain(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
    {
        RequireCount(package);
        return Mutate(invocation, package, FalloutPackageRuntimeMutationKind.Retain, "actual-state-package-retain", () => package.Retain());
    }
    internal int Release(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
    {
        RequireCount(package);
        return Mutate(invocation, package, FalloutPackageRuntimeMutationKind.Release, "actual-state-package-release", () => package.Release());
    }
    private void RequireCount(FalloutPackageRuntimeObject package)
    {
        RequireObject(package);
        if (!_countAuthorityAvailable || !package.CountAvailable)
            throw new NotSupportedException("source-package-lifetime-count-current-writer-unowned");
    }
    internal void RetainUnownedLifetime(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey form, string producer)
    {
        if (!_packages.TryGetValue(form, out var package)) { RetainUnownedCountAuthority(invocation, producer); return; }
        _ = Mutate(invocation, package, FalloutPackageRuntimeMutationKind.UnownedLifetime, producer,
            () => { package.CountAvailable = false; return package.LifetimeCount; });
    }
    internal void RetainUnownedCountAuthority(FalloutPackageCommandFieldInvocation invocation, string producer)
    {
        RequireCurrent(); RequireInvocation(invocation); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        _countAuthorityAvailable = false;
        _countAuthorityObservations.Add(new(checked(++_sequence), invocation, producer));
    }
    internal void PublishProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package, int value, string producer)
    {
        RequireObject(package);
        if (!package.ProcedureAvailable) throw new NotSupportedException("source-package-procedure-current-writer-unowned");
        _ = Mutate(invocation, package, FalloutPackageRuntimeMutationKind.Procedure, producer,
            () => { package.Procedure = value; return package.LifetimeCount; });
    }
    internal void PublishScriptAssignedFlag(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
    {
        if (!package.FlagsAvailable) throw new NotSupportedException("source-package-runtime-flags-current-writer-unowned");
        _ = Mutate(invocation, package, FalloutPackageRuntimeMutationKind.ScriptAssignedFlag, "actual-AddScriptPackage-runtime-flag-store",
            () => { package.Flags |= ScriptAssignedFlag; return package.LifetimeCount; });
    }
    internal void RetainUnownedWriter(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package, bool procedure, string producer) =>
        _ = Mutate(invocation, package, procedure ? FalloutPackageRuntimeMutationKind.UnownedProcedure : FalloutPackageRuntimeMutationKind.UnownedFlags,
            producer, () => { if (procedure) package.ProcedureAvailable = false; else package.FlagsAvailable = false; return package.LifetimeCount; });
    private int Mutate(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package,
        FalloutPackageRuntimeMutationKind kind, string producer, Func<int> write)
    {
        RequireObject(package); RequireInvocation(invocation); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        var count = package.LifetimeCount; var procedure = package.Procedure; var flags = package.Flags;
        var result = write();
        RetainUnavailable(package);
        package.History.Add(new(checked(++_sequence), invocation, package.Form, kind, count, package.LifetimeCount,
            procedure, package.Procedure, flags, package.Flags, producer));
        return result;
    }
    private void RetainUnavailable(FalloutPackageRuntimeObject package)
    {
        if (!package.ProcedureAvailable || !package.FlagsAvailable || !package.CountAvailable) _firstUnavailable ??= package;
    }
    internal static void RequireInvocation(FalloutPackageCommandFieldInvocation invocation)
    {
        if (invocation is null || invocation.Actor is null) throw new InvalidDataException("Package writer omitted its typed Actor invocation.");
        invocation.Actor.Validate();
        if (invocation.SourceCommand == Guid.Empty || invocation.Process == Guid.Empty || invocation.Epoch < 1 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Package writer lost its actual source command, Actor/process/epoch or entered product thread.");
    }
    public void Dispose() { if (_disposed) return; RequireCurrent(); _disposed = true; }
}
