using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// C# owns each process generation, request, callback, fault and retirement.
// Authored diagnostic calls and source-bound NVSE initialization share this
// owner. Game objects, engine hooks and plugin co-save I/O require separate owners.
internal sealed partial class NativePluginExecutionDomain : IDisposable
{
    private static long _generationSequence = RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly object _faultGate = new();
    private readonly NativePluginDomainChild _process;
    private readonly TimeSpan _timeout;
    private readonly int _maximumDepth, _maximumCallbacks;
    private readonly Task _diagnosticDrain;
    private readonly StringBuilder _diagnostics = new();
    private readonly Dictionary<ulong, NativePluginFunction> _functions = [];
    private NativePluginDomainFault? _fault;
    private bool _diagnosticsTruncated, _disposed, _retired, _childExited;
    private FileStream? _moduleSource;
    private NativePluginModule? _module;
    private Func<NativePluginCallback, uint>? _callback;
    private CancellationTokenSource? _transaction;
    private CancellationTokenRegistration _watchdog;
    private int _exchangeDepth, _callDepth, _callbackCount;
    private ulong _nextRequest, _lastCallback, _callbackOwner, _currentCall;
    private string _operation = "startup";
    internal ulong Generation { get; }
    internal int ProcessId { get; }
    internal uint NativeThread { get; private set; }
    internal bool NaturallyRetired => _retired;
    internal bool ChildExited { get => Volatile.Read(ref _childExited); private set => Volatile.Write(ref _childExited, value); }
    internal int? ChildExitCode { get; private set; }
    internal NativePluginTokenObjectReceipt? ObjectSecurity => _process.ObjectSecurity;
    private bool _childExitDiagnosticPublished;
    internal NativePluginDomainFault? Fault { get { lock (_faultGate) return CurrentFault(); } }
    internal Func<NativePluginCallback, uint>? Callback
    {
        get => _callback;
        set
        {
            VerifyOwner();
            if (_callDepth != 0) throw new InvalidOperationException("An active callback/call owns its callback registration.");
            _callback = value;
        }
    }

