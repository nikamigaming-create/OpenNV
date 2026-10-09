using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// The owner is published before Initialize, so a failed export/construction
// still has a concrete DLL reference which the campaign can retire separately.
internal sealed class FalloutCallingThreadFistpHost : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NativeResult
    {
        internal uint Abi, Bytes, Process, Thread, Input;
        internal int Value;
        internal ushort ControlBefore, StatusBefore, ControlAfter, StatusAfter;
        internal byte TagBefore, TagAfter, Outcome, Operation;
        internal uint Reserved;
        internal uint Argument;
        internal ushort StatusAtConversion, ReservedTail;
        internal readonly FalloutFistpObservation Read() => new(Abi, Bytes, Process, Thread, Input, Value,
            ControlBefore, StatusBefore, ControlAfter, StatusAfter, TagBefore, TagAfter,
            (FalloutFistpOutcome)Outcome, (FalloutFistpOperation)Operation, Reserved, Argument, StatusAtConversion, ReservedTail);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Call(uint operation, uint input, ref NativeResult result, uint bytes);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ModulePath(nint module, [Out] char[] path, uint characters);
    private readonly string _path;
    private readonly Guid _identity = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private nint _library;
    private Call? _call;
    private FalloutFistpHostBinding? _binding;
    private bool _entered, _disposed;
    private string? _failure;
    internal FalloutFistpObservation? LastObservation { get; private set; }
    internal long ObservationOrdinal { get; private set; }
    internal FalloutFistpHostBinding Binding => _binding ?? throw new NotSupportedException("Actual calling-thread float host construction has not returned.");
    internal object State => new { identity = _identity, path = _path, binding = _binding, entered = _entered, failure = _failure, retired = _disposed, last = LastObservation };

    internal FalloutCallingThreadFistpHost(string adapterPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterPath);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Calling-thread FISTP currently requires the actual Windows AMD64 host ABI.");
        _path = Path.GetFullPath(adapterPath);
    }
    internal void Initialize()
    {
        RequireThread();
        if (_library != 0 || _binding is not null || _failure is not null) throw new InvalidOperationException("Float host construction cannot replay its entered prefix.");
        try
        {
            _library = NativeLibrary.Load(_path);
            var characters = new char[32768];
            var count = ModulePath(_library, characters, (uint)characters.Length);
            if (count == 0 || count >= characters.Length) throw new InvalidDataException("Float host cannot identify its actual loaded native image.");
            var loaded = Path.GetFullPath(new string(characters, 0, checked((int)count)));
            if (!StringComparer.OrdinalIgnoreCase.Equals(loaded, _path)) throw new InvalidDataException("Float host resolved a different native DLL image.");
            var digest = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loaded))).ToLowerInvariant();
            _call = Marshal.GetDelegateForFunctionPointer<Call>(NativeLibrary.GetExport(_library, "opennv_host_fistp32"));
            if (Marshal.SizeOf<NativeResult>() != FalloutFistp32Abi.Bytes) throw new InvalidDataException("Managed/native FISTP ABI extent differs.");
            var observed = Invoke(FalloutFistpOperation.Observe, 0);
            FalloutFistp32Abi.RequireHeader(observed, FalloutFistpOperation.Observe, 0);
            if (observed.NativeProcess != (uint)Environment.ProcessId) throw new InvalidDataException("Float host observation came from another native process.");
            _binding = new(_identity, digest, _thread, observed);
            FalloutFistp32Abi.RequireBinding(_binding);
        }
        catch (Exception error) { _failure ??= Error(error); throw; }
    }
    internal FalloutFistpObservation Convert(uint bits) => Convert(bits, FalloutFistpOperation.SourceArgument);
    internal FalloutFistpObservation Convert(uint bits, FalloutFistpOperation operation)
    {
        if (operation is not (FalloutFistpOperation.Convert or FalloutFistpOperation.SourceArgument))
            throw new InvalidDataException("Actual FISTP requires a selected original conversion transport.");
        RequireThread(); var binding = Binding;
        // Verify the actual OS thread before executing an instruction. The
        // managed thread check also refuses a cross-thread call without touching
        // that thread's native FPU. Neither check substitutes a domain worker.
        var observed = Invoke(FalloutFistpOperation.Observe, 0);
        FalloutFistp32Abi.RequireHeader(observed, FalloutFistpOperation.Observe, 0);
        RequireNativeThread(observed, binding);
        var converted = Invoke(operation, bits);
        RequireNativeThread(converted, binding);
        return converted;
    }
    internal void RequireCurrent(FalloutFistpHostBinding binding)
    {
        RequireThread();
        if (!ReferenceEquals(_binding, binding)) throw new InvalidOperationException("Float host lease belongs to another actual native construction.");
    }
    private FalloutFistpObservation Invoke(FalloutFistpOperation operation, uint bits)
    {
        RequireThread();
        if (_entered) throw new InvalidOperationException("Actual calling-thread x87 bridge is already entered.");
        _entered = true;
        try
        {
            var result = default(NativeResult);
            var status = (_call ?? throw new NotSupportedException("Calling-thread native FISTP export is absent."))((uint)operation, bits, ref result, FalloutFistp32Abi.Bytes);
            var observation = result.Read(); LastObservation = observation;
            ObservationOrdinal = checked(ObservationOrdinal + 1);
            if (status != 0) throw new InvalidDataException("Calling-thread FISTP ABI rejected the actual request: " + status);
            FalloutFistp32Abi.RequireHeader(observation, operation, bits);
            return observation;
        }
        finally { _entered = false; }
    }
    private static void RequireNativeThread(FalloutFistpObservation value, FalloutFistpHostBinding binding)
    {
        if (value.NativeProcess != binding.Construction.NativeProcess || value.NativeThread != binding.Construction.NativeThread)
            throw new InvalidDataException("Calling-thread conversion left its real process/native-thread construction.");
    }
    private void RequireThread()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Calling-thread float host was used outside its actual presentation-thread lease.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        RequireThread();
        if (_entered) throw new InvalidOperationException("Float host retirement cannot interrupt a live native instruction.");
        // NativeLibrary.Free releases only this explicit load reference. The
        // existing Godot/audio extension owns its own independent DLL reference.
        if (_library != 0) { NativeLibrary.Free(_library); _library = 0; }
        _call = null; _disposed = true;
    }
    private static string Error(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
}
