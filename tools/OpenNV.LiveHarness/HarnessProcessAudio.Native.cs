using System.Runtime.InteropServices;

namespace OpenNV.LiveHarness;

public static partial class HarnessProcessAudio
{
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string device, in Guid iid, in PropVariant parameters,
        IActivationCompleted completion, out IActivationOperation operation);
    [DllImport("Ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateFreeThreadedMarshaler(IntPtr outer, out IntPtr marshaler);
    [DllImport("Ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("Ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { internal int Length; internal IntPtr Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal Blob Blob;
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivationOperation
    {
        [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivationCompleted
    {
        [PreserveSig] int ActivateCompleted(IActivationOperation operation);
    }

    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Activation : IActivationCompleted, IAgileObject, ICustomQueryInterface, IDisposable
    {
        private IntPtr _marshaler;
        internal Activation() => Check(CoCreateFreeThreadedMarshaler(IntPtr.Zero, out _marshaler));
        internal readonly TaskCompletionSource<object> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            if (iid != new Guid("00000003-0000-0000-C000-000000000046")) return CustomQueryInterfaceResult.NotHandled;
            return Marshal.QueryInterface(_marshaler, ref iid, out instance) == 0
                ? CustomQueryInterfaceResult.Handled : CustomQueryInterfaceResult.Failed;
        }
        public void Dispose() { if (_marshaler != IntPtr.Zero) { Marshal.Release(_marshaler); _marshaler = IntPtr.Zero; } }
        public int ActivateCompleted(IActivationOperation operation)
        {
            try
            {
                Check(operation.GetActivateResult(out var result, out var instance));
                Check(result); Completion.TrySetResult(instance);
            }
            catch (Exception error) { Completion.TrySetException(error); }
            return 0;
        }
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int sharedMode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int sharedMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr bytes, out uint frames, out uint flags, out ulong devicePosition, out ulong qpc100Nanoseconds);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
}
