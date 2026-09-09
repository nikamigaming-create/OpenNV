using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.LiveHarness;

// Windows process loopback observes only the explicitly selected process tree.
// It neither opens a microphone nor modifies or injects the target process.
public static partial class HarnessProcessAudio
{
    internal static object Capture(int processId, int milliseconds, string directory)
    {
        if (milliseconds is < 100 or > 60000 || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Audio capture requires 100-60000 milliseconds and a new absolute private directory.");
        using var target = Process.GetProcessById(processId);
        var startTime = target.StartTime.ToUniversalTime();
        var owner = new TemporaryCaptureDirectory(directory);
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IActivationOperation? operation = null;
        var activationBytes = Marshal.AllocCoTaskMem(12);
        var formatPointer = IntPtr.Zero;
        var started = false;
        var initializedCom = false;
        Activation? activation = null;
        try
        {
            Check(CoInitializeEx(IntPtr.Zero, 0)); initializedCom = true;
            Marshal.WriteInt32(activationBytes, 0, 1); // Process loopback activation.
            Marshal.WriteInt32(activationBytes, 4, processId);
            Marshal.WriteInt32(activationBytes, 8, 0); // Include only this process tree.
            var parameters = new PropVariant { Type = 65, Blob = new Blob { Length = 12, Data = activationBytes } };
            activation = new Activation();
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, parameters, activation, out operation));
            try { client = (IAudioClient)activation.Completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
            catch (TimeoutException)
            {
                // Windows has no cancellation operation here. Its asynchronous
                // callback and activation parameters must outlive our timeout.
                var pendingActivation = activation;
                var pendingOperation = operation;
                var pendingBytes = activationBytes;
                _ = pendingActivation.Completion.Task.ContinueWith(completed =>
                {
                    if (completed.IsCompletedSuccessfully) Marshal.FinalReleaseComObject(completed.Result);
                    else _ = completed.Exception;
                    Marshal.FinalReleaseComObject(pendingOperation);
                    pendingActivation.Dispose();
                    Marshal.FreeCoTaskMem(pendingBytes);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                activation = null; operation = null; activationBytes = IntPtr.Zero;
                throw;
            }
            var mixResult = client.GetMixFormat(out formatPointer);
            var formatOrigin = "reported-process-loopback-mix-format";
            uint flags = 0x00060000; // Loopback and event callback.
            if (mixResult == unchecked((int)0x80004001))
            {
                // Some Windows virtual loopback clients do not expose a native
                // mix format. Keep that explicit instead of claiming device bits.
                if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
                formatPointer = Marshal.AllocCoTaskMem(18);
                var requested = new byte[18];
                BinaryPrimitives.WriteUInt16LittleEndian(requested, 3); // IEEE float.
                BinaryPrimitives.WriteUInt16LittleEndian(requested.AsSpan(2), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(requested.AsSpan(4), 48000);
                BinaryPrimitives.WriteUInt32LittleEndian(requested.AsSpan(8), 384000);
                BinaryPrimitives.WriteUInt16LittleEndian(requested.AsSpan(12), 8);
                BinaryPrimitives.WriteUInt16LittleEndian(requested.AsSpan(14), 32);
                Marshal.Copy(requested, 0, formatPointer, requested.Length);
                flags |= 0x80000000; // Windows performs any required conversion.
                formatOrigin = "requested-stereo-float32-48000;native-format-query-not-implemented;Windows-conversion-possible";
            }
            else Check(mixResult);
            var extra = (ushort)Marshal.ReadInt16(formatPointer, 16);
            var format = new byte[18 + extra]; Marshal.Copy(formatPointer, format, 0, format.Length);
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2));
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4));
            var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12));
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14));
            if (channels == 0 || rate == 0 || blockAlign == 0) throw new InvalidDataException("Audio client returned an invalid format.");
            Check(client.Initialize(0, flags, 0, 0, formatPointer, IntPtr.Zero));
            Check(client.GetService(typeof(IAudioCaptureClient).GUID, out var service));
            capture = (IAudioCaptureClient)service;
            using var ready = new AutoResetEvent(false);
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            var packets = new List<AudioPacket>();
            var gaps = new List<string>();
            var rawPath = Path.Combine(owner.Path, "audio.samples");
            var begin = Stopwatch.GetTimestamp();
            using (var raw = new FileStream(rawPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                Check(client.Start()); started = true;
                while (Stopwatch.GetElapsedTime(begin).TotalMilliseconds < milliseconds)
                {
                    ready.WaitOne(25);
                    Drain();
                }
                Check(client.Stop()); started = false;
                Drain();

                void Drain()
                {
                    Check(capture.GetNextPacketSize(out var available));
                    while (available != 0)
                    {
                        Check(capture.GetBuffer(out var data, out var frames, out var bufferFlags, out var position, out var qpc));
                        try
                        {
                            if (frames != available) gaps.Add($"Packet availability changed: {available} to {frames}.");
                            if ((bufferFlags & 1) != 0) gaps.Add($"Discontinuity at device frame {position}.");
                            if ((bufferFlags & 4) != 0) gaps.Add($"Timestamp error at device frame {position}.");
                            var bytes = new byte[checked((int)frames * blockAlign)];
                            var silent = (bufferFlags & 2) != 0;
                            if (!silent) Marshal.Copy(data, bytes, 0, bytes.Length);
                            packets.Add(new(packets.Count + 1, raw.Position, bytes.Length, frames, position, qpc, bufferFlags,
                                silent ? "API-declared-silence-expanded-to-zero-samples" : "unchanged-API-packet-bytes"));
                            raw.Write(bytes);
                        }
                        finally { Check(capture.ReleaseBuffer(frames)); }
                        Check(capture.GetNextPacketSize(out available));
                    }
                }
            }
            if (packets.Count == 0) gaps.Add("No audio packets observed; silence is not inferred.");
            var deviceCounter = packets.Any(packet => packet.DevicePosition != 0);
            var qpcClock = packets.Count != 0 && packets.All(packet => packet.Qpc100Nanoseconds != 0 && (packet.Flags & 4) == 0);
            foreach (var (previous, next) in packets.Zip(packets.Skip(1)))
            {
                if (deviceCounter && next.DevicePosition != previous.DevicePosition + previous.Frames)
                    gaps.Add($"Device frame gap at packet {next.Ordinal}.");
                if (qpcClock && Math.Abs((decimal)next.Qpc100Nanoseconds - previous.Qpc100Nanoseconds -
                    (decimal)previous.Frames * 10_000_000 / rate) > 1)
                    gaps.Add($"Audio timestamp discontinuity at packet {next.Ordinal}.");
            }
            if (target.HasExited || target.StartTime.ToUniversalTime() != startTime) gaps.Add("Target process lifetime changed during capture.");
            var wave = Path.Combine(owner.Path, "audio.wav");
            WriteWave(rawPath, wave, format);
            using var input = File.OpenRead(rawPath);
            var report = new
            {
                schema = "opennv-process-audio-observation/v1",
                processId,
                startTime,
                processTree = "included-target-only",
                microphone = false,
                beginQpc = begin,
                endQpc = Stopwatch.GetTimestamp(),
                qpcFrequency = Stopwatch.Frequency,
                formatOrigin,
                formatHex = Convert.ToHexString(format),
                channels,
                rate,
                bits,
                blockAlign,
                raw = rawPath,
                wave,
                bytes = input.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(),
                packets,
                gaps,
                deviceCounter = deviceCounter ? "observed" : "unavailable-all-zero;sample-counter-continuity-unverified",
                packetClock = qpcClock ? "API-QPC-timestamps-observed" : "unavailable-or-error",
                coverage = "Windows-application-render-mix;physical-endpoint-effects-and-per-source-sample-contributors-unobserved",
                frameJoin = "unobserved",
                parity = "unverified",
            };
            File.WriteAllText(Path.Combine(owner.Path, "audio.json"), JsonSerializer.Serialize(report, Program.Json));
            return new
            {
                directory = owner.Path,
                packets = packets.Count,
                bytes = input.Length,
                gaps = gaps.Count,
                deviceCounter,
                qpcClock,
                formatOrigin,
                parity = "unverified"
            };
        }
        catch { owner.Dispose(); throw; }
        finally
        {
            if (started) client?.Stop();
            if (capture is not null) Marshal.FinalReleaseComObject(capture);
            if (client is not null) Marshal.FinalReleaseComObject(client);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
            activation?.Dispose();
            Marshal.FreeCoTaskMem(activationBytes);
            if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
            if (initializedCom) CoUninitialize();
        }
    }

    private sealed record AudioPacket(int Ordinal, long Offset, int Bytes, uint Frames, ulong DevicePosition,
        ulong Qpc100Nanoseconds, uint Flags, string BytesOrigin);

    private static void WriteWave(string raw, string destination, byte[] format)
    {
        using var input = File.OpenRead(raw);
        using var output = new BinaryWriter(new FileStream(destination, FileMode.CreateNew, FileAccess.Write));
        output.Write(Encoding.ASCII.GetBytes("RIFF")); output.Write(checked((uint)(input.Length + 20 + format.Length)));
        output.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); output.Write(format.Length); output.Write(format);
        output.Write(Encoding.ASCII.GetBytes("data")); output.Write(checked((uint)input.Length)); input.CopyTo(output.BaseStream);
    }
}
