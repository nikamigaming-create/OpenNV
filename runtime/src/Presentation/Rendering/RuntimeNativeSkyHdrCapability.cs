using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// The selected original asks the device for RGBA half-float post-pixel blend
// support. This owner executes the corresponding native format/pipeline query
// on Godot's real rendering device. It submits no draw and certifies no pixels.
internal sealed class RuntimeNativeSkyHdrCapability : IRuntimeSkyHdrCapability
{
    private readonly object _gate = new();
    private readonly Guid _process;
    private readonly FalloutSkyHdrSource _source;
    private readonly int _presentationThread = System.Environment.CurrentManagedThreadId;
    private RenderingDevice? _device;
    private Rid _texture, _framebuffer, _shader, _pipeline;
    private bool _entered, _returned, _supported, _retiring, _retired;
    private Exception? _failure;
    private long _changed;
    private TaskCompletionSource? _retirementCompletion;
    public Task Retirement
    {
        get
        {
            RequirePresentationThread(); lock (_gate)
                return _retirementCompletion?.Task ?? throw new InvalidOperationException("HDR native retirement has not entered.");
        }
    }
    internal Guid Identity { get; } = Guid.NewGuid();
    Guid IFalloutSkyHdrCapability.Identity => Identity;
    private const string Vertex = """
        #version 450
        void main() {
            vec2 point = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
            gl_Position = vec4(point * 2.0 - 1.0, 0.0, 1.0);
        }
        """;
    private const string Fragment = """
        #version 450
        layout(location = 0) out vec4 colour;
        void main() { colour = vec4(0.25, 0.5, 0.75, 0.5); }
        """;
    internal RuntimeNativeSkyHdrCapability(FalloutSkyHdrSource source, Guid process)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (process == Guid.Empty) throw new InvalidDataException("Native HDR query has no current source process.");
        _source = source; _process = process;
    }
    private void RequirePresentationThread()
    {
        if (System.Environment.CurrentManagedThreadId != _presentationThread)
            throw new InvalidOperationException("Native HDR query publication/retirement left its actual presentation thread.");
    }
    public void Begin()
    {
        RequirePresentationThread();
        lock (_gate)
        {
            if (_entered || _retiring || _retired) throw new InvalidOperationException("HDR format constructor was replayed or retired.");
            _entered = true; ++_changed;
        }
        try { RenderingServer.CallOnRenderThread(Callable.From(Construct)); }
        catch (Exception failure) { lock (_gate) { _failure = failure; ++_changed; } throw; }
    }
    private void Construct()
    {
        lock (_gate)
        {
            if (_retiring || _retired) return; // Cancellation owns every still-created RID below.
            try
            {
                _device = RenderingServer.GetRenderingDevice();
                if (_device is null) throw new NotSupportedException("Actual rendering device has no admitted half-float format query.");
                const RenderingDevice.TextureUsageBits usage = RenderingDevice.TextureUsageBits.ColorAttachmentBit |
                    RenderingDevice.TextureUsageBits.SamplingBit;
                var nativeBlendSupported = RuntimeNativeSkyHdrBlendFormat.Query(_device);
                if (!nativeBlendSupported || !_device.TextureIsFormatSupportedForUsage(RenderingDevice.DataFormat.R16G16B16A16Sfloat, usage))
                { _supported = false; _returned = true; ++_changed; return; }
                using var format = new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                    TextureType = RenderingDevice.TextureType.Type2D,
                    Width = 1,
                    Height = 1,
                    Depth = 1,
                    ArrayLayers = 1,
                    Mipmaps = 1,
                    Samples = RenderingDevice.TextureSamples.Samples1,
                    UsageBits = usage
                };
                using var view = new RDTextureView();
                _texture = _device.TextureCreate(format, view);
                if (!_texture.IsValid || !_device.TextureIsValid(_texture)) throw new InvalidOperationException("HDR format query could not retain its actual half-float texture.");
                _framebuffer = _device.FramebufferCreate(new Godot.Collections.Array<Rid> { _texture });
                if (!_framebuffer.IsValid) throw new InvalidOperationException("HDR format query could not construct its actual framebuffer.");
                using var shaderSource = new RDShaderSource
                {
                    Language = RenderingDevice.ShaderLanguage.Glsl,
                    SourceVertex = Vertex,
                    SourceFragment = Fragment
                };
                using var spirV = _device.ShaderCompileSpirVFromSource(shaderSource);
                if (!string.IsNullOrWhiteSpace(spirV.CompileErrorVertex) || !string.IsNullOrWhiteSpace(spirV.CompileErrorFragment))
                    throw new InvalidOperationException("Actual HDR blend capability shader failed: " + spirV.CompileErrorVertex + spirV.CompileErrorFragment);
                _shader = _device.ShaderCreateFromSpirV(spirV, "OpenNV_Source_HDR_Format_Query");
                if (!_shader.IsValid) throw new InvalidOperationException("HDR format query could not retain its actual native shader.");
                using var raster = new RDPipelineRasterizationState { CullMode = RenderingDevice.PolygonCullMode.Disabled };
                using var samples = new RDPipelineMultisampleState();
                using var depth = new RDPipelineDepthStencilState();
                using var attachment = new RDPipelineColorBlendStateAttachment
                {
                    EnableBlend = true,
                    SrcColorBlendFactor = RenderingDevice.BlendFactor.SrcAlpha,
                    DstColorBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
                    SrcAlphaBlendFactor = RenderingDevice.BlendFactor.SrcAlpha,
                    DstAlphaBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
                    WriteR = true,
                    WriteG = true,
                    WriteB = true,
                    WriteA = true
                };
                using var blend = new RDPipelineColorBlendState { Attachments = new Godot.Collections.Array<RDPipelineColorBlendStateAttachment> { attachment } };
                _pipeline = _device.RenderPipelineCreate(_shader, _device.FramebufferGetFormat(_framebuffer), -1,
                    RenderingDevice.RenderPrimitive.Triangles, raster, samples, depth, blend);
                if (!_pipeline.IsValid || !_device.RenderPipelineIsValid(_pipeline))
                    throw new InvalidOperationException("Actual device rejected the HDR half-float post-pixel blend query.");
                _supported = true; _returned = true; ++_changed;
            }
            catch (Exception failure) { _failure = failure; ++_changed; }
        }
    }
    public bool Returned
    {
        get
        {
            RequirePresentationThread(); lock (_gate)
            {
                if (_failure is { } failure) throw new InvalidOperationException("HDR native constructor retains its original failure.", failure);
                return _returned && !_retiring && !_retired;
            }
        }
    }
    public FalloutSkyHdrCapability Read()
    {
        RequirePresentationThread(); lock (_gate)
        {
            if (_failure is { } failure) throw new InvalidOperationException("HDR native owner retains actual format/resource failure.", failure);
            if (!_returned || _retiring || _retired || _device is null || !GodotObject.IsInstanceValid(_device))
                throw new NotSupportedException("source-Sky-HDR-real-native-format-query-entered-not-returned");
            // No GPU calls are made from this thread. RIDs are retained under
            // the render-thread creation/retirement lock, rather than tested
            // from an unrelated native thread.
            if (_supported && (!_pipeline.IsValid || !_framebuffer.IsValid || !_texture.IsValid || !_shader.IsValid))
                throw new InvalidDataException("Actual HDR format lease lost a required native resource.");
            return new(Identity, _process, _source.EngineSha256, _source.Contract, _changed, _supported, true,
                "Godot-render-thread/public-native-RGBA16Float-blend-feature-query-and-framebuffer-pipeline-return");
        }
    }
    public void BeginRetirement()
    {
        RequirePresentationThread(); lock (_gate)
        {
            if (_retired) return;
            if (_retiring) return;
            _retirementCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retiring = true; ++_changed;
        }
        try { RenderingServer.CallOnRenderThread(Callable.From(RetireNative)); }
        catch (Exception failure) { lock (_gate) { _failure ??= failure; _retiring = false; ++_changed; _retirementCompletion!.TrySetException(failure); } throw; }
    }
    private void RetireNative()
    {
        lock (_gate)
        {
            var failures = new List<Exception>();
            if (_device is not null)
            {
                Release(ref _pipeline);
                if (!_pipeline.IsValid) Release(ref _framebuffer);
                if (!_framebuffer.IsValid) Release(ref _texture);
                if (!_pipeline.IsValid) Release(ref _shader);
            }
            if (failures.Count != 0)
            {
                var failed = new AggregateException("HDR retains independently failed native retirement and its original constructor failure.",
                    _failure is null ? failures : failures.Prepend(_failure));
                _failure = failed; _retiring = false; ++_changed; _retirementCompletion!.TrySetException(failed); return;
            }
            _device = null; _retired = true; _retiring = false; ++_changed;
            _retirementCompletion!.TrySetResult();
            void Release(ref Rid resource)
            {
                if (!resource.IsValid) return;
                try { _device!.FreeRid(resource); resource = default; }
                catch (Exception failure) { failures.Add(failure); }
            }
        }
    }
    public bool Retired
    {
        get
        {
            RequirePresentationThread(); lock (_gate)
            {
                if (!_retired && _failure is { } failure) throw new InvalidOperationException("HDR native retirement still owns failed resources.", failure);
                return _retired;
            }
        }
    }
}
