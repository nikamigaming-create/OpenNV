using System.Diagnostics;
using System.Text;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed record ImageSpaceTracePass(int Ordinal, uint View, int DrawCount, long Nanoseconds,
    ulong SourceZero, ulong SourceOne, ulong Destination, byte[] PushConstants, bool Submitted);
internal sealed record ImageSpaceTraceSurface(ulong Resource, int LastWriter, int Width, int Height,
    int Mipmaps, uint UsageBits, string Format, byte[] Pixels, string? Error);
internal sealed record ImageSpaceRenderTrace(ulong Request, long BeginNanoseconds, long EndNanoseconds,
    string SourcePrograms, byte[] ComputeSource, IReadOnlyList<ImageSpaceTracePass> Passes,
    IReadOnlyList<ImageSpaceTraceSurface> Surfaces);

internal partial class RetailHdrCompositorEffect
{
    private sealed class TraceRequest(ulong request)
    {
        internal ulong Request { get; } = request;
        internal List<TracePass> Passes { get; } = [];
    }

    private sealed class TracePass(ImageSpaceTracePass data, Rid destination)
    {
        internal ImageSpaceTracePass Data { get; private set; } = data;
        internal Rid Destination { get; } = destination;
        internal void Submitted() => Data = Data with { Submitted = true };
    }

    private TraceRequest? _traceRequest;
    private uint _traceView;

    internal void BeginRenderTrace(ulong request) => Volatile.Write(ref _traceRequest, new(request));
    internal void CancelRenderTrace() => Volatile.Write(ref _traceRequest, null);

    private TracePass? ObservePass(Rid sourceZero, Rid sourceOne, Rid destination, byte[] constants)
    {
        var request = Volatile.Read(ref _traceRequest);
        if (request is null) return null;
        var pass = new TracePass(new(request.Passes.Count, _traceView, Engine.GetFramesDrawn(), TraceNanoseconds(),
            sourceZero.Id, sourceOne.Id, destination.Id, constants, false), destination);
        request.Passes.Add(pass);
        return pass;
    }

    // Called only for an explicit trace, after drawing. Read all retained pass
    // destinations together on the render thread. Inputs overwritten by later
    // passes are identified by the surface's last writer, never labelled as
    // captured input pixels. Ordinary frames perform no GPU readback.
    internal ImageSpaceRenderTrace CaptureRenderTrace(ulong request)
    {
        var completed = new TaskCompletionSource<ImageSpaceRenderTrace>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                var trace = Volatile.Read(ref _traceRequest);
                if (trace?.Request != request || trace.Passes.Count == 0 || !Operational || _renderingDevice is null)
                    throw new InvalidOperationException("Image-space trace has no completed compositor submission.");
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _traceRequest, null, trace), trace))
                    throw new InvalidOperationException("Image-space trace was cancelled before readback.");
                if (trace.Passes.Any(pass => !pass.Data.Submitted))
                    throw new InvalidOperationException("Image-space trace contains an unsubmitted pass.");
                var begin = TraceNanoseconds();
                var surfaces = new List<ImageSpaceTraceSurface>();
                foreach (var writes in trace.Passes.GroupBy(pass => pass.Destination.Id))
                {
                    var last = writes.Last();
                    using var format = _renderingDevice.TextureGetFormat(last.Destination);
                    if (format.Format != RenderingDevice.DataFormat.R16G16B16A16Sfloat ||
                        format.ArrayLayers != 1 || format.Depth != 1)
                        throw new NotSupportedException("Image-space trace destination format or layer extent is unbound.");
                    var readable = (format.UsageBits & RenderingDevice.TextureUsageBits.CanCopyFromBit) != 0;
                    var pixels = readable ? _renderingDevice.TextureGetData(last.Destination, 0) : [];
                    var expectedBytes = 0;
                    for (var mip = 0; mip < format.Mipmaps; mip++)
                        expectedBytes = checked(expectedBytes + Math.Max(1, (int)format.Width >> mip) *
                            Math.Max(1, (int)format.Height >> mip) * 8);
                    var error = !readable ? "GPU destination was not allocated for readback; viewport output is a separate evidence lane." :
                        pixels.Length == expectedBytes ? null :
                        $"GPU destination {last.Destination.Id} last written by pass {last.Data.Ordinal}: " +
                        $"read {pixels.Length} bytes, allocation {format.Width}x{format.Height}, " +
                        $"{format.Mipmaps} mip levels requires {expectedBytes}.";
                    surfaces.Add(new(last.Destination.Id, last.Data.Ordinal, (int)format.Width, (int)format.Height,
                        (int)format.Mipmaps, (uint)format.UsageBits, "R16G16B16A16_SFLOAT-little-endian", pixels, error));
                }
                completed.SetResult(new(request, begin, TraceNanoseconds(), SourceProgramIdentity,
                    Encoding.UTF8.GetBytes(_computeShaderSource), trace.Passes.Select(pass => pass.Data).ToArray(), surfaces));
            }
            catch (Exception exception) { completed.SetException(exception); }
        }));
        // A late render callback can safely complete after a timeout; it never
        // touches a disposed wait handle or changes gameplay/render settings.
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(_hdr.ReadbackTimeoutSeconds)).GetAwaiter().GetResult();
    }

    private static long TraceNanoseconds() => checked((long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)));
}
