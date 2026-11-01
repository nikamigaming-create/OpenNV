using System.Buffers.Binary;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

// Render-thread-owned, full-resolution paired-mask reduction. Only the final
// two integer counts leave the GPU; the fragment predicates are unchanged.
internal sealed class NativeVatsGpuCounts : IDisposable
{
    private readonly RenderingDevice _device;
    private readonly List<Rid> _owned = [];
    private readonly Rid[] _snapshots = new Rid[4];
    private readonly Rid _shader, _pipeline, _sampler, _normalizedBytes;
    private Rid _partials, _result, _uniforms;
    private uint _width, _height, _groupsX, _groupsY;
    private RenderingDevice.DataFormat _format;
    private bool _disposed;
    private int _captured;
    internal Rid Result => _result;
    internal static bool Supports(RenderingDevice.DataFormat format) => format is
        RenderingDevice.DataFormat.R8G8B8A8Unorm or RenderingDevice.DataFormat.B8G8R8A8Unorm or
        RenderingDevice.DataFormat.R16G16B16A16Sfloat;

    internal NativeVatsGpuCounts(RenderingDevice device)
    {
        _device = device;
        try
        {
            using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = Source };
            using var spirV = device.ShaderCompileSpirVFromSource(source);
            if (!string.IsNullOrEmpty(spirV.CompileErrorCompute)) throw new InvalidOperationException(spirV.CompileErrorCompute);
            _shader = Own(device.ShaderCreateFromSpirV(spirV, "OpenNV_VatsExactMaskCounts"));
            _pipeline = Own(device.ComputePipelineCreate(_shader));
            using var sampler = new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Nearest,
                MagFilter = RenderingDevice.SamplerFilter.Nearest,
                MipFilter = RenderingDevice.SamplerFilter.Nearest
            };
            _sampler = Own(device.SamplerCreate(sampler));
            // Recover each UNORM byte before loading the exact CPU float32
            // normalization. A GPU reciprocal multiply is not this contract.
            var table = new byte[256 * sizeof(float)];
            for (var i = 0; i < 256; i++) BinaryPrimitives.WriteSingleLittleEndian(table.AsSpan(i * 4), i / 255f);
            _normalizedBytes = Own(device.StorageBufferCreate((uint)table.Length, table));
        }
        catch { Dispose(); throw; }
    }

    private Rid Own(Rid rid)
    {
        if (!rid.IsValid) throw new InvalidOperationException("VATS GPU reduction resource creation failed.");
        _owned.Add(rid); return rid;
    }

    internal void Capture(Rid source, int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)slot >= 4) throw new ArgumentOutOfRangeException(nameof(slot));
        if (slot == 0) _captured = 0;
        using var format = _device.TextureGetFormat(source);
        if (!Supports(format.Format)) throw new NotSupportedException("Exact VATS GPU texture format is unsupported: " + format.Format);
        if (_width == 0) Configure(format);
        if (_width != format.Width || _height != format.Height || _format != format.Format ||
            format.Depth != 1 || format.ArrayLayers != 1 || format.Mipmaps != 1 || format.Samples != RenderingDevice.TextureSamples.Samples1)
            throw new InvalidDataException("VATS GPU mask extent/format/samples changed between captures.");
        var error = _device.TextureCopy(source, _snapshots[slot], Vector3.Zero, Vector3.Zero, new(_width, _height, 1), 0, 0, 0, 0);
        if (error != Error.Ok) throw new InvalidOperationException("VATS GPU mask copy failed: " + error);
        _captured |= 1 << slot;
    }

    private void Configure(RDTextureFormat source)
    {
        if (source.Width == 0 || source.Height == 0 || (ulong)source.Width * source.Height > int.MaxValue)
            throw new NotSupportedException("VATS mask pixel count exceeds its integer count owner.");
        _width = source.Width; _height = source.Height; _format = source.Format;
        _groupsX = (_width + 15) / 16; _groupsY = (_height + 15) / 16;
        using var format = new RDTextureFormat
        {
            Format = _format,
            Width = _width,
            Height = _height,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = 1,
            TextureType = RenderingDevice.TextureType.Type2D,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit
        };
        using var view = new RDTextureView();
        for (var i = 0; i < _snapshots.Length; i++) _snapshots[i] = Own(_device.TextureCreate(format, view));
        _partials = Own(_device.StorageBufferCreate(checked(_groupsX * _groupsY * 8)));
        _result = Own(_device.StorageBufferCreate(8));
        var uniforms = new Godot.Collections.Array<RDUniform>();
        var wrappers = new List<RDUniform>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = i };
                uniform.AddId(_sampler); uniform.AddId(_snapshots[i]); uniforms.Add(uniform); wrappers.Add(uniform);
            }
            foreach (var (binding, buffer) in new[] { (4, _partials), (5, _result), (6, _normalizedBytes) })
            {
                var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = binding };
                uniform.AddId(buffer); uniforms.Add(uniform); wrappers.Add(uniform);
            }
            _uniforms = Own(_device.UniformSetCreate(uniforms, _shader, 0));
        }
        finally { foreach (var uniform in wrappers) uniform.Dispose(); }
    }

    internal void Dispatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_uniforms.IsValid || _captured != 15) throw new InvalidOperationException("VATS reduction has no complete captured texture owner.");
        var push = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(0), _width);
        BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(4), _height);
        BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(8), _groupsX);
        BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(12), checked(_groupsX * _groupsY));
        BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(20), _format == RenderingDevice.DataFormat.R16G16B16A16Sfloat ? 0u : 1u);
        var list = _device.ComputeListBegin();
        try
        {
            _device.ComputeListBindComputePipeline(list, _pipeline);
            _device.ComputeListBindUniformSet(list, _uniforms, 0);
            _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _device.ComputeListDispatch(list, _groupsX, _groupsY, 1);
            _device.ComputeListAddBarrier(list);
            BinaryPrimitives.WriteUInt32LittleEndian(push.AsSpan(16), 1);
            _device.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _device.ComputeListDispatch(list, 1, 1, 1);
        }
        finally { _device.ComputeListEnd(); }
        // The main device is submitted by Godot. Never Submit/Sync it here.
    }

    internal static (uint Total, uint Visible) Decode(byte[] data)
    {
        if (data.Length != 8) throw new InvalidDataException("VATS GPU count receipt has an invalid extent.");
        var total = BinaryPrimitives.ReadUInt32LittleEndian(data); var visible = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (visible > int.MaxValue || total > int.MaxValue) throw new InvalidDataException("VATS GPU numerator/denominator is invalid.");
        return (total, visible);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        for (var i = _owned.Count - 1; i >= 0; i--) _device.FreeRid(_owned[i]);
        _owned.Clear();
    }

    private const string Source = """
        #version 450
        layout(local_size_x=16, local_size_y=16, local_size_z=1) in;
        layout(set=0,binding=0) uniform sampler2D reference_red;
        layout(set=0,binding=1) uniform sampler2D reference_blue;
        layout(set=0,binding=2) uniform sampler2D measured_red;
        layout(set=0,binding=3) uniform sampler2D measured_blue;
        layout(set=0,binding=4,std430) buffer Partials { uvec2 partials[]; };
        layout(set=0,binding=5,std430) buffer Result { uvec2 result; };
        layout(set=0,binding=6,std430) readonly buffer Bytes { float normalized_bytes[]; };
        layout(push_constant,std430) uniform Params { uvec2 extent; uint groups_x; uint group_count; uint phase; uint unorm; } p;
        shared uvec2 sums[256];
        vec3 pixel(sampler2D image, ivec2 point) {
            vec3 value=texelFetch(image,point,0).rgb;
            if(p.unorm==0u) return value;
            uvec3 bytes=uvec3(round(value*255.0));
            return vec3(normalized_bytes[bytes.r],normalized_bytes[bytes.g],normalized_bytes[bytes.b]);
        }
        bool matches(vec3 first,vec3 second,float expected_r,float expected_b) {
            precise float r=first.r-second.r;
            precise float b=second.b-first.b;
            precise float green=first.g-second.g;
            precise float dr=r-expected_r;
            precise float db=b-expected_b;
            float q=uintBitsToFloat(0x3b808081u);
            return abs(dr)<=q && abs(db)<=q && abs(green)<=q;
        }
        void main() {
            uint local=gl_LocalInvocationIndex;
            uvec2 value=uvec2(0);
            if(p.phase==0u) {
                uvec2 point=gl_GlobalInvocationID.xy;
                if(all(lessThan(point,p.extent))) {
                    vec3 rr=pixel(reference_red,ivec2(point));
                    vec3 rb=pixel(reference_blue,ivec2(point));
                    precise float er=rr.r-rb.r;
                    precise float eb=rb.b-rr.b;
                    precise float delta=er-eb;
                    float two_q=2.0*uintBitsToFloat(0x3b808081u);
                    if(!(er<two_q || eb<two_q || abs(delta)>two_q)) {
                        value.x=matches(rr,rb,er,eb)?1u:0u;
                        value.y=matches(pixel(measured_red,ivec2(point)),pixel(measured_blue,ivec2(point)),er,eb)?1u:0u;
                    }
                }
            } else {
                for(uint i=local;i<p.group_count;i+=256u) value+=partials[i];
            }
            sums[local]=value;
            barrier();
            for(uint stride=128u;stride>0u;stride>>=1u) {
                if(local<stride) sums[local]+=sums[local+stride];
                barrier();
            }
            if(local==0u) {
                if(p.phase==0u) partials[gl_WorkGroupID.x+gl_WorkGroupID.y*p.groups_x]=sums[0];
                else result=sums[0];
            }
        }
        """;
}
