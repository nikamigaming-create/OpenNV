using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutCallingThreadFistpHost? _callingThreadFistpHost;
    private IDisposable? _callingThreadFistpLease;
    private FalloutReferenceWorld? _callingThreadFistpWorld;
    private void BindNativeCallingThreadFistp(FalloutReferenceWorld world)
    {
        if (_callingThreadFistpHost is not null || _callingThreadFistpLease is not null || _callingThreadFistpWorld is not null)
            throw new InvalidOperationException("Calling-thread FISTP already owns a current or failed native construction.");
        if (!ReferenceEquals(_nativeReferences, world)) throw new InvalidDataException("Float host binding changed the actual native campaign world.");
        var adjacent = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? throw new InvalidOperationException("Native host has no executable directory."), "opennv_audio.dll");
        var path = File.Exists(adjacent) ? adjacent : ProjectSettings.GlobalizePath("res://generated/native/opennv_audio.dll");
        _callingThreadFistpWorld = world;
        _callingThreadFistpHost = new(path);
        _callingThreadFistpHost.Initialize();
        _callingThreadFistpLease = world.BindCallingThreadFistp(_callingThreadFistpHost);
    }
    private void RetireNativeCallingThreadFistp()
    {
        // Release the actual source borrower before the native DLL load owner.
        // A failed borrower release retains both for a safe later retry.
        _callingThreadFistpLease?.Dispose(); _callingThreadFistpLease = null;
        _callingThreadFistpHost?.Dispose(); _callingThreadFistpHost = null;
        _callingThreadFistpWorld = null;
    }
}
