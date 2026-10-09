namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private sealed record WindowProcessCall(ulong Id, uint Api, uint Handle, uint First, uint Second, uint Third, uint Count);
    private sealed record WindowProcessHandle(uint Api, uint Handle, NativePluginKernelHandle Guard);
    private WindowProcessCall? _windowProcessPending;
    private ulong _windowProcessSequence;
    private readonly Dictionary<uint, WindowProcessHandle> _windowProcessHandles = [];
    private readonly List<NativePluginProcessReceipt> _windowProcessReceipts = [];
    internal IReadOnlyList<NativePluginProcessReceipt> NvseProcessReceipts => _windowProcessReceipts.AsReadOnly();
    private byte[] DispatchWindowProcessSdk(ulong parent, uint thread, ulong module, BinaryReader reader)
    {
        var window = _productWindow; var stage = reader.ReadUInt32();
        if (stage == 1)
        {
            var api = reader.ReadUInt32(); var handle = reader.ReadUInt32(); var first = reader.ReadUInt32();
            var second = reader.ReadUInt32(); var third = reader.ReadUInt32(); var count = reader.ReadUInt32(); Finish(reader);
            if (_windowProcessPending is not null || _windowNativeRetired || count > MaximumPayload - 256)
                throw new InvalidOperationException("Process SDK call repeats a pending/retired caller or exceeds its bounded actual buffer.");
            switch (api)
            {
                case 1: // OpenThread: actual query access, actual SDK can deny it.
                    if (handle != 0 || (first & ~0x00100840U) != 0 || second != 0 || (third != thread && (window is null || third != window.Thread)) || count != 0)
                        throw new NotSupportedException("OpenThread has unowned access/inheritance/foreign-thread semantics.");
                    break;
                case 2: // GetProcessTimes: child process pseudo handle only.
                    if (handle != uint.MaxValue || (first | second | third) != 0 || count != 32)
                        throw new NotSupportedException("Process times require the actual x86 child process and exact FILETIME outputs.");
                    break;
                case 3: // ReadProcessMemory: bounded real child memory, no retail/PID projection.
                    if (handle != uint.MaxValue || third > 1 || second != count || (ulong)first + count > 1UL << 32)
                        throw new NotSupportedException("Process memory query lacks the actual child/bounded x86 source address.");
                    break;
                case 4: // Toolhelp32 module snapshot.
                    if (handle != 0 || (first & ~0x18U) != 0 || (first & 0x18U) == 0 ||
                        (second != 0 && second != (uint)ProcessId && (window is null || second != window.Process)) || third != 0 || count != 0)
                        throw new NotSupportedException("Toolhelp requests an unowned process/global/heap/thread enumeration.");
                    break;
                case 5 or 6: // Actual MODULEENTRY32W.
                    RequireWindowProcessHandle(handle, 4);
                    if ((first | second | third) != 0 || count != 1064) throw new InvalidDataException("Module entry lacks the complete x86 public SDK declaration.");
                    break;
                case 7:
                    RequireWindowProcessHandle(handle);
                    if ((first | second | third | count) != 0) throw new InvalidDataException("Query-handle close has extra public arguments.");
                    break;
                default: throw new NotSupportedException("Process API has no exact source-owned SDK producer.");
            }
            var id = checked(++_windowProcessSequence); _windowProcessPending = new(id, api, handle, first, second, third, count);
            return Payload(writer => writer.Write(id));
        }
        if (stage != 2 || _windowProcessPending is not { } pending || reader.ReadUInt64() != pending.Id)
            throw new InvalidDataException("Process SDK completion has no actual entered caller prefix.");
        var result = reader.ReadInt64(); var error = reader.ReadUInt32(); var handleResult = reader.ReadUInt32();
        var countResult = reader.ReadUInt32(); Finish(reader);
        if (result != 0 && countResult > pending.Count || result < int.MinValue || result > uint.MaxValue)
            throw new InvalidDataException("Process SDK completion changed its public scalar/output extent.");
        if (pending.Api is 1 or 4)
        {
            var success = pending.Api == 1 ? result != 0 : unchecked((uint)result) != uint.MaxValue;
            if (handleResult != unchecked((uint)result) || countResult != 0 || success && (handleResult is 0 or uint.MaxValue))
                throw new InvalidDataException("Actual process creation result and retained kernel handle differ.");
            if (success)
            {
                var guard = _process.RetainWindowProcessReference(handleResult);
                // Keep the acquired reference even if native handle reuse/duplicate
                // publication fails: child closure owns the next cleanup attempt.
                if (!_windowProcessHandles.TryAdd(handleResult, new(pending.Api, handleResult, guard)))
                { _process.RetainFailedWindowProcessReference(guard); throw new InvalidDataException("Process SDK repeats a live native handle."); }
            }
        }
        else
        {
            if (handleResult != pending.Handle) throw new InvalidDataException("Process SDK result changed its actual caller handle.");
            if (pending.Api == 7 && result != 0)
            {
                var owned = _windowProcessHandles[pending.Handle];
                _process.CloseWindowProcessReference(owned.Guard); _windowProcessHandles.Remove(pending.Handle);
            }
        }
        _windowProcessReceipts.Add(new(Generation, module, thread, parent, pending.Api, handleResult, result, error, countResult,
            pending.Api == 4 ? pending.Second == 0 ? (uint)ProcessId : pending.Second : (uint)ProcessId,
            pending.Api == 1 ? pending.Third : thread, pending.Api != 3 || pending.Third == 1));
        _windowProcessPending = null;
        return Payload(writer => writer.Write(1U));
    }
    private void RequireWindowProcessHandle(uint handle, uint? kind = null)
    {
        if (!_windowProcessHandles.TryGetValue(handle, out var owned) || kind is { } expected && owned.Api != expected)
            throw new NotSupportedException("Process query targets a foreign/closed/other-kind kernel handle.");
        _process.RequireWindowProcessIdentity(handle, owned.Guard);
    }
}
