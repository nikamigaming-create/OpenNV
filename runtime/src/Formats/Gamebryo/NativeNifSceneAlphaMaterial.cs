using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Formats.Gamebryo;

// This adapter extends only our generated shaders. It never reads native
// material alpha back into the logical property or changes the material cell.
internal sealed class NativeNifSceneAlphaMaterial
{
    internal const string Parameter = "source_scene_shader_alpha";
    internal const string Parity = "logical-source-alpha-consumed;source-blend-variant-and-final-pixels-unverified";
    private sealed class FadeProgram(Shader original)
    {
        internal Shader Shader { get; } = new() { Code = FadeCode(original.Code) };
        internal int Users;
    }
    private static readonly ConditionalWeakTable<Shader, FadeProgram> FadeShaders = new();
    private static readonly ConditionalWeakTable<ShaderMaterial, NativeNifSceneAlphaMaterial> Borrowers = new();
    private readonly ShaderMaterial _material;
    private readonly Shader _original;
    private FadeProgram? _fade;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private uint? _lastBits;
    private uint? _lastMaterialBits;
    private bool _retired;
    internal NativeNifSceneAlphaMaterial(Material material)
    {
        if (material is not ShaderMaterial shader || shader.Shader is null || material.NextPass is not null ||
            material.ResourceName is not (NativeNifLightingMaterial.ResourceIdentity or NativeNifEffectMaterial.ResourceIdentity or NativeFaceGenMaterial.ResourceIdentity))
            throw new NotSupportedException("source-logical-scene-alpha-native-material-adapter-unowned:" + material.ResourceName);
        _material = shader; _original = shader.Shader;
        if (!Borrowers.TryAdd(shader, this)) throw new InvalidOperationException("Native scene alpha material already has an actual borrower.");
    }
    internal long Stores { get; private set; }
    internal uint? LastBits => _lastBits;
    internal void BorrowMaterialAlpha(uint bits)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != System.Environment.CurrentManagedThreadId || !GodotObject.IsInstanceValid(_material))
            throw new InvalidOperationException("Native material-alpha borrow changed its actual resource owner.");
        if (_lastMaterialBits == bits) return;
        var value = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(value)) throw new NotSupportedException("source-scene-nonfinite-material-alpha-renderer-consumer-unowned");
        var parameter = _material.ResourceName switch
        {
            NativeNifLightingMaterial.ResourceIdentity => "base_factor",
            NativeNifEffectMaterial.ResourceIdentity => "source_color_multiplier",
            _ => throw new NotSupportedException("source-logical-scene-current-material-alpha-renderer-layout-unowned"),
        };
        // RGB remains a presentation value. Only this independent alpha field
        // is borrowed from C#; it never feeds back into the source property.
        var color = _material.GetShaderParameter(parameter).AsVector4(); color.W = value;
        _material.SetShaderParameter(parameter, color); _lastMaterialBits = bits;
    }
    internal void Borrow(uint bits)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Native scene alpha borrow changed its actual resource thread.");
        if (!GodotObject.IsInstanceValid(_material) || !GodotObject.IsInstanceValid(_original))
            throw new ObjectDisposedException(nameof(NativeNifSceneAlphaMaterial));
        if (_lastBits == bits) return;
        var alpha = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(alpha)) throw new NotSupportedException("source-scene-nonfinite-alpha-renderer-consumer-unowned");
        if (bits == 0x3f800000)
            _material.Shader = _original;
        else
        {
            if (_fade is null)
            {
                _fade = FadeShaders.GetValue(_original, shader => new(shader));
                _fade.Users = checked(_fade.Users + 1);
            }
            _material.Shader = _fade.Shader;
            _material.SetShaderParameter(Parameter, alpha);
        }
        _lastBits = bits; Stores = checked(Stores + 1);
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Native scene alpha retirement changed its actual resource thread.");
        var fade = _fade; _fade = null;
        try
        {
            NativeNifSceneRetirement.Drain("Native scene alpha material", [RestoreShader, ReleaseFadeProgram]);
        }
        finally { Borrowers.Remove(_material); _retired = true; }
        void RestoreShader()
        {
            if (GodotObject.IsInstanceValid(_material))
                _material.Shader = GodotObject.IsInstanceValid(_original) ? _original : null;
        }
        void ReleaseFadeProgram()
        {
            if (fade is null) return;
            if (fade.Users < 1) throw new InvalidOperationException("Native scene fade program lost its actual borrower count.");
            if (--fade.Users != 0) return;
            FadeShaders.Remove(_original); fade.Shader.Dispose();
        }
    }
    internal static string FadeCode(string original)
    {
        const string declaration = "shader_type spatial;";
        const string fragment = "void fragment()";
        var stage = original.IndexOf(fragment, StringComparison.Ordinal);
        var open = stage < 0 ? -1 : original.IndexOf('{', stage);
        if (!original.Contains(declaration, StringComparison.Ordinal) || open < 0 ||
            original.Contains(Parameter, StringComparison.Ordinal))
            throw new NotSupportedException("Logical alpha requires an unchanged first-party spatial fragment shader.");
        var depth = 1; var end = open + 1;
        while (end < original.Length && depth != 0)
        {
            if (original[end] == '{') depth++;
            else if (original[end] == '}') depth--;
            end++;
        }
        if (depth != 0) throw new InvalidDataException("First-party material fragment has an incomplete extent.");
        var body = original.AsSpan(open + 1, end - open - 2);
        // Preserve the original fragment's coverage, including its independent
        // material/texture/vertex alpha. Opaque materials use a separate fade
        // variant only while the actual logical shader cell differs from one.
        var write = body.Contains("ALPHA", StringComparison.Ordinal)
            ? "\n    ALPHA *= " + Parameter + ";\n"
            : "\n    ALPHA = " + Parameter + ";\n";
        return original.Insert(end - 1, write).Replace(declaration,
            declaration + "\nuniform float " + Parameter + " = 1.0;", StringComparison.Ordinal);
    }
}
