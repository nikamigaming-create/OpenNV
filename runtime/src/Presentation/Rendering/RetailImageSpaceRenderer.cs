using System.Threading;
using System.Globalization;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class RetailImageSpaceRenderer
{
    internal static Application Apply(
        WorldEnvironment environment,
        RetailImageSpaceComposition.ComposedImageSpace imageSpace,
        RetailImageSpaceConfiguration configuration,
        CaptureConfiguration capture,
        ColorTransferConfiguration outputTransfer,
        bool runtimeAdaptation = false)
    {
        var application = Create(
            imageSpace,
            configuration,
            capture,
            outputTransfer,
            runtimeAdaptation);
        environment.Compositor = application.Compositor;
        return application;
    }

    internal static Application Create(
        RetailImageSpaceComposition.ComposedImageSpace imageSpace,
        RetailImageSpaceConfiguration configuration,
        CaptureConfiguration capture,
        ColorTransferConfiguration outputTransfer,
        bool runtimeAdaptation)
    {
        var indices = configuration.TraitIndices;
        var cinematic = new Vector4(
            imageSpace.Traits[indices.CinematicSaturation],
            imageSpace.Traits[indices.CinematicContrastAverageLuminance],
            imageSpace.Traits[indices.CinematicContrast],
            imageSpace.Traits[indices.CinematicBrightness]);
        var effect = new RetailHdrCompositorEffect(
            imageSpace.Traits[indices.TargetLuminance],
            imageSpace.MatchedAdaptationSum,
            cinematic,
            imageSpace.Tint,
            imageSpace.Fade,
            configuration,
            capture,
            outputTransfer,
            runtimeAdaptation);
        var compositor = new Compositor
        {
            CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect },
        };
        return new Application(
            configuration.Schema,
            cinematic,
            imageSpace.Tint,
            imageSpace.Fade,
            imageSpace.MatchedAdaptationSum,
            imageSpace.MatchedAdaptationSourceSha256,
            imageSpace.AppliedModifiers,
            effect,
            compositor);
    }

    internal sealed record Application(
        string Schema,
        Vector4 Cinematic,
        Vector4 Tint,
        Vector4 Fade,
        float MatchedAdaptationSum,
        string MatchedAdaptationSourceSha256,
        IReadOnlyList<RetailImageSpaceComposition.AppliedModifier> AppliedModifiers,
        RetailHdrCompositorEffect Effect,
        Compositor Compositor)
    {
        internal bool FinalCinematicStageResolved => Effect.Operational;

        internal bool HdrAdaptationBrightPassBloomResolved => Effect.Operational;
    }
}

internal partial class RetailHdrCompositorEffect : CompositorEffect
{
    private const int CopyPass = 0;
    private const int DownsamplePass = 1;
    private const int AdaptationPass = 2;
    private const int VerticalBlurPass = 3;
    private const int HorizontalBlurPass = 4;
    private const int HdrCombinePass = 5;
    private const int OutputTransferPass = 6;
    private const uint TextureUsage =
        (uint)(RenderingDevice.TextureUsageBits.SamplingBit |
            RenderingDevice.TextureUsageBits.StorageBit |
            RenderingDevice.TextureUsageBits.CanCopyFromBit |
            RenderingDevice.TextureUsageBits.CpuReadBit);

    private static readonly StringName TextureContext = new("opennv_retail_hdr");

