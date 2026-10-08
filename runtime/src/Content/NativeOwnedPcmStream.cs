using System.Runtime.InteropServices;
using Godot;

namespace OpenNV.Runtime.Content;

internal sealed class NativeOwnedPcmStream : IDisposable
{
    private sealed class Source(float[] samples, int channels, int rate, int outputRate, FalloutSoundLoop loop,
        FalloutPcmPlaybackSnapshot? saved, bool suspended)
    {
        private readonly object _gate = new();
        internal readonly List<FalloutPcmPlayback> Playbacks = [];
        internal FalloutPcmPlayback Allocate()
        {
            lock (_gate)
            {
                var playback = new FalloutPcmPlayback(samples, channels, rate, outputRate, loop, saved);
                playback.SetSuspended(suspended);
                saved = null; Playbacks.Add(playback); return playback;
            }
        }
        internal void SetSuspended(bool value)
        {
            lock (_gate)
            {
                suspended = value;
                foreach (var playback in Playbacks) playback.SetSuspended(value);
            }
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int MixCallback(nint context, float* buffer, float rateScale, int frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double QueryCallback(nint context, int operation, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Create(nint context, nint mix, nint query, nint allocate, nint release, double seconds, int loop);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Release(ulong id);
    private static readonly unsafe MixCallback Mix = MixNative;
    private static readonly QueryCallback Query = QueryNative;
    private static readonly AllocateCallback Allocate = AllocateNative;
    private static readonly ReleaseCallback Free = FreeNative;
    private static Create? _create;
    private static Release? _release;
    private readonly Source _source;
    internal NativeOwnedPcmData Data { get; }
    internal AudioStream Stream { get; }

    internal NativeOwnedPcmStream(AudioStreamWav wav, FalloutSoundLoop loop, FalloutPcmPlaybackSnapshot? saved = null)
        : this(RequireWav(wav), loop, saved) { }

    private static NativeOwnedPcmData RequireWav(AudioStreamWav wav)
    {
        if (wav.Format != AudioStreamWav.FormatEnum.Format16Bits)
            throw new NotSupportedException("Checkpointed PCM playback currently requires decoded 16-bit WAV samples.");
        return new(wav);
    }

    internal NativeOwnedPcmStream(NativeOwnedPcmData data, FalloutSoundLoop loop, FalloutPcmPlaybackSnapshot? saved = null,
        bool suspended = false)
    {
        Data = data;
        _source = new(data.Samples, data.Channels, data.Rate, checked((int)AudioServer.GetMixRate()), loop, saved, suspended);
        // Validate before publishing a native object or callback handle.
        _ = new FalloutPcmPlayback(data.Samples, data.Channels, data.Rate, checked((int)AudioServer.GetMixRate()), loop, saved);
        EnsureLibrary();
        var context = GCHandle.Alloc(_source);
        var id = _create!(GCHandle.ToIntPtr(context), Marshal.GetFunctionPointerForDelegate(Mix),
            Marshal.GetFunctionPointerForDelegate(Query), Marshal.GetFunctionPointerForDelegate(Allocate),
            Marshal.GetFunctionPointerForDelegate(Free), (double)data.Frames / data.Rate, loop.Mode == FalloutSoundLoopMode.None ? 0 : 1);
        if (id == 0) { context.Free(); throw new InvalidOperationException("Native PCM stream construction failed."); }
        AudioStream? stream = null;
        try
        {
            Stream = stream = GodotObject.InstanceFromId(id) as AudioStream ?? throw new InvalidOperationException("Native PCM object has no AudioStream binding.");
            Stream.SetMeta("opennv_owned_media_source", data.MediaSource);
            Stream.SetMeta("opennv_owned_media_path", data.MediaPath);
            Stream.SetMeta("opennv_owned_media_sha256", data.MediaSha256);
            Stream.SetMeta("opennv_decoded_pcm_sha256", data.Sha256);
        }
        catch { stream?.Dispose(); throw; }
        finally { _release!(id); }
    }

    public void Dispose() => Stream.Dispose();
    internal void SetSuspended(bool suspended) => _source.SetSuspended(suspended);

    internal FalloutPcmPlaybackSnapshot Capture()
    {
        if (_source.Playbacks.Count != 1) throw new InvalidDataException("A source voice has no unique PCM playback owner.");
        return _source.Playbacks[0].Capture();
    }
    internal void RequireHealthy()
    {
        if (_source.Playbacks.Count != 1) throw new InvalidDataException("A source voice has no unique PCM playback owner.");
        _source.Playbacks[0].RequireHealthy();
    }
    internal void ReleaseEnvelope()
    {
        if (_source.Playbacks.Count != 1) throw new InvalidDataException("A source envelope has no unique PCM playback owner.");
        _source.Playbacks[0].ReleaseEnvelope();
    }
    private static void EnsureLibrary()
    {
        if (_create is not null) return;
        if (!ClassDB.ClassExists("OpenNVPcmStream")) throw new InvalidOperationException("The native audio extension is not loaded.");
        var candidates = new[] { Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "opennv_audio.dll"),
            ProjectSettings.GlobalizePath("res://generated/native/opennv_audio.dll") };
        nint library = 0;
        foreach (var path in candidates) if (NativeLibrary.TryLoad(path, out library)) break;
        if (library == 0) throw new DllNotFoundException("The installed native audio extension could not be opened.");
        _create = Marshal.GetDelegateForFunctionPointer<Create>(NativeLibrary.GetExport(library, "opennv_audio_create"));
        _release = Marshal.GetDelegateForFunctionPointer<Release>(NativeLibrary.GetExport(library, "opennv_audio_release"));
    }
    private static unsafe int MixNative(nint context, float* buffer, float rateScale, int frames)
    {
        var playback = (FalloutPcmPlayback)GCHandle.FromIntPtr(context).Target!;
        try { return playback.Mix(buffer, rateScale, frames); }
        catch (Exception error) { playback.Fail(error); return 0; }
    }
    private static double QueryNative(nint context, int operation, double value)
    {
        var playback = (FalloutPcmPlayback)GCHandle.FromIntPtr(context).Target!;
        try { return playback.Query(operation, value); }
        catch (Exception error) { playback.Fail(error); return 0; }
    }
    private static nint AllocateNative(nint context)
    {
        try { return GCHandle.ToIntPtr(GCHandle.Alloc(((Source)GCHandle.FromIntPtr(context).Target!).Allocate())); }
        catch (Exception) { return 0; }
    }
    private static void FreeNative(nint context) => GCHandle.FromIntPtr(context).Free();
}
