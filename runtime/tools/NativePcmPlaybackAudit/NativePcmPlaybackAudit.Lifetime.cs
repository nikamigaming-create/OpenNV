using System.Runtime.InteropServices;
using Godot;

namespace OpenNV.Runtime.Tools;

public partial class NativePcmPlaybackAudit
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LifetimeMixCallback(nint context, nint buffer, float rate, int frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double LifetimeQueryCallback(nint context, int operation, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint LifetimeAllocateCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LifetimeReleaseCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong LifetimeCreate(nint context, nint mix, nint query, nint allocate, nint release, double seconds, int loop);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LifetimeRelease(ulong id);

    private sealed class NativeStreamLifetimeFixture : IDisposable
    {
        internal readonly nint Context = Marshal.AllocHGlobal(1);
        internal int Releases;
        internal string? Error;
        private int _freed;

        internal void Unexpected(string operation) => Error = "Native lifetime fixture unexpectedly invoked " + operation + ".";
        internal void Release(nint context)
        {
            if (context != Context) { Error = "Native lifetime release changed its opaque source context."; return; }
            Interlocked.Increment(ref Releases);
            Dispose();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _freed, 1) == 0) Marshal.FreeHGlobal(Context);
        }
    }

    // The bridge callback slots are process-wide. Keep these delegates rooted
    // until the next ordinary PCM constructor installs its real callbacks.
    private static NativeStreamLifetimeFixture? _nativeStreamLifetimeFixture;
    private static readonly LifetimeMixCallback LifetimeMix = (_, _, _, _) =>
    {
        _nativeStreamLifetimeFixture?.Unexpected("mix"); return 0;
    };
    private static readonly LifetimeQueryCallback LifetimeQuery = (_, _, _) =>
    {
        _nativeStreamLifetimeFixture?.Unexpected("query"); return 0;
    };
    private static readonly LifetimeAllocateCallback LifetimeAllocate = _ =>
    {
        _nativeStreamLifetimeFixture?.Unexpected("playback allocation"); return 0;
    };
    private static readonly LifetimeReleaseCallback LifetimeFree = context => _nativeStreamLifetimeFixture?.Release(context);

    private static void CheckUnboundNativeStreamLifetime()
    {
        // Run first in this independent audit process, before any ordinary PCM
        // stream exists. The following real constructor restores callback slots.
        if (!ClassDB.ClassExists("OpenNVPcmStream"))
            throw new InvalidOperationException("The native audio extension is not loaded for its lifetime audit.");
        if (_nativeStreamLifetimeFixture is not null)
            throw new InvalidOperationException("Native stream lifetime audit is already active.");
        var candidates = new[] { Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "opennv_audio.dll"),
            ProjectSettings.GlobalizePath("res://generated/native/opennv_audio.dll") };
        nint library = 0;
        foreach (var path in candidates) if (NativeLibrary.TryLoad(path, out library)) break;
        if (library == 0) throw new DllNotFoundException("The installed native audio extension could not be opened for its lifetime audit.");
        try
        {
            var create = Marshal.GetDelegateForFunctionPointer<LifetimeCreate>(NativeLibrary.GetExport(library, "opennv_audio_create"));
            var release = Marshal.GetDelegateForFunctionPointer<LifetimeRelease>(NativeLibrary.GetExport(library, "opennv_audio_release"));
            using var fixture = new NativeStreamLifetimeFixture();
            _nativeStreamLifetimeFixture = fixture;
            ulong id = 0;
            var constructionReleased = false;
            try
            {
                id = create(fixture.Context, Marshal.GetFunctionPointerForDelegate(LifetimeMix),
                    Marshal.GetFunctionPointerForDelegate(LifetimeQuery), Marshal.GetFunctionPointerForDelegate(LifetimeAllocate),
                    Marshal.GetFunctionPointerForDelegate(LifetimeFree), 1, 0);
                // C# IsInstanceIdValid calls InstanceFromId and adds a managed
                // binding while the object lives. Keep construction unbound.
                if (id == 0)
                    throw new InvalidDataException("The native lifetime fixture did not return a source-stream ID.");
                if (fixture.Releases != 0 || fixture.Error is not null)
                    throw new InvalidDataException("Native stream construction invoked an unexpected lifetime callback: " + fixture.Error);
                constructionReleased = true;
                release(id);
                // The native free_instance callback must establish retirement
                // before an ID lookup can safely avoid creating a binding.
                if (fixture.Releases != 1 || fixture.Error is not null)
                    throw new InvalidDataException($"Unbound native stream retirement failed: contextReleases={fixture.Releases} nativeObjectLive=unobserved callbackError={fixture.Error}.");
                if (GodotObject.IsInstanceIdValid(id))
                    throw new InvalidDataException("The retired native source stream still has a live object ID.");
                // The public release entry point resolves IDs before touching
                // an object. Repeat only after deletion has been established.
                release(id);
                if (fixture.Releases != 1 || fixture.Error is not null || GodotObject.IsInstanceIdValid(id))
                    throw new InvalidDataException("Repeated release of the deleted native stream changed source-context ownership.");
            }
            finally
            {
                try { if (id != 0 && !constructionReleased) release(id); }
                finally { _nativeStreamLifetimeFixture = null; }
            }
        }
        finally { NativeLibrary.Free(library); }
        GD.Print("OPENNV_NATIVE_PCM_LIFETIME_PASS constructionReference=true managedBinding=false contextReleases=1 nativeObjectDeleted=true repeatedRelease=true");
    }
}