    private const string ComputeShaderTemplate = """
        #version 450

        layout(local_size_x = __WORK_GROUP_SIDE__, local_size_y = __WORK_GROUP_SIDE__, local_size_z = 1) in;

        layout(set = 0, binding = 0) uniform sampler2D source_zero;
        layout(set = 0, binding = 1) uniform sampler2D source_one;
        layout(rgba16f, set = 0, binding = 2) uniform writeonly image2D destination_image;

        layout(push_constant, std430) uniform Params {
            vec4 dimensions;
            vec4 controls;
            vec4 cinematic;
            vec4 tint;
            vec4 fade;
            vec4 hdr_state;
        } params;

        const float blur_weights[__BLUR_WEIGHT_COUNT__] = float[](
            __BLUR_WEIGHTS__);

        vec4 sampled(sampler2D source, vec2 uv) {
            return textureLod(source, uv, 0.0);
        }

        vec3 encoded_to_linear(vec3 encoded) {
            vec3 low = encoded / __TRANSFER_LINEAR_SCALE__;
            vec3 high = pow(
                (encoded + __TRANSFER_OFFSET__) / __TRANSFER_NORMALIZATION__,
                vec3(__TRANSFER_EXPONENT__));
            return mix(low, high, step(vec3(__TRANSFER_ENCODED_CUTOFF__), encoded));
        }

        vec3 four_tap_downsample(vec2 uv, vec2 source_size) {
            vec2 texel = vec2(1.0) / source_size;
            return (
                sampled(source_zero, uv + vec2(-texel.x, -texel.y)).rgb +
                sampled(source_zero, uv + vec2( texel.x, -texel.y)).rgb +
                sampled(source_zero, uv + vec2( texel.x,  texel.y)).rgb +
                sampled(source_zero, uv + vec2(-texel.x,  texel.y)).rgb) * 0.25;
        }

        void main() {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
            ivec2 destination_size = ivec2(params.dimensions.xy);
            if (pixel.x >= destination_size.x || pixel.y >= destination_size.y) {
                return;
            }

            vec2 source_size = params.dimensions.zw;
            vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(destination_size);
            int pass = int(params.controls.x + 0.5);
            vec4 result = vec4(0.0);

            if (pass == __COPY_PASS__) {
                result = sampled(source_zero, uv);
            } else if (pass == __DOWNSAMPLE_PASS__) {
                result = vec4(four_tap_downsample(uv, source_size), 0.0);
            } else if (pass == __ADAPTATION_PASS__) {
                vec3 current_average = four_tap_downsample(uv, source_size);
                vec3 previous_average = params.controls.y > 0.5
                    ? current_average
                    : sampled(source_one, uv).rgb;
                float retained = pow(__ADAPTATION_RETENTION_BASE__, params.controls.z);
                vec3 adapted = mix(current_average, previous_average, retained);
                float magnitude = length(adapted);
                float bounded_magnitude = max(magnitude, __MINIMUM_ADAPTATION_MAGNITUDE__);
                float scale = min(bounded_magnitude, 1.0) / bounded_magnitude;
                result = vec4(adapted * scale, 0.0);
            } else if (pass == __VERTICAL_BLUR_PASS__) {
                vec3 bright = vec3(0.0);
                for (int index = 0; index < __BLUR_WEIGHT_COUNT__; ++index) {
                    float offset = float(index - __BLUR_CENTER_INDEX__) / source_size.y;
                    vec3 value = sampled(source_zero, uv + vec2(0.0, offset)).rgb;
                    bright += max(value - vec3(__BRIGHT_THRESHOLD__), vec3(0.0)) *
                        __BRIGHT_SCALE__ * blur_weights[index];
                }
                // The matched retail frame already carries its temporal adaptation
                // state in the alpha channel of the captured blurred-HDR input.
                float adaptation_sum = params.hdr_state.y > 0.5
                    ? max(dot(sampled(source_one, vec2(0.5)).rgb, vec3(1.0)), 0.0001)
                    : params.hdr_state.x;
                result = vec4(bright, adaptation_sum);
            } else if (pass == __HORIZONTAL_BLUR_PASS__) {
                vec3 blurred = vec3(0.0);
                for (int index = 0; index < __BLUR_WEIGHT_COUNT__; ++index) {
                    float offset = float(index - __BLUR_CENTER_INDEX__) / source_size.x;
                    blurred += sampled(source_zero, uv + vec2(offset, 0.0)).rgb *
                        blur_weights[index];
                }
                float adaptation_sum = sampled(
                    source_zero,
                    uv + vec2(float(__BLUR_CENTER_INDEX__) / source_size.x, 0.0)).a;
                result = vec4(blurred, adaptation_sum);
            } else if (pass == __HDR_COMBINE_PASS__) {
                vec4 bloom = sampled(source_zero, uv);
                vec4 scene = sampled(source_one, uv);
                float normalization = max(bloom.a, params.controls.w);
                vec3 color = scene.rgb * params.controls.w / normalization +
                    max(bloom.rgb * __BLOOM_NORMALIZATION_SCALE__ / normalization, vec3(0.0));
                float luminance = dot(color, vec3(__LUMINANCE_WEIGHTS__));
                color = mix(vec3(luminance), color, params.cinematic.x);
                color = mix(color, vec3(luminance) * params.tint.rgb, params.tint.a);
                color = ((color * params.cinematic.w) - params.cinematic.y) *
                    params.cinematic.z + params.cinematic.y;
                color = mix(color, params.fade.rgb, params.fade.a);
                result = vec4(clamp(color, vec3(0.0), vec3(1.0)), 1.0);
            } else {
                // Godot sRGB-encodes the forward viewport when it is copied to
                // the engine-owned PNG.  Retail wrote the pass-5 numeric result
                // to X8R8G8B8 with sRGB write disabled.  Store the inverse
                // transfer here so the captured byte values remain retail's.
                vec4 retail = sampled(source_zero, uv);
                result = vec4(encoded_to_linear(retail.rgb), retail.a);
            }

            imageStore(destination_image, pixel, result);
        }
        """;