    internal NativePluginExecutionDomain(string companion, TimeSpan? timeout = null, int maximumDepth = 8, int maximumCallbacks = 32, NativePluginPrivateIo? privateIo = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The native execution domain requires Windows.");
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, NativeMaximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCallbacks, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCallbacks, 64);
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout < TimeSpan.FromMilliseconds(50) || _timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _maximumDepth = maximumDepth; _maximumCallbacks = maximumCallbacks;
        var executable = Path.GetFullPath(companion);
        if (!File.Exists(executable)) throw new FileNotFoundException("Native x86 companion is absent.", executable);
        VerifyPe(executable, dll: false);
        Generation = checked((ulong)Interlocked.Increment(ref _generationSequence));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.ArgumentList.Add("--generation"); start.ArgumentList.Add(Generation.ToString(CultureInfo.InvariantCulture));
        _process = NativePluginDomainChild.Start(start, privateIo);
        ProcessId = _process.Id; _diagnosticDrain = DrainDiagnostics();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.Hello, []);
            if (reader.ReadUInt32() != checked((uint)ProcessId)) throw new InvalidDataException("Native companion process identity drifted.");
            NativeThread = reader.ReadUInt32();
            if (NativeThread == 0 || reader.ReadUInt32() != 32 || reader.ReadUInt32() != MaximumPayload || reader.ReadUInt32() != NativeMaximumDepth)
                throw new InvalidDataException("Native companion pointer width or protocol capability drifted.");
            Finish(reader);
            if (privateIo is not null) PreparePrivateIo(privateIo);
        }
        catch (Exception error)
        {
            MarkFault(error, null); StopOwnedChild(); DrainAfterExit();
            if (ChildExited) { ClearPrivateIo(); _process.Dispose(); _disposed = true; }
            throw FaultException(error);
        }
    }

    internal NativePluginModule LoadAuthoredModule(string path, string expectedSha256)
    {
        VerifyOwner();
        if (_callDepth != 0 || NativeModuleCount != 0) throw new InvalidOperationException("Authored module admission requires an empty call/module owner.");
        var fullPath = Path.GetFullPath(path);
        var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var admissionStarted = false;
        try
        {
            using (var pe = new PEReader(source, PEStreamOptions.LeaveOpen)) VerifyPe(pe, dll: true);
            source.Position = 0;
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(source)), expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Authored native module identity drifted before admission.");
            admissionStarted = true;
            using var reader = Exchange(NativePluginDomainOperation.LoadAuthored, Payload(writer => WriteText(writer, fullPath)));
            var module = new NativePluginModule(Generation, reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32(), ReadLifetime(reader), reader.ReadUInt32());
            Finish(reader);
            if (module.Handle == 0 || module.GuestBase == 0 || module.ExportCount == 0 || module.Entry.TlsAttach != 1 ||
                module.Entry.DllAttach != 1 || module.Entry.TlsOrder == 0 || module.Entry.TlsOrder >= module.Entry.DllOrder)
                throw new InvalidDataException("Native module lacks its actual loader-entry receipt.");
            VerifyOwner(); _module = module; _moduleSource = source; return module;
        }
        catch (NativePluginDomainRefusal) { source.Dispose(); throw; }
        catch (Exception error)
        {
            source.Dispose();
            if (!admissionStarted) throw;
            throw Fatal(error);
        }
    }

    internal NativePluginFunction Resolve(NativePluginModule module, string export, NativePluginAbi abi)
    {
        VerifyModule(module);
        if (_callDepth != 0) throw new InvalidOperationException("Export resolution cannot change a callback-owned call table.");
        if (abi is not (NativePluginAbi.Cdecl or NativePluginAbi.Stdcall or NativePluginAbi.Thiscall)) throw new ArgumentOutOfRangeException(nameof(abi));
        if (export.Length is 0 or > 255 || export.Any(character => character is < (char)0x21 or > (char)0x7e))
            throw new ArgumentException("Native export names require nonempty ASCII without spaces or NUL.", nameof(export));
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.Resolve, Payload(writer => { writer.Write(module.Handle); writer.Write((uint)abi); WriteText(writer, export); }));
            var function = new NativePluginFunction(Generation, module.Handle, reader.ReadUInt64(), abi, export, reader.ReadUInt32());
            Finish(reader);
            if (function.Handle == 0 || function.GuestAddress == 0 || !_functions.TryAdd(function.Handle, function))
                throw new InvalidDataException("Native function identity drifted.");
            VerifyOwner(); return function;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal NativePluginCallReceipt Call(NativePluginFunction function, uint first, uint second)
    {
        VerifyOwner();
        if (_module is null || function.Generation != Generation || function.Module != _module.Handle ||
            !_functions.TryGetValue(function.Handle, out var owned) || !ReferenceEquals(owned, function))
            throw new InvalidOperationException("Native function belongs to a stale module/process generation.");
        if (_callDepth >= _maximumDepth) throw Fatal(new InvalidOperationException("Native reentrant call depth exceeded its owner budget."));
        ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.Call, Payload(writer =>
            { writer.Write(function.Module); writer.Write(function.Handle); writer.Write(first); writer.Write(second); }));
            var receipt = new NativePluginCallReceipt(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            Finish(reader);
            if (receipt.StackDelta != 0 || receipt.PreservedRegisters != 7 || receipt.ExceptionCode != 0)
                throw new InvalidDataException("Native ABI receipt did not retain a clean call boundary.");
            VerifyOwner(); return receipt;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
        finally { --_callDepth; }
    }

    internal NativePluginUnloadReceipt Unload(NativePluginModule module)
    {
        VerifyModule(module);
        if (_callDepth != 0) throw new InvalidOperationException("A native call/callback still owns the module.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.Unload, Payload(writer => writer.Write(module.Handle)));
            var receipt = new NativePluginUnloadReceipt(ReadLifetime(reader), reader.ReadUInt32() != 0); Finish(reader);
            if (receipt.Lifetime.TlsDetach != 1 || receipt.Lifetime.DllDetach != 1 || receipt.MappingPresent)
                throw new InvalidDataException("Native module retirement retains its image or lacks TLS/DllMain detach.");
            VerifyOwner(); _module = null; _functions.Clear(); _moduleSource!.Dispose(); _moduleSource = null; return receipt;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed && ChildExited) return;
        if (_callDepth != 0) throw new InvalidOperationException("A native call/callback prevents retirement of its process generation.");
        Exception? failure = null;
        try
        {
            if (Fault is null)
            {
                if (_module is not null) Unload(_module);
                if (_nvsePlugin is not null) UnloadNvse(_nvsePlugin);
                RequirePrivateIoRetired();
                ReleaseGuestResources();
                using var reader = Exchange(NativePluginDomainOperation.Retire, []);
                var guest = ReadGuestStatistics(reader); Finish(reader);
                CheckGuestStatistics(guest, retiredDelta: -checked((int)_guestRetired), reservedDelta: -(long)_guestReserved);
                PublishGuestStatistics(guest);
                _process.StandardInput.Close();
                if (!_process.WaitForExit(checked((int)_timeout.TotalMilliseconds))) throw new TimeoutException("Native retirement acknowledgement did not produce process exit.");
                DrainAfterExit();
                if (_process.ExitCode != 0 || _diagnosticsTruncated || _diagnostics.Length != 0)
                    throw new InvalidDataException("Native retirement exited with a failure or unowned diagnostic output.");
                _retired = true; ChildExited = true;
            }
        }
        catch (Exception error) { MarkFault(error, null); failure = error; }
        finally
        {
            if (!_retired) StopOwnedChild();
            DrainAfterExit();
            if (ChildExited)
            {
                var cleanup = new List<Exception>();
                try { _moduleSource?.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                _moduleSource = null; _module = null; _functions.Clear();
                try { ClearGuestCapabilities(); } catch (Exception error) { cleanup.Add(error); }
                try { ClearNvseCapabilities(); } catch (Exception error) { cleanup.Add(error); }
                try { ClearPrivateIo(); } catch (Exception error) { cleanup.Add(error); }
                try { _process.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                _disposed = true;
                if (cleanup.Count != 0)
                {
                    if (failure is not null) cleanup.Insert(0, failure);
                    failure = new AggregateException("Native source/capability retirement failed after verified child closure.", cleanup);
                    MarkFault(failure, null);
                }
            }
            else
            {
                // Keep actual source leases, callable capabilities, process
                // handle and first failure for another exact-child cleanup.
                MarkFault(new TimeoutException("The owned native child has not exited after bounded retirement."), null);
                failure ??= new TimeoutException("Owned child remains alive.");
            }
        }
        if (failure is not null) throw FaultException(failure);
    }

    private BinaryReader Exchange(NativePluginDomainOperation operation, byte[] payload)
    {
        VerifyOwner();
        var root = _exchangeDepth++ == 0;
        var previousOperation = _operation; var previousCall = _currentCall;
        var request = ++_nextRequest;
        lock (_faultGate) { _operation = operation.ToString(); _currentCall = request; }
        if (root)
        {
            _callbackCount = 0; _nvseCallbackCount = 0; _ioCallbackCount = 0; _transaction = new CancellationTokenSource();
            _watchdog = _transaction.Token.Register(() =>
            { MarkFault(new TimeoutException("Native transaction exceeded its complete call/callback deadline."), null); StopOwnedChild(); });
            _transaction.CancelAfter(_timeout);
        }
        try
        {
            WriteFrame(new(NativePluginDomainMessage.Request, (uint)operation, Generation, request, _callbackOwner, payload));
            for (; ; )
            {
                var frame = ReadFrame();
                if (frame.Generation != Generation) throw new InvalidDataException("Native reply belongs to a foreign process generation.");
                if (frame.Kind == NativePluginDomainMessage.Fault)
                {
                    if (frame.Id != request || frame.Parent != _callbackOwner || frame.Operation != (uint)operation)
                        throw new InvalidDataException("Native fault does not belong to its waiting call frame.");
                    using var fault = Reader(frame.Payload); var code = fault.ReadUInt32(); var reason = ReadText(fault); Finish(fault);
                    if (code == 0 || reason.Length == 0)
                        throw new InvalidDataException("Native fault lacks a failure code or reason.");
                    MarkFault(new InvalidDataException(reason), code); throw FaultException();
                }
                if (frame.Kind is NativePluginDomainMessage.Callback or NativePluginDomainMessage.StateQuery or NativePluginDomainMessage.NvseCallback or NativePluginDomainMessage.IoCallback)
                {
                    if (operation is not (NativePluginDomainOperation.Call or NativePluginDomainOperation.NvseQuery or
                        NativePluginDomainOperation.NvseLoad or NativePluginDomainOperation.NvseMessage or NativePluginDomainOperation.NvseSerialization or NativePluginDomainOperation.NvseCommand or NativePluginDomainOperation.UnloadNvse))
                        throw new InvalidDataException("A native callback arrived outside its executable call owner.");
                    DispatchCallback(frame, request); continue;
                }
                if (frame.Kind != NativePluginDomainMessage.Reply || frame.Id != request || frame.Parent != _callbackOwner || frame.Operation != (uint)operation)
                    throw new InvalidDataException("Native reply does not belong to its waiting call frame.");
                var reader = Reader(frame.Payload);
                try
                {
                    var status = reader.ReadUInt32(); var liveModules = reader.ReadUInt32(); var reason = ReadText(reader);
                    var expectedModules = operation switch
                    {
                        NativePluginDomainOperation.LoadAuthored or NativePluginDomainOperation.LoadNvse => status == 0 ? 1U : 0U,
                        NativePluginDomainOperation.Unload or NativePluginDomainOperation.UnloadNvse => status == 0 ? 0U : 1U,
                        NativePluginDomainOperation.Resolve or NativePluginDomainOperation.Call or NativePluginDomainOperation.NvseQuery or
                        NativePluginDomainOperation.NvseLoad or NativePluginDomainOperation.NvseMessage or NativePluginDomainOperation.NvseSerialization or
                        NativePluginDomainOperation.NvseExpressionAbi or NativePluginDomainOperation.NvseCommand or NativePluginDomainOperation.NvseExpressionStatistics or
                        NativePluginDomainOperation.NvseValuesAttach or NativePluginDomainOperation.NvseValuesStatistics or NativePluginDomainOperation.NvseValueHeap or
                        NativePluginDomainOperation.NvseLocalCreate or NativePluginDomainOperation.NvseLocalFill or NativePluginDomainOperation.NvseLocalSeal or
                        NativePluginDomainOperation.NvseLocalRetire or NativePluginDomainOperation.NvseLocalStatistics or
                        NativePluginDomainOperation.NvseObjectBind or NativePluginDomainOperation.NvseObjectRetire or NativePluginDomainOperation.NvseScriptInterface or
                        NativePluginDomainOperation.NvseObjectRefresh or NativePluginDomainOperation.NvseLocalAttachScript or
                        NativePluginDomainOperation.NvseFileMethods or NativePluginDomainOperation.NvseBinaryMethods or
                        NativePluginDomainOperation.NvseBinaryBind or NativePluginDomainOperation.NvseBinaryRetire => 1U,
                        NativePluginDomainOperation.GuestCapabilities or NativePluginDomainOperation.GuestAllocate or
                        NativePluginDomainOperation.GuestRead or NativePluginDomainOperation.GuestWrite or
                        NativePluginDomainOperation.GuestRelease or NativePluginDomainOperation.GuestBindState or
                        NativePluginDomainOperation.GuestStatistics or NativePluginDomainOperation.GuestSeal => NativeModuleCount,
                        _ => 0U,
                    };
                    if (liveModules != expectedModules) throw new InvalidDataException("Native module lifetime drifted across its request.");
                    if (status != 0) { Finish(reader); throw new NativePluginDomainRefusal(status, liveModules, reason); }
                    if (reason.Length != 0) throw new InvalidDataException("Native success reply carries an unowned diagnostic.");
                    return reader;
                }
                catch { reader.Dispose(); throw; }
            }
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
        finally
        {
            if (root) { _watchdog.Dispose(); _transaction!.Dispose(); _transaction = null; }
            lock (_faultGate) { _operation = previousOperation; _currentCall = previousCall; }
            --_exchangeDepth;
        }
    }

    private void DispatchCallback(Frame frame, ulong waitingCall)
    {
        if (frame.Parent != waitingCall || (frame.Id & CallbackBit) == 0 || frame.Id <= _lastCallback || _callDepth == 0)
            throw new InvalidDataException("Native callback does not belong to its current call/sequence.");
        if (frame.Kind == NativePluginDomainMessage.IoCallback)
        {
            if (++_ioCallbackCount > 1048576) throw new InvalidDataException("Native private I/O callback budget exceeded.");
        }
        else if (frame.Kind == NativePluginDomainMessage.NvseCallback)
        {
            if (++_nvseCallbackCount > _nvseCallbackBudget) throw new InvalidDataException("NVSE interface callback budget exceeded.");
        }
        else if (++_callbackCount > _maximumCallbacks) throw new InvalidDataException("Native callback budget exceeded.");
        _lastCallback = frame.Id;
        var previous = _callbackOwner; _callbackOwner = frame.Id;
        try
        {
            uint value = 0; byte[]? typedReply = null;
            if (frame.Kind == NativePluginDomainMessage.IoCallback) typedReply = DispatchPrivateIo(frame, waitingCall);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation >= LocalBegin && frame.Operation <= LocalEnd)
                typedReply = DispatchNvseLocals(frame);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation >= BinaryBegin && frame.Operation <= BinaryEnd)
                typedReply = DispatchNvseBinary(frame);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation >= SourceFileBegin && frame.Operation <= SourceFileEnd)
                typedReply = DispatchNvseSourceFile(frame);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation is 0x401 or 0x403)
                typedReply = DispatchNvseSourceObject(frame);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation >= ValueGetString)
                typedReply = DispatchNvseValueHost(frame, waitingCall);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback && frame.Operation >= ExpressionInitialize)
                typedReply = DispatchNvseExpressionHost(frame, waitingCall);
            else if (frame.Kind == NativePluginDomainMessage.NvseCallback) value = DispatchNvseHost(frame, waitingCall);
            else if (frame.Kind == NativePluginDomainMessage.StateQuery) value = DispatchGuestStateQuery(frame, waitingCall);
            else
            {
                using var reader = Reader(frame.Payload);
                var thread = reader.ReadUInt32(); var first = reader.ReadUInt32(); var second = reader.ReadUInt32(); Finish(reader);
                if (thread != NativeThread) throw new InvalidDataException("Native callback came from an unowned thread.");
                var handler = _callback ?? throw new InvalidOperationException("A native callback has no C# runtime owner.");
                value = handler(new(Generation, frame.Id, waitingCall, frame.Operation, first, second, thread));
            }
            VerifyOwner(); _transaction!.Token.ThrowIfCancellationRequested();
            var replyKind = frame.Kind == NativePluginDomainMessage.IoCallback ? NativePluginDomainMessage.IoReply :
                frame.Kind == NativePluginDomainMessage.NvseCallback ? NativePluginDomainMessage.NvseReply :
                frame.Kind == NativePluginDomainMessage.StateQuery ? NativePluginDomainMessage.StateReply : NativePluginDomainMessage.CallbackReply;
            WriteFrame(new(replyKind, frame.Operation, Generation, frame.Id, waitingCall,
                typedReply ?? Payload(writer => writer.Write(value))));
        }
        finally { _callbackOwner = previous; }
    }

    private void VerifyModule(NativePluginModule module)
    {
        VerifyOwner();
        if (!ReferenceEquals(_module, module) || module.Generation != Generation)
            throw new InvalidOperationException("Native module belongs to a stale process generation.");
    }
    private void VerifyOwner()
    {
        VerifyThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (Fault is not null) throw FaultException();
        if (_retired) throw new ObjectDisposedException(nameof(NativePluginExecutionDomain));
    }
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native-domain requests and callbacks must remain on the creating C# thread.");
    }
    private NativePluginDomainFaultException Fatal(Exception error)
    {
        MarkFault(error, null); StopOwnedChild(); return FaultException(error);
    }
    private NativePluginDomainFaultException FaultException(Exception? inner = null) => new(Fault ??
        throw new InvalidOperationException("A native-domain fault has no retained owner."), this, inner);
    private void MarkFault(Exception error, uint? nativeCode)
    {
        lock (_faultGate)
        {
            if (_fault is null) _fault = new(Generation, ProcessId, _currentCall, _operation, error.Message, nativeCode, "", false);
            else if (error is not NativePluginDomainFaultException && error.Message != _fault.Reason)
                AppendDiagnostic($" Secondary {_operation} failure: {error.Message}");
        }
    }
    private NativePluginDomainFault? CurrentFault() => _fault is null ? null :
        _fault with { Diagnostics = _diagnostics.ToString(), DiagnosticsTruncated = _diagnosticsTruncated };
    private void StopOwnedChild()
    {
        // Process.Start retained this exact child's process handle. Never open a
        // caller-supplied PID or kill a process tree, debugger or unrelated game.
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: false);
            if (_process.WaitForExit(1000))
            {
                ObserveChildExit();
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception error) { AppendDiagnostic($" Child retirement: {error.Message}"); }
    }
    private async Task DrainDiagnostics()
    {
        var buffer = new char[1024];
        try
        {
            for (; ; )
            {
                var count = await _process.StandardError.ReadAsync(buffer).ConfigureAwait(false); if (count == 0) return;
                lock (_faultGate)
                {
                    var retained = Math.Min(count, 16384 - _diagnostics.Length);
                    if (retained > 0) _diagnostics.Append(buffer, 0, retained);
                    if (retained != count) _diagnosticsTruncated = true;
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException error) { AppendDiagnostic($" Diagnostic read failure: {error.Message}"); }
    }
    private void DrainAfterExit()
    {
        try
        {
            if (_process.WaitForExit(1000))
            {
                ObserveChildExit();
                if (!_diagnosticDrain.Wait(TimeSpan.FromSeconds(1)))
                    lock (_faultGate) { AppendDiagnostic(" Diagnostic drain did not complete."); _diagnosticsTruncated = true; }
            }
        }
        catch (InvalidOperationException) { }
        catch (AggregateException error) { AppendDiagnostic($" Diagnostic drain failure: {error.Message}"); }
    }
    private void ObserveChildExit()
    {
        ChildExited = true;
        lock (_faultGate)
        {
            ChildExitCode ??= _process.ExitCode;
            if (_fault is null || _childExitDiagnosticPublished) return;
            AppendDiagnostic($" Child exit=0x{unchecked((uint)ChildExitCode.Value):x8}.");
            _childExitDiagnosticPublished = true;
        }
    }
    private void AppendDiagnostic(string text)
    {
        lock (_faultGate)
        {
            var retained = Math.Min(text.Length, Math.Max(0, 16384 - _diagnostics.Length));
            if (retained > 0) _diagnostics.Append(text, 0, retained);
            if (retained != text.Length) _diagnosticsTruncated = true;
        }
    }
    private static void VerifyPe(string path, bool dll)
    {
        using var source = File.OpenRead(path); using var pe = new PEReader(source); VerifyPe(pe, dll);
    }
    private static void VerifyPe(PEReader pe, bool dll)
    {
        var headers = pe.PEHeaders;
        if (headers.CoffHeader.Machine != Machine.I386 || headers.PEHeader?.Magic != PEMagic.PE32 ||
            headers.CorHeader is not null || headers.CoffHeader.Characteristics.HasFlag(Characteristics.Dll) != dll)
            throw new InvalidDataException("Native execution requires an unmanaged Windows PE32/I386 image of the declared kind.");
    }
}
