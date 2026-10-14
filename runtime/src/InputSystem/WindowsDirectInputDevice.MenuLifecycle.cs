using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.InputSystem;

internal sealed partial class WindowsDirectInputDevice
{
    private readonly Guid _menuMouseDeviceFactory = Guid.NewGuid();

    internal FalloutOriginalMouseBufferFlush FlushSourceMenuMouse(FalloutStandaloneMenuFieldInvocation invocation)
    {
        RequireOwner(); invocation.Require(FalloutStandaloneMenuFieldStep.QuietReturn);
        var owner = _sourceOwner ?? throw new InvalidOperationException("Menu flush has no actual source input-device binding.");
        RequireCurrent(owner);
        if (owner.Source != invocation.Stack) throw new InvalidDataException("Menu flush borrowed another current native device stack.");
        if (_mouse == 0) return new(invocation.Identity, invocation.Process, invocation.Object, owner.Factory,
            owner.Source, null, false, 20, null, null, null, null, true);
        uint count = uint.MaxValue;
        // Source x86 object data has twenty bytes. The fresh native x64 COM
        // interface uses the host public ABI with its pointer-sized app data.
        var extent = Marshal.SizeOf<BufferedMouseObjectData>();
        var status = Function<GetBufferedDeviceData>(_mouse, 10)(_mouse, checked((uint)extent), 0, ref count, 0);
        return new(invocation.Identity, invocation.Process, invocation.Object, owner.Factory, owner.Source,
            _menuMouseDeviceFactory, true, 20, extent, uint.MaxValue, count, status, true);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BufferedMouseObjectData
    {
        internal uint Offset, Data, Timestamp, Sequence;
        internal nuint ApplicationData;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferedDeviceData(nint instance, uint objectExtent, nint destination, ref uint count, uint flags);
}