    private static string BuildComputeShaderSource(
        RetailImageSpaceConfiguration configuration,
        ColorTransferConfiguration outputTransfer)
    {
        var hdr = configuration.HdrBlend;
        var blurCenterIndex = hdr.BlurWeights.Count / 2;
        var blurWeights = string.Join(
            ",\n            ",
            hdr.BlurWeights.Select(Invariant));
        var luminanceWeights = string.Join(", ", configuration.LuminanceWeightsRgb.Select(Invariant));
        var source = ComputeShaderTemplate
            .Replace("__WORK_GROUP_SIDE__", hdr.WorkGroupSidePixels.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLUR_WEIGHT_COUNT__", hdr.BlurWeights.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLUR_CENTER_INDEX__", blurCenterIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLUR_WEIGHTS__", blurWeights)
            .Replace("__TRANSFER_LINEAR_SCALE__", Invariant(outputTransfer.LinearScale))
            .Replace("__TRANSFER_OFFSET__", Invariant(outputTransfer.Offset))
            .Replace("__TRANSFER_NORMALIZATION__", Invariant(outputTransfer.Normalization))
            .Replace("__TRANSFER_EXPONENT__", Invariant(outputTransfer.Exponent))
            .Replace("__TRANSFER_ENCODED_CUTOFF__", Invariant(outputTransfer.EncodedCutoff))
            .Replace("__ADAPTATION_RETENTION_BASE__", Invariant(hdr.AdaptationRetentionBase))
            .Replace("__MINIMUM_ADAPTATION_MAGNITUDE__", Invariant(hdr.MinimumAdaptationMagnitude))
            .Replace("__BRIGHT_THRESHOLD__", Invariant(hdr.BrightThreshold))
            .Replace("__BRIGHT_SCALE__", Invariant(hdr.BrightScale))
            .Replace("__BLOOM_NORMALIZATION_SCALE__", Invariant(hdr.BloomNormalizationScale))
            .Replace("__LUMINANCE_WEIGHTS__", luminanceWeights)
            .Replace("__COPY_PASS__", CopyPass.ToString(CultureInfo.InvariantCulture))
            .Replace("__DOWNSAMPLE_PASS__", DownsamplePass.ToString(CultureInfo.InvariantCulture))
            .Replace("__ADAPTATION_PASS__", AdaptationPass.ToString(CultureInfo.InvariantCulture))
            .Replace("__VERTICAL_BLUR_PASS__", VerticalBlurPass.ToString(CultureInfo.InvariantCulture))
            .Replace("__HORIZONTAL_BLUR_PASS__", HorizontalBlurPass.ToString(CultureInfo.InvariantCulture))
            .Replace("__HDR_COMBINE_PASS__", HdrCombinePass.ToString(CultureInfo.InvariantCulture));
        if (source.Contains("__", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Retail HDR shader template contains an unresolved configuration token.");
        return source;
    }

    private static string Invariant(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private readonly float _targetLuminance;
    private readonly float _matchedAdaptationSum;
    private readonly Vector4 _cinematic;
    private readonly Vector4 _tint;
    private readonly Vector4 _fade;
    private readonly RetailHdrBlendConfiguration _hdr;
    private readonly CaptureConfiguration _capture;
    private readonly string _computeShaderSource;
    private readonly bool _runtimeAdaptation;
    private readonly Dictionary<uint, bool> _writeAdaptationA = new();
    private readonly Dictionary<uint, Rid> _sceneCopies = new();
    private readonly Dictionary<uint, Rid> _postHdrScenes = new();

    private RenderingDevice? _renderingDevice;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _pointSampler;
    private Rid _linearSampler;
    private int _operational;
    private int _failureReported;

    internal RetailHdrCompositorEffect(
        float targetLuminance,
        float matchedAdaptationSum,
        Vector4 cinematic,
        Vector4 tint,
        Vector4 fade,
        RetailImageSpaceConfiguration configuration,
        CaptureConfiguration capture,
        ColorTransferConfiguration outputTransfer,
        bool runtimeAdaptation)
    {
        if (!float.IsFinite(matchedAdaptationSum) || matchedAdaptationSum <= 0.0f)
            throw new InvalidOperationException(
                "Matched retail HDR adaptation state is invalid.");
        _targetLuminance = targetLuminance;
        _matchedAdaptationSum = matchedAdaptationSum;
        _cinematic = cinematic;
        _tint = tint;
        _fade = fade;
        _hdr = configuration.HdrBlend;
        _capture = capture;
        _runtimeAdaptation = runtimeAdaptation;
        _computeShaderSource = BuildComputeShaderSource(
            configuration,
            outputTransfer);
        Enabled = true;
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
    }

    internal bool Operational => Volatile.Read(ref _operational) != 0;

    internal byte[] CapturePreHdrSceneColor(uint view = 0) =>
        CaptureRetainedSceneColor(_sceneCopies, "pre-HDR", view);

    internal byte[] CapturePostHdrSceneColor(uint view = 0) =>
        CaptureRetainedSceneColor(_postHdrScenes, "post-HDR", view);

    private byte[] CaptureRetainedSceneColor(
        IReadOnlyDictionary<uint, Rid> retainedScenes,
        string stage,
        uint view)
    {
        if (!Operational)
            throw new InvalidOperationException(
                $"Retail HDR compositor is not operational for {stage} scene-color capture.");
        byte[]? result = null;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                if (_renderingDevice is null ||
                    !retainedScenes.TryGetValue(view, out var retainedScene) ||
                    !retainedScene.IsValid)
                    throw new InvalidOperationException(
                        $"Retail HDR compositor has no retained {stage} scene color.");
                result = _renderingDevice.TextureGetData(retainedScene, 0);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        }));
        if (!completed.Wait(TimeSpan.FromSeconds(_hdr.ReadbackTimeoutSeconds)))
            throw new TimeoutException($"Timed out reading the {stage} scene color.");
        if (failure is not null)
            throw new InvalidOperationException(
                $"Could not read the {stage} scene color.",
                failure);
        var expectedBytes = checked(
            _capture.ExpectedWidthPixels *
            _capture.ExpectedHeightPixels *
            _hdr.ComponentCount *
            _hdr.ComponentBytes);
        if (result is null || result.Length != expectedBytes)
            throw new InvalidOperationException(
                $"{stage} scene color has invalid byte length: {result?.Length ?? 0}.");
        return result;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent)
            return;

        try
        {
            EnsurePipeline();
            using var buffers = renderData.GetRenderSceneBuffers() as RenderSceneBuffersRD;
            if (buffers is null)
                return;
            var size = buffers.GetInternalSize();
            if (size != new Vector2I(
                    _capture.ExpectedWidthPixels,
                    _capture.ExpectedHeightPixels))
            {
                Volatile.Write(ref _operational, 0);
                return;
            }

            for (uint view = 0; view < buffers.GetViewCount(); ++view)
                RenderView(buffers, view, size);
            Volatile.Write(ref _operational, 1);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _operational, 0);
            if (Interlocked.Exchange(ref _failureReported, 1) == 0)
                GD.PushError($"OpenNV retail HDR compositor failed: {exception.Message}");
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _renderingDevice is null)
            return;
        if (_shader.IsValid)
            _renderingDevice.FreeRid(_shader);
        if (_pointSampler.IsValid)
            _renderingDevice.FreeRid(_pointSampler);
        if (_linearSampler.IsValid)
            _renderingDevice.FreeRid(_linearSampler);
    }

    private void EnsurePipeline()
    {
        if (_pipeline.IsValid)
            return;
        _renderingDevice = RenderingServer.GetRenderingDevice() ??
            throw new InvalidOperationException("Forward+ did not expose its rendering device.");
        using var source = new RDShaderSource
        {
            Language = RenderingDevice.ShaderLanguage.Glsl,
            SourceCompute = _computeShaderSource,
        };
        using var spirV = _renderingDevice.ShaderCompileSpirVFromSource(source);
        if (!string.IsNullOrWhiteSpace(spirV.CompileErrorCompute))
            throw new InvalidOperationException(spirV.CompileErrorCompute);
        _shader = _renderingDevice.ShaderCreateFromSpirV(
            spirV,
            "OpenNV_RetailHdrImageSpace");
        if (!_shader.IsValid)
            throw new InvalidOperationException("RenderingDevice rejected the retail HDR shader.");
        _pipeline = _renderingDevice.ComputePipelineCreate(_shader);
        if (!_pipeline.IsValid)
            throw new InvalidOperationException("RenderingDevice rejected the retail HDR pipeline.");
        _pointSampler = _renderingDevice.SamplerCreate(Sampler(linear: false));
        _linearSampler = _renderingDevice.SamplerCreate(Sampler(linear: true));
        if (!_pointSampler.IsValid || !_linearSampler.IsValid)
            throw new InvalidOperationException("RenderingDevice rejected a retail HDR sampler.");
    }

    private void RenderView(RenderSceneBuffersRD buffers, uint view, Vector2I sceneSize)
    {
        var suffix = view.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var targets = _hdr.Targets;
        var halfSize = Size(targets.HalfPixels);
        var sourceSize = Size(targets.SourcePixels);
        var adaptationSize = Size(targets.AdaptationPixels);
        var brightSize = Size(targets.BrightPixels);
        var bloomSize = Size(targets.BloomPixels);
        var scene = buffers.GetColorLayer(view);
        var sceneCopy = Texture(buffers, $"scene_{suffix}", sceneSize, out _);
        _sceneCopies[view] = sceneCopy;
        var postHdrScene = Texture(buffers, $"post_{suffix}", sceneSize, out _);
        _postHdrScenes[view] = postHdrScene;
        var half = Texture(buffers, $"half_{suffix}", halfSize, out _);
        var source = Texture(buffers, $"source_{suffix}", sourceSize, out _);
        var downsampleSizes = targets.DownsamplePixels.Select(Size).ToArray();
        var downsampleTextures = downsampleSizes
            .Select((size, index) =>
                Texture(buffers, $"down_{index}_{suffix}", size, out _))
            .ToArray();
        var adaptationA = Texture(
            buffers,
            $"adapt_a_{suffix}",
            adaptationSize,
            out var adaptationACreated);
        var adaptationB = Texture(
            buffers,
            $"adapt_b_{suffix}",
            adaptationSize,
            out var adaptationBCreated);
        var bright = Texture(buffers, $"bright_{suffix}", brightSize, out _);
        var bloom = Texture(buffers, $"bloom_{suffix}", bloomSize, out _);

        var adaptationCreated = adaptationACreated || adaptationBCreated ||
            !_writeAdaptationA.ContainsKey(view);
        var writeA = !_writeAdaptationA.TryGetValue(view, out var storedWriteA) || storedWriteA;
        var nextAdaptation = writeA ? adaptationA : adaptationB;
        var previousAdaptation = writeA ? adaptationB : adaptationA;

        var passes = new List<DispatchPass>
        {
            Pass(
                scene,
                _pointSampler,
                scene,
                _pointSampler,
                sceneCopy,
                CopyPass,
                sceneSize,
                sceneSize),
            Pass(
                sceneCopy,
                _linearSampler,
                sceneCopy,
                _linearSampler,
                half,
                CopyPass,
                halfSize,
                sceneSize),
            Pass(
                half,
                _pointSampler,
                half,
                _pointSampler,
                source,
                CopyPass,
                sourceSize,
                halfSize),
        };
        var previousDownsampleTexture = source;
        var previousDownsampleSize = sourceSize;
        for (var index = 0; index < downsampleTextures.Length; ++index)
        {
            passes.Add(Pass(
                previousDownsampleTexture,
                _linearSampler,
                previousDownsampleTexture,
                _linearSampler,
                downsampleTextures[index],
                DownsamplePass,
                downsampleSizes[index],
                previousDownsampleSize));
            previousDownsampleTexture = downsampleTextures[index];
            previousDownsampleSize = downsampleSizes[index];
        }
        passes.Add(Pass(
            previousDownsampleTexture,
            _linearSampler,
            previousAdaptation,
            _pointSampler,
            nextAdaptation,
            AdaptationPass,
            adaptationSize,
            previousDownsampleSize,
            adaptationCreated));
        passes.Add(Pass(
            source,
            _pointSampler,
            nextAdaptation,
            _pointSampler,
            bright,
            VerticalBlurPass,
            brightSize,
            sourceSize));
        passes.Add(Pass(
            bright,
            _pointSampler,
            bright,
            _pointSampler,
            bloom,
            HorizontalBlurPass,
            bloomSize,
            brightSize));
        passes.Add(Pass(
            bloom,
            _linearSampler,
            sceneCopy,
            _pointSampler,
            postHdrScene,
            HdrCombinePass,
            sceneSize,
            bloomSize));
        passes.Add(Pass(
            postHdrScene,
            _pointSampler,
            postHdrScene,
            _pointSampler,
            scene,
            OutputTransferPass,
            sceneSize,
            sceneSize));

        var computeList = _renderingDevice!.ComputeListBegin();
        _renderingDevice.ComputeListBindComputePipeline(computeList, _pipeline);
        for (var index = 0; index < passes.Count; ++index)
        {
            var pass = passes[index];
            _renderingDevice.ComputeListBindUniformSet(computeList, pass.UniformSet, 0);
            _renderingDevice.ComputeListSetPushConstant(
                computeList,
                pass.PushConstants,
                (uint)pass.PushConstants.Length);
            _renderingDevice.ComputeListDispatch(
                computeList,
                pass.GroupsX,
                pass.GroupsY,
                1);
            if (index + 1 < passes.Count)
                _renderingDevice.ComputeListAddBarrier(computeList);
        }
        _renderingDevice.ComputeListEnd();
        _writeAdaptationA[view] = !writeA;
    }

    private static Vector2I Size(IReadOnlyList<int> pixels) =>
        new(pixels[0], pixels[1]);

    private Rid Texture(
        RenderSceneBuffersRD buffers,
        string name,
        Vector2I size,
        out bool created)
    {
        var textureName = new StringName(name);
        created = !buffers.HasTexture(TextureContext, textureName);
        return buffers.CreateTexture(
            TextureContext,
            textureName,
            RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            TextureUsage,
            RenderingDevice.TextureSamples.Samples1,
            size,
            1,
            1,
            false,
            false);
    }

    private DispatchPass Pass(
        Rid sourceZero,
        Rid samplerZero,
        Rid sourceOne,
        Rid samplerOne,
        Rid destination,
        int pass,
        Vector2I destinationSize,
        Vector2I sourceSize,
        bool initializeAdaptation = false)
    {
        var uniforms = new Godot.Collections.Array<RDUniform>
        {
            SampledUniform(0, samplerZero, sourceZero),
            SampledUniform(1, samplerOne, sourceOne),
            ImageUniform(2, destination),
        };
        var uniformSet = UniformSetCacheRD.GetCache(_shader, 0, uniforms);
        if (!uniformSet.IsValid)
            throw new InvalidOperationException("Could not bind a retail HDR compositor pass.");
        return new DispatchPass(
            uniformSet,
            PushConstants(pass, destinationSize, sourceSize, initializeAdaptation),
            Groups(destinationSize.X),
            Groups(destinationSize.Y));
    }

    private byte[] PushConstants(
        int pass,
        Vector2I destinationSize,
        Vector2I sourceSize,
        bool initializeAdaptation)
    {
        var values = new[]
        {
            (float)destinationSize.X,
            (float)destinationSize.Y,
            (float)sourceSize.X,
            (float)sourceSize.Y,
            (float)pass,
            initializeAdaptation ? 1.0f : 0.0f,
            _hdr.AdaptationDeltaSeconds,
            _targetLuminance,
            _cinematic.X,
            _cinematic.Y,
            _cinematic.Z,
            _cinematic.W,
            _tint.X,
            _tint.Y,
            _tint.Z,
            _tint.W,
            _fade.X,
            _fade.Y,
            _fade.Z,
            _fade.W,
            _matchedAdaptationSum,
            _runtimeAdaptation ? 1.0f : 0.0f,
            0.0f,
            0.0f,
        };
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static RDUniform SampledUniform(int binding, Rid sampler, Rid texture)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.SamplerWithTexture,
            Binding = binding,
        };
        uniform.AddId(sampler);
        uniform.AddId(texture);
        return uniform;
    }

    private static RDUniform ImageUniform(int binding, Rid texture)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.Image,
            Binding = binding,
        };
        uniform.AddId(texture);
        return uniform;
    }

    private static RDSamplerState Sampler(bool linear) => new()
    {
        MagFilter = linear
            ? RenderingDevice.SamplerFilter.Linear
            : RenderingDevice.SamplerFilter.Nearest,
        MinFilter = linear
            ? RenderingDevice.SamplerFilter.Linear
            : RenderingDevice.SamplerFilter.Nearest,
        MipFilter = RenderingDevice.SamplerFilter.Nearest,
        RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        RepeatW = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        MinLod = 0.0f,
        MaxLod = 0.0f,
    };

    private uint Groups(int pixels) =>
        (uint)((pixels + _hdr.WorkGroupSidePixels - 1) / _hdr.WorkGroupSidePixels);

    private readonly record struct DispatchPass(
        Rid UniformSet,
        byte[] PushConstants,
        uint GroupsX,
        uint GroupsY);
}
