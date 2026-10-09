using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedInterfaceFadeQuad : Control
{
    private readonly Action<Exception> _failed;
    private Texture2D? _texture;
    private NativeViewportLayout? _layout;
    private float _opacity;
    private long _revision;
    private bool _retiring;
    private Exception? _publicationFailure;
    private readonly ulong _nativeId;
    private readonly ulong _textureId;
    internal int Channel { get; }
    internal long Generation { get; }
    internal long? SubmittedRevision { get; private set; }
    internal object State => new
    {
        Channel,
        Generation,
        opacity = _opacity,
        revision = _revision,
        SubmittedRevision,
        native = _nativeId,
        texture = _textureId,
        retiring = _retiring,
        sourceTexture = _texture?.GetMeta("opennv_owned_media_path").AsString()
    };

    private NativeOwnedInterfaceFadeQuad(FalloutInterfaceFadeChannel channel, Texture2D texture, Action<Exception> failed)
    {
        Channel = channel.Channel; Generation = channel.Generation; _texture = texture; _failed = failed;
        _nativeId = GetInstanceId(); _textureId = texture.GetInstanceId();
        _opacity = channel.Opacity; _revision = channel.Revision;
        Name = "SourceInterfaceFadeChannel" + Channel; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
    }
    internal static NativeOwnedInterfaceFadeQuad Attach(Control root, RuntimeLiveContentSource source,
        FalloutInterfaceFadeTexture declaration, FalloutInterfaceFadeChannel channel, Action<Exception> failed)
    {
        if (!root.IsInsideTree() || channel.Direction == FalloutInterfaceFadeDirection.Absent || declaration.Channel != channel.Channel)
            throw new InvalidOperationException("Interface fade has no actual live source geometry request.");
        if (!source.TryRead(declaration.LogicalPath, null, out var bytes, out var provenance) ||
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != declaration.BytesSha256)
            throw new InvalidDataException("Interface fade winning texture disappeared or changed after source admission.");
        // Decode/upload only the current owned winner. No placeholder color or
        // generated texture can fulfill this original material declaration.
        var texture = NativeDdsTexture.Load(bytes, provenance);
        NativeOwnedInterfaceFadeQuad? result = null;
        try
        {
            texture.SetMeta("opennv_owned_media_path", declaration.LogicalPath);
            texture.SetMeta("opennv_owned_media_sha256", declaration.BytesSha256);
            result = new(channel, texture, failed);
            root.AddChild(result);
            if (result.GetParent() != root || !result.IsInsideTree() || result._publicationFailure is not null)
                throw new InvalidOperationException("Owned interface fade geometry failed attachment.", result._publicationFailure);
            return result;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { if (result is null) texture.Dispose(); else result.Retire(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Owned interface fade construction and disposal failed.", failures);
        }
    }
    public override void _EnterTree()
    {
        try { _layout = new(this, Layout); Layout(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _publicationFailure ??= error; _failed(error); }
    }
    private void Layout()
    {
        if (_retiring) return;
        try
        {
            var size = GetViewportRect().Size;
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0)
                throw new NotSupportedException("Source interface fade has no positive current viewport dimensions.");
            if (GetParent() is not Control root || root.GetGlobalTransform() != Transform2D.Identity)
                throw new NotSupportedException("Source interface fade root requires another transform/viewport geometry consumer.");
            Position = Vector2.Zero; Size = size; QueueRedraw();
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _publicationFailure ??= error; _failed(error); }
    }
    internal void RequireGeneration(FalloutInterfaceFadeChannel channel)
    {
        if (_retiring || channel.Channel != Channel || channel.Generation != Generation)
            throw new InvalidOperationException("Interface fade geometry is not the original source generation.");
    }
    internal void Write(FalloutInterfaceFadeChannel channel)
    {
        RequireGeneration(channel);
        if (!IsInsideTree() || channel.Revision < _revision || !float.IsFinite(channel.Opacity) || channel.Opacity is < 0 or > 1)
            throw new InvalidOperationException("Interface fade native opacity differs from its living source clock.");
        _revision = channel.Revision; _opacity = channel.Opacity; QueueRedraw();
    }
    public override void _Draw()
    {
        if (_retiring || _texture is null) return;
        try
        {
            DrawTextureRect(_texture, new(Vector2.Zero, Size), false, new Color(1, 1, 1, _opacity));
            SubmittedRevision = _revision;
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _failed(error); }
    }
    internal void Retire()
    {
        if (_retiring) return;
        _retiring = true;
        var failures = new List<Exception>();
        void Release(Action release)
        {
            try { release(); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
        }
        var layout = _layout; _layout = null; if (layout is not null) Release(layout.Dispose);
        var texture = _texture; _texture = null;
        Release(() => { if (GodotObject.IsInstanceValid(this)) Free(); });
        if (texture is not null) Release(texture.Dispose);
        if (failures.Count != 0) throw new AggregateException("Owned interface fade geometry/material retirement failed.", failures);
    }
    public override void _ExitTree()
    {
        var layout = _layout; _layout = null;
        try { layout?.Dispose(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { _failed(error); }
        if (!_retiring)
            _failed(new InvalidOperationException("Source interface fade geometry lost its root before authoritative retirement."));
    }
}
