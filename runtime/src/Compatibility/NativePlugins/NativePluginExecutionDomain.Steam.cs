namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // Disjoint from Data16 callback0x500. Root composes enum Operation48 in
    // both ends; this literal is an IPC contract, not an original game opcode.
    private const NativePluginDomainOperation SteamProviderOperation = (NativePluginDomainOperation)48;
    private NativePluginSteamSource? _steamSource;
    private ulong _steamSession, _steamSequence, _steamInterfaceEpoch;
    private readonly Dictionary<ulong, NativePluginSteamToken> _steamTokens = [];
    private readonly List<NativePluginSteamReceipt> _steamReceipts = [];
    private readonly Dictionary<string, (string Sha256, FileStream Lease)> _steamCallableSources = new(StringComparer.OrdinalIgnoreCase);
    private NativePluginSteamPhase? _steamPhase;
    private Exception? _steamFailure;
    private NativePluginSteamFaultPrefix? _steamNativeFault;
    internal IReadOnlyList<NativePluginSteamReceipt> SteamReceipts => _steamReceipts.AsReadOnly();
    internal NativePluginSteamPhase? SteamPhase => _steamPhase;
    internal NativePluginSteamFaultPrefix? SteamNativeFault => _steamNativeFault;
    internal string? SteamSaveBlocker => _steamFailure?.Message ?? (_steamSourceHashEntered ? "source-platform-file-hash-entered" : _steamHandlerEntered ? "source-platform-callback-handler-entered" :
        _steamTokens.Count != 0 ? "source-platform-interface-call-leases-live" : null);

    internal void LoadSteamProvider(NativePluginSteamSource source)
    {
        VerifyOwner(); ArgumentNullException.ThrowIfNull(source); source.Declaration.Validate();
        if (_steamSource is not null || _steamSession != 0 || _callDepth != 0 || _exchangeDepth != 0 || _privateIo is null || ObjectSecurity is null)
            throw new NotSupportedException("Original platform requires one genuine write-restricted child and its selected source lifetime.");
        if (!_privateIo.Selection.OriginalRoots.Any(root => NativePluginPrivateIo.Within(root, source.ProviderPath)))
            throw new InvalidDataException("Platform source is outside this actual child's retained read-only input roots.");
        source.Claim(Generation); _steamSource = source; _steamPhase = NativePluginSteamPhase.LoadEntered;
        var previous = _steamEnteredOperation; _steamEnteredOperation = NativePluginSteamOperation.Load;
        _steamOperationCallbacks = 0; _steamOperationSourceHashes = 0; _steamCallbackRequest = 0; ++_callDepth;
        try
        {
            using var reader = Exchange(SteamProviderOperation, Payload(writer =>
            {
                writer.Write((uint)NativePluginSteamOperation.Load); WriteText(writer, source.ProviderPath);
                WriteText(writer, source.Declaration.ProviderSha256);
                writer.Write(source.Declaration.UserLoggedOnSlot); writer.Write(source.Declaration.SetAchievementSlot);
                writer.Write(source.Declaration.StoreStatisticsSlot); writer.Write(source.Declaration.UtilsAppIdSlot);
                writer.Write(source.Application?.AppId ?? 0);
            }));
            var receipt = ReadSteamReceipt(reader, NativePluginSteamOperation.Load, 0); Finish(reader);
            if (receipt.Session == 0 || receipt.Sequence != 1 || receipt.Phase != NativePluginSteamPhase.Loaded || receipt.Result != 1)
                throw new InvalidDataException("Actual platform loader omitted its retained source receipt.");
            _steamSession = receipt.Session; PublishSteamReceipt(receipt);
        }
        catch (Exception failure) { _steamFailure = failure; _steamPhase = NativePluginSteamPhase.Failed; throw Fatal(failure); }
        finally { --_callDepth; _steamEnteredOperation = previous; }
    }
    internal bool InitializeSteamProvider()
    {
        RequireSteam();
        if (_steamPhase != NativePluginSteamPhase.Loaded)
            throw new InvalidOperationException("Actual platform initialization retains its attempted prefix and cannot replay.");
        _steamPhase = NativePluginSteamPhase.InitializeEntered;
        var receipt = SteamCall(NativePluginSteamOperation.Initialize);
        return (receipt.Result & 0xff) != 0;
    }
    internal bool SteamIsRunning() => (SteamCall(NativePluginSteamOperation.IsRunning).Result & 0xff) != 0;
    internal NativePluginSteamToken? QuerySteamInterface(NativePluginSteamInterface kind)
    {
        RequireSteam();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var receipt = SteamCall(kind switch
        {
            NativePluginSteamInterface.User => NativePluginSteamOperation.User,
            NativePluginSteamInterface.Statistics => NativePluginSteamOperation.Statistics,
            NativePluginSteamInterface.Utilities => NativePluginSteamOperation.Utilities,
            _ => throw new InvalidDataException("Unknown original interface family.")
        });
        if (receipt.Token == 0)
        {
            if (receipt.Result != 0) throw Fatal(new InvalidDataException("A null platform query carried an invented interface pointer."));
            return null;
        }
        if (receipt.Result == 0) throw Fatal(new InvalidDataException("A platform lease has no actual returned interface."));
        var token = new NativePluginSteamToken(Generation, _steamSession, receipt.Token, kind, _steamInterfaceEpoch);
        if (!_steamTokens.TryAdd(token.Token, token)) throw Fatal(new InvalidDataException("Platform reused a live child-owned interface token."));
        return token;
    }
    internal bool SteamLoggedOn(NativePluginSteamToken token)
    { RequireSteamToken(token, NativePluginSteamInterface.User); return (SteamCall(NativePluginSteamOperation.LoggedOn, token.Token).Result & 0xff) != 0; }
    internal bool SteamSetAchievement(NativePluginSteamToken token, string actualName)
    {
        RequireSteamToken(token, NativePluginSteamInterface.Statistics);
        if (actualName.Length is < 1 or > 3 || actualName.Any(character => character is < (char)0x20 or > (char)0x7e))
            throw new NotSupportedException("Original source achievement formatting needs its genuine bounded four-byte/invalid-parameter owner.");
        return (SteamCall(NativePluginSteamOperation.SetAchievement, token.Token, actualName).Result & 0xff) != 0;
    }
    internal bool SteamStoreStatistics(NativePluginSteamToken token)
    { RequireSteamToken(token, NativePluginSteamInterface.Statistics); return (SteamCall(NativePluginSteamOperation.StoreStatistics, token.Token).Result & 0xff) != 0; }
    internal void PumpSteamCallbacks() => _ = SteamCall(NativePluginSteamOperation.RunCallbacks);
    internal void ReleaseSteamInterface(NativePluginSteamToken token)
    {
        RequireSteamToken(token, token.Kind);
        var receipt = SteamCall(NativePluginSteamOperation.ReleaseInterface, token.Token);
        if (receipt.Result != 1) throw Fatal(new InvalidDataException("Actual child interface lease did not retire."));
        _steamTokens.Remove(token.Token);
    }
    internal void ShutdownSteamProvider()
    {
        RequireSteam();
        if (_steamTokens.Count != 0 || _steamPhase is not (NativePluginSteamPhase.Loaded or NativePluginSteamPhase.InitializeReturned))
            throw new InvalidOperationException("Source platform shutdown cannot discard an entered call or live borrowed interface.");
        _steamPhase = NativePluginSteamPhase.ShutdownEntered;
        _ = SteamCall(NativePluginSteamOperation.Shutdown);
        _steamInterfaceEpoch = checked(_steamInterfaceEpoch + 1);
    }
    internal void UnloadSteamProvider()
    {
        RequireSteam();
        if (_steamTokens.Count != 0 || _steamCallbacks.Count != 0 || _steamCallbackLease != Guid.Empty ||
            _steamPhase is not (NativePluginSteamPhase.ShutdownReturned or NativePluginSteamPhase.Loaded))
            throw new InvalidOperationException("Actual platform requires shutdown/interface retirement before FreeLibrary.");
        var receipt = SteamCall(NativePluginSteamOperation.Unload);
        if (receipt.Result != 1 || receipt.Phase != NativePluginSteamPhase.Retired)
            throw Fatal(new InvalidDataException("Actual platform export reference did not retire."));
        _steamSession = 0; _steamSource!.Retire(Generation); _steamSource = null;
        foreach (var lease in _steamCallableSources.Values) lease.Lease.Dispose(); _steamCallableSources.Clear();
    }
    private void RequireSteam()
    {
        VerifyOwner();
        if (_steamFailure is { } retained) throw new InvalidOperationException("Platform retains its exact attempted source prefix.", retained);
        if (_steamSource is null || _steamSession == 0 || _callDepth != 0 || _exchangeDepth != 0 ||
            _steamPhase is NativePluginSteamPhase.Retired or NativePluginSteamPhase.Failed)
            throw new InvalidOperationException("Original platform call has no actual current native source lease.");
        _steamSource.Check(Generation);
    }
    private void RequireSteamToken(NativePluginSteamToken token, NativePluginSteamInterface kind)
    {
        RequireSteam();
        if (token.Generation != Generation || token.Session != _steamSession || token.Kind != kind || token.InterfaceEpoch != _steamInterfaceEpoch ||
            !_steamTokens.TryGetValue(token.Token, out var owned) || !ReferenceEquals(owned, token))
            throw new InvalidOperationException("Steam method has a stale or foreign actual child interface lease.");
    }
    private NativePluginSteamReceipt SteamCall(NativePluginSteamOperation operation, ulong token = 0, string? argument = null)
    {
        RequireSteam();
        var previous = _steamEnteredOperation; _steamEnteredOperation = operation;
        _steamOperationCallbacks = 0; _steamOperationSourceHashes = 0; _steamCallbackRequest = 0; ++_callDepth;
        try
        {
            using var reader = Exchange(SteamProviderOperation, Payload(writer =>
            { writer.Write((uint)operation); writer.Write(_steamSession); writer.Write(token); WriteText(writer, argument ?? ""); }));
            var receipt = ReadSteamReceipt(reader, operation, token); Finish(reader);
            if (receipt.Session != _steamSession || receipt.Sequence != checked(_steamSequence + 1))
                throw new InvalidDataException("Platform source call lost its actual session/ordered result prefix.");
            PublishSteamReceipt(receipt); return receipt;
        }
        catch (Exception failure) { _steamFailure ??= failure; _steamPhase = NativePluginSteamPhase.Failed; throw Fatal(failure); }
        finally { --_callDepth; _steamEnteredOperation = previous; }
    }
    private NativePluginSteamReceipt ReadSteamReceipt(BinaryReader reader, NativePluginSteamOperation operation, ulong inputToken)
    {
        var session = reader.ReadUInt64(); var sequence = reader.ReadUInt64(); var actualOperation = (NativePluginSteamOperation)reader.ReadUInt32();
        var token = reader.ReadUInt64(); var thread = reader.ReadUInt32(); var result = reader.ReadUInt32(); var measured = SteamBoolean(reader); var stack = reader.ReadInt32();
        var registers = reader.ReadUInt32(); var exception = reader.ReadUInt32(); var app = reader.ReadUInt32();
        var phase = (NativePluginSteamPhase)reader.ReadUInt32(); var path = ReadText(reader); var sha = ReadText(reader);
        var calls = ReadSteamPrefixes(reader, successful: true);
        var callbackPrefixes = ReadSteamCallbackPrefixes(reader, successful: true);
        ReadSteamSourceHashPrefixes(reader, successful: true); RequireSteamSourceHashSession(session);
        var foreignCall = calls.Count != 0;
        if (actualOperation != operation || thread != NativeThread || measured != foreignCall || stack != 0 || registers != (measured ? 7U : 0U) || exception != 0 ||
            !Enum.IsDefined(phase) || operation is not (NativePluginSteamOperation.User or NativePluginSteamOperation.Statistics or NativePluginSteamOperation.Utilities or NativePluginSteamOperation.RegisterCallback) && token != inputToken ||
            (path.Length == 0) != (sha.Length == 0))
            throw new InvalidDataException("Actual platform export/method omitted its clean ABI/result/lifetime receipt.");
        RequireSteamPrefix(operation, result, calls);
        var sourceMethod = operation is NativePluginSteamOperation.LoggedOn or NativePluginSteamOperation.SetAchievement or
            NativePluginSteamOperation.StoreStatistics or NativePluginSteamOperation.ApplicationId or NativePluginSteamOperation.RequestStatistics;
        if (sourceMethod != (path.Length != 0))
            throw new InvalidDataException("Actual platform result lost its selected application/callable-owner receipt.");
        NativePluginSteamNativeOwner? owner = null;
        if (path.Length != 0) owner = RetainSteamCallable(path, sha);
        return new(Generation, session, sequence, operation, token, thread, result, measured, stack, registers, exception, app, owner, phase, calls, callbackPrefixes);
    }
    private static bool SteamBoolean(BinaryReader reader) => reader.ReadUInt32() switch
    { 0 => false, 1 => true, _ => throw new InvalidDataException("Platform transport changed its actual Boolean layout.") };
    private IReadOnlyList<NativePluginSteamForeignPrefix> ReadSteamPrefixes(BinaryReader reader, bool successful)
    {
        var count = reader.ReadUInt32();
        if (count > 16) throw new InvalidDataException("Platform transport exceeded its bounded actual call-prefix extent.");
        var values = new NativePluginSteamForeignPrefix[checked((int)count)];
        for (var index = 0; index < values.Length; index++)
        {
            var step = (NativePluginSteamCallStep)reader.ReadUInt32(); var kind = (NativePluginSteamReturnKind)reader.ReadUInt32();
            var returned = SteamBoolean(reader); var result = reader.ReadUInt32(); var stack = reader.ReadInt32();
            var registers = reader.ReadUInt32(); var exception = reader.ReadUInt32();
            var path = ReadText(reader); var sha = ReadText(reader);
            if (!Enum.IsDefined(step) || !Enum.IsDefined(kind) || successful && !returned ||
                returned && (stack != 0 || registers != 7 || exception != 0) ||
                kind == NativePluginSteamReturnKind.None && result != 0 ||
                kind is NativePluginSteamReturnKind.BooleanByte or NativePluginSteamReturnKind.PointerPresence && result > 1 ||
                !returned && index != values.Length - 1)
                throw new InvalidDataException("Platform transport fabricated a returned call after a failed foreign prefix.");
            values[index] = new(step, kind, returned, result, stack, registers, exception, RetainSteamCallable(path, sha));
        }
        return Array.AsReadOnly(values);
    }
    private NativePluginSteamNativeOwner RetainSteamCallable(string path, string sha)
    {
        if (!Path.IsPathFullyQualified(path) || sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Actual Steam callable owner has no full source path/digest.");
        var physical = Path.GetFullPath(path);
        RequireSteamSourceHashOwner(physical, sha);
        if (_steamCallableSources.TryGetValue(physical, out var retained))
        {
            if (!retained.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Actual mapped Steam callable differs from its retained read-only source.");
        }
        else
        {
            // FileShare.Read excludes writer/deletion replacement for this
            // actual lease. Do not rehash a large immutable client on every
            // ordinary Main query; the first admission hashes its own handle.
            var lease = NativeNvseHostSource.Lease(physical, sha, true);
            _steamCallableSources.Add(physical, (sha.ToLowerInvariant(), lease));
        }
        return new(physical, sha.ToLowerInvariant());
    }
    private static void RequireSteamPrefix(NativePluginSteamOperation operation, uint result, IReadOnlyList<NativePluginSteamForeignPrefix> calls)
    {
        (NativePluginSteamCallStep, NativePluginSteamReturnKind)[] expected = operation switch
        {
            NativePluginSteamOperation.Load or NativePluginSteamOperation.Unload or NativePluginSteamOperation.ReleaseInterface => [],
            NativePluginSteamOperation.Initialize or NativePluginSteamOperation.IsRunning => [(NativePluginSteamCallStep.SourceExport, NativePluginSteamReturnKind.BooleanByte)],
            NativePluginSteamOperation.User or NativePluginSteamOperation.Statistics or NativePluginSteamOperation.Utilities => [(NativePluginSteamCallStep.SourceExport, NativePluginSteamReturnKind.PointerPresence)],
            NativePluginSteamOperation.LoggedOn or NativePluginSteamOperation.SetAchievement or NativePluginSteamOperation.StoreStatistics or NativePluginSteamOperation.RequestStatistics => [(NativePluginSteamCallStep.SourceMethod, NativePluginSteamReturnKind.BooleanByte)],
            NativePluginSteamOperation.ApplicationId => [(NativePluginSteamCallStep.SourceMethod, NativePluginSteamReturnKind.ApplicationId)],
            NativePluginSteamOperation.UnregisterCallback when calls.Count == 0 => [],
            NativePluginSteamOperation.RegisterCallback or NativePluginSteamOperation.UnregisterCallback or NativePluginSteamOperation.RunCallbacks or NativePluginSteamOperation.Shutdown => [(NativePluginSteamCallStep.SourceExport, NativePluginSteamReturnKind.None)],
            _ => throw new InvalidDataException("Platform receipt has no original operation owner.")
        };
        if (calls.Count != expected.Length || calls.Where((call, index) => (call.Step, call.Kind) != expected[index]).Any())
            throw new InvalidDataException("Platform result changed its actual source export/method order.");
        if (operation is NativePluginSteamOperation.RegisterCallback or NativePluginSteamOperation.UnregisterCallback) return;
        if (operation != NativePluginSteamOperation.ApplicationId && result > 1 || calls.Count != 0 && calls[0].Result != result)
            throw new InvalidDataException("Platform result differs from the actual returned source call.");
    }
    // The general Exchange fault reader calls this before Finish for operation48.
    // A failed platform call carries its genuine earlier native returns; it
    // never manufactures an ordinary success receipt or retries that prefix.
    private void RetainSteamFault(BinaryReader reader)
    {
        if (reader.ReadUInt32() != 3) throw new InvalidDataException("Platform native failure-prefix schema is unowned.");
        var session = reader.ReadUInt64(); var attempted = reader.ReadUInt32(); var phase = (NativePluginSteamPhase)reader.ReadUInt32();
        var module = SteamBoolean(reader); var input = SteamBoolean(reader); var app = reader.ReadUInt32();
        var calls = ReadSteamPrefixes(reader, successful: false);
        var count = reader.ReadUInt32();
        if (count > 64) throw new InvalidDataException("Platform fault exceeds its actual callable-source extent.");
        var lifetimes = new List<NativePluginSteamCallableLifetime>();
        for (var index = 0U; index < count; index++)
        {
            var path = ReadText(reader); var sha = ReadText(reader);
            var callable = SteamBoolean(reader); var source = SteamBoolean(reader);
            if (path.Length != 0 && !Path.IsPathFullyQualified(path) || path.Length == 0 && sha.Length != 0 ||
                sha.Length != 0 && (sha.Length != 64 || !sha.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Platform fault changed its actual partially retired callable source.");
            if (sha.Length != 0) _ = RetainSteamCallable(path, sha);
            lifetimes.Add(new(path.Length == 0 ? null : Path.GetFullPath(path), sha.Length == 0 ? null : sha.ToLowerInvariant(), callable, source));
        }
        if (!Enum.IsDefined(phase) || attempted != 0 && !Enum.IsDefined((NativePluginSteamOperation)attempted) ||
            session != 0 && _steamSession != 0 && session != _steamSession)
            throw new InvalidDataException("Platform failure belongs to another native session/operation.");
        var callbackPrefixes = ReadSteamCallbackPrefixes(reader, successful: false);
        var callbackLifetimes = ReadSteamCallbackLifetimes(reader);
        ReadSteamSourceHashPrefixes(reader, successful: false);
        _steamNativeFault = new(Generation, session, attempted == 0 ? null : (NativePluginSteamOperation)attempted,
            phase, module, input, app, calls, lifetimes.AsReadOnly(), callbackPrefixes, callbackLifetimes);
        _steamPhase = NativePluginSteamPhase.Failed;
    }
    private void PublishSteamReceipt(NativePluginSteamReceipt receipt)
    { _steamSequence = receipt.Sequence; _steamPhase = receipt.Phase; _steamReceipts.Add(receipt); }
    private void RequireSteamRetired()
    { if (_steamSource is not null || _steamSession != 0 || _steamTokens.Count != 0 || _steamCallbacks.Count != 0 || _steamCallbackLease != Guid.Empty) throw new InvalidOperationException("Native child still owns selected platform source/interfaces."); }
    private void ClearSteamAfterChildExit()
    {
        if (!ChildExited || _steamSourceHashEntered) throw new InvalidOperationException("Platform source cleanup requires the verified exact native child's exit and returned file-hash callback.");
        ClearSteamCallbacksAfterChildExit();
        _steamTokens.Clear(); _steamSession = 0; _steamSource?.Retire(Generation); _steamSource = null;
        foreach (var source in _steamCallableSources.Values) source.Lease.Dispose(); _steamCallableSources.Clear();
    }
    internal void RetireSteamAfterFaultedChildExit()
    {
        VerifyThread();
        if (!ChildExited || Fault is null) throw new InvalidOperationException("Terminal platform cleanup requires this exact failed native child's observed exit.");
        ClearSteamAfterChildExit();
    }
}
