using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed partial class RuntimeNativeMoon
{
    private FalloutMoonTexture ReadTexture(string path)
    {
        if (_textures.TryGetValue(path, out var old)) return old.Source;
        if (!_source.TryRead(path, null, out var bytes, out var source)) throw new FileNotFoundException("Owned Moon DDS is absent.", path);
        var declaration = new FalloutMoonTexture(path, source, Convert.ToHexString(SHA256.HashData(bytes)));
        var texture = NativeDdsTexture.Load(bytes, source);
        _textures.Add(path, (texture, declaration)); return declaration;
    }
    private void BindSampler(ShaderMaterial property, FalloutMoonTexture? texture)
    {
        property.SetShaderParameter("source_Moon_sampler", texture is null ? default(Variant) : _textures[texture.Path].Texture);
        RequireSampler(property, texture);
    }
    private void RequireSampler(ShaderMaterial property, FalloutMoonTexture? texture)
    {
        using var sampler = property.GetShaderParameter("source_Moon_sampler");
        if (texture is null)
        {
            if (sampler.VariantType != Variant.Type.Nil) throw new InvalidDataException("Moon property retained an undeclared native sampler.");
            return;
        }
        if (!_textures.TryGetValue(texture.Path, out var owned) || owned.Source != texture || sampler.AsGodotObject() is not Texture2D native ||
            !GodotObject.IsInstanceValid(native) || native.GetInstanceId() != owned.Texture.GetInstanceId())
            throw new InvalidDataException("Moon property sampler changed its exact source-owned native texture.");
    }
    private void RestoreFields(FalloutMoonNativeFields saved)
    {
        foreach (var declared in new[] { saved.PrimaryTexture, saved.ShadowTexture }.Where(texture => texture is not null))
            if (ReadTexture(declared!.Path) != declared) throw new InvalidDataException("Cold Moon DDS changed its winning bytes/source.");
        _primaryTexture = saved.PrimaryTexture; _shadowTexture = saved.ShadowTexture;
        BindSampler(_primaryProperty!, _primaryTexture); BindSampler(_shadowProperty!, _shadowTexture);
        _primaryHasTexture = saved.PrimaryHasTexture; _shadowHasTexture = saved.ShadowHasTexture;
        _parent!.Visible = !saved.ParentHidden; _primaryChild!.Visible = !saved.PrimaryHidden; _shadowChild!.Visible = !saved.ShadowHidden;
        _primaryAlpha = saved.PrimaryAlphaBits; _shadowAlpha = saved.ShadowAlphaBits;
        _primaryProperty!.SetShaderParameter("source_Moon_alpha", BitConverter.UInt32BitsToSingle(_primaryAlpha));
        _shadowProperty!.SetShaderParameter("source_Moon_alpha", BitConverter.UInt32BitsToSingle(_shadowAlpha));
    }
    public FalloutMoonNativeTextureReturn LoadPhase(string? path)
    {
        Require(); _ = Capture();
        if (path is null) { _primaryHasTexture = false; return new(Identity, null, false); }
        var next = ReadTexture(path);
        BindSampler(_primaryProperty!, next); _primaryTexture = next; _primaryHasTexture = true;
        return new(Identity, next, true);
    }
    public void Retire()
    {
        if (_retired) return; Require(retirement: true);
        var failures = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception failure) { failures.Add(failure); } }
        foreach (var property in new[] { _primaryProperty, _shadowProperty }.Where(value => value is not null))
            Attempt(() => { property!.SetShaderParameter("source_Moon_sampler", default(Variant)); RequireSampler(property, null); });
        // Retire the actual node borrowers before releasing resources. Detached
        // partial constructors and a whole-root Free follow the same owner.
        foreach (var node in new Node?[] { _primary, _shadow, _primaryChild, _shadowChild, _parent })
            Attempt(() => { if (node is not null && GodotObject.IsInstanceValid(node)) node.Free(); });
        void Release<T>(ref T? resource) where T : RefCounted
        {
            if (resource is null) return;
            if (!GodotObject.IsInstanceValid(resource) || resource.GetReferenceCount() != 1)
                throw new InvalidOperationException("Moon resource retains an actual native/foreign borrower.");
            resource.Dispose(); resource = null;
        }
        Attempt(() => Release(ref _primaryProperty)); Attempt(() => Release(ref _shadowProperty));
        Attempt(() => Release(ref _primaryMesh)); Attempt(() => Release(ref _shadowMesh));
        foreach (var (path, owned) in _textures.ToArray())
            Attempt(() =>
            {
                if (!GodotObject.IsInstanceValid(owned.Texture) || owned.Texture.GetReferenceCount() != 1)
                    throw new InvalidOperationException("Moon DDS retains an actual native/foreign borrower: " + path);
                owned.Texture.Dispose(); _textures.Remove(path);
            });
        if (failures.Count != 0) throw new AggregateException("Moon retirement retained every actual resource/child failure.", failures);
        _primaryTexture = null; _shadowTexture = null; _parent = null; _primaryChild = null; _shadowChild = null;
        _primary = null; _shadow = null; _retired = true;
    }
}
