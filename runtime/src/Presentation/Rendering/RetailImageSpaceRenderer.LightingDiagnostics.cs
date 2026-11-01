using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RetailHdrCompositorEffect
{
    private readonly record struct LightingSubmission(Rid PreHdrDownsample, Rid Adaptation,
        FrameParameters Parameters, int Draw, long Ticks);
    private readonly Dictionary<uint, LightingSubmission> _lightingSubmissions = [];

    // Explicit detailed inspection only. No ordinary-frame texture readback.
    // These are the existing final scene downsample and adapted-average targets,
    // not a full scene capture or a new exposure estimate.
    internal object CaptureLightingDiagnostics(uint view = 0)
    {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                if (!Operational || Volatile.Read(ref _released) != 0 || _renderingDevice is null ||
                    !_lightingSubmissions.TryGetValue(view, out var submission))
                    throw new InvalidOperationException("No retained image-space lighting submission for this view.");
                var preHdr = ReadLightingSurface(submission.PreHdrDownsample);
                var adapted = ReadLightingSurface(submission.Adaptation);
                var parameters = submission.Parameters;
                completion.SetResult(new
                {
                    scope = "last-submitted-HDR-frame;existing-final-scene-downsample-and-adaptation;not-full-scene-color",
                    view,
                    submission.Draw,
                    inspectedDraw = Engine.GetFramesDrawn(),
                    ageSeconds = (Stopwatch.GetTimestamp() - submission.Ticks) / (double)Stopwatch.Frequency,
                    parameters.TargetLuminance,
                    cinematic = new[] { parameters.Cinematic.X, parameters.Cinematic.Y, parameters.Cinematic.Z, parameters.Cinematic.W },
                    tint = new[] { parameters.Tint.X, parameters.Tint.Y, parameters.Tint.Z, parameters.Tint.W },
                    fade = new[] { parameters.Fade.X, parameters.Fade.Y, parameters.Fade.Z, parameters.Fade.W },
                    parameters.DeltaSeconds,
                    preHdr,
                    adapted,
                });
            }
            catch (Exception error)
            {
                completion.SetResult(new { scope = "explicit-HDR-lighting-inspection-failed", error = error.Message });
            }
        }));
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(_hdr.ReadbackTimeoutSeconds)).GetAwaiter().GetResult();
    }

    private object ReadLightingSurface(Rid texture)
    {
        var device = _renderingDevice ?? throw new InvalidOperationException("Lighting rendering device retired.");
        using var format = device.TextureGetFormat(texture);
        if (format.Format != RenderingDevice.DataFormat.R16G16B16A16Sfloat ||
            format.ArrayLayers != 1 || format.Depth != 1 || format.Mipmaps != 1 ||
            format.Width == 0 || format.Height == 0 || (ulong)format.Width * format.Height > 64 ||
            (format.UsageBits & RenderingDevice.TextureUsageBits.CanCopyFromBit) == 0)
            throw new NotSupportedException("Lighting inspection requires a readable single-layer RGBA16F target of at most 64 pixels.");
        var bytes = device.TextureGetData(texture, 0);
        if (bytes.Length != checked((int)(format.Width * format.Height) * 8))
            throw new InvalidDataException("Lighting inspection target readback extent differs from its actual allocation.");
        var values = new float[bytes.Length / 2];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (float)BitConverter.UInt16BitsToHalf(
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2)));
            if (!float.IsFinite(values[index])) throw new InvalidDataException("Lighting inspection contains nonfinite GPU values.");
        }
        return new { format.Width, format.Height, format = "RGBA16F", readbackBytes = bytes.Length, rgba = values };
    }
}
