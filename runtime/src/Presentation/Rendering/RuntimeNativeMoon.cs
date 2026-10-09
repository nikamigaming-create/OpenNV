using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

// The generated meshes and their two original property fields are independent
// of shader admission. An unbound active draw refuses before either mesh is
// published; it never becomes a default Godot material or a white Moon.
internal sealed partial class RuntimeNativeMoon : IFalloutNativeMoon
{
    private readonly RuntimeLiveContentSource _source;
    private readonly Node3D _host;
    private readonly FalloutMoonSettings _settings;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, (Texture2D Texture, FalloutMoonTexture Source)> _textures = new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _parent, _primaryChild, _shadowChild;
    private MeshInstance3D? _primary, _shadow;
    private ArrayMesh? _primaryMesh, _shadowMesh;
    private ShaderMaterial? _primaryProperty, _shadowProperty;
    private bool _primaryHasTexture, _shadowHasTexture, _retired;
    private FalloutMoonTexture? _primaryTexture, _shadowTexture;
    private uint _primaryAlpha = BitConverter.SingleToUInt32Bits(1f), _shadowAlpha = BitConverter.SingleToUInt32Bits(1f);
    private readonly float _units;
    public Guid Identity { get; }
    public FalloutMoonRole Role { get; }
    private RuntimeNativeMoon(FalloutMoonSnapshot row, RuntimeLiveContentSource source, Node3D host, float units)
    {
        if (row.CapturedIdentity == Guid.Empty || !float.IsFinite(units) || units <= 0)
            throw new InvalidDataException("Moon scene omitted actual source identity/unit ownership.");
        Identity = row.CapturedIdentity; Role = row.Role; _settings = row.Settings; _source = source; _host = host; _units = units;
    }
    internal static IFalloutNativeMoon Create(FalloutSkyMoonState state, FalloutMoonSnapshot row, int mode,
        RuntimeLiveContentSource source, Node3D host, float units,
        Func<int, ShaderMaterial> constructSourceProperty)
    {
        ArgumentNullException.ThrowIfNull(constructSourceProperty);
        var child = new RuntimeNativeMoon(row, source, host, units);
        try { child.Construct(row, mode, constructSourceProperty); return child; }
        catch (Exception original)
        {
            try { child.Retire(); }
            catch (Exception cleanup)
            {
                var retained = new AggregateException("Moon native construction and actual resource retirement failed independently.", original, cleanup);
                state.RetainFailedFactory(child, retained); throw retained;
            }
            throw;
        }
    }
    private void Require(bool retirement = false)
    {
        if (_retired || System.Environment.CurrentManagedThreadId != _thread ||
            !retirement && (!GodotObject.IsInstanceValid(_host) || !_host.IsInsideTree() || _host.IsQueuedForDeletion()))
            throw new InvalidOperationException("Moon scene lost its actual native parent/thread lifetime.");
    }
    private void Construct(FalloutMoonSnapshot row, int mode, Func<int, ShaderMaterial> constructSourceProperty)
    {
        Require(); _settings.Validate();
        _parent = new Node3D { Name = Role + "Moon" };
        _primaryChild = new Node3D { Name = "Primary" }; _shadowChild = new Node3D { Name = "Shadow" };
        _primaryMesh = Geometry(FalloutMoonMath.Geometry(_settings.Size, shadow: false));
        _shadowMesh = Geometry(FalloutMoonMath.Geometry(_settings.Size, shadow: true));
        // Register every resource before the next fallible child. Both original
        // properties have separate native owners and distinct Sky roles.
        _primaryProperty = constructSourceProperty(6) ?? throw new InvalidDataException("Moon primary property returned no actual source material.");
        _shadowProperty = constructSourceProperty(7) ?? throw new InvalidDataException("Moon shadow property returned no actual source material.");
        if (_primaryProperty.GetInstanceId() == _shadowProperty.GetInstanceId() ||
            !GodotObject.IsInstanceValid(_primaryProperty.Shader) || !GodotObject.IsInstanceValid(_shadowProperty.Shader))
            throw new InvalidDataException("Moon properties alias or omit an actual admitted shader owner.");
        RequireRole(_primaryProperty, 6); RequireRole(_shadowProperty, 7);
        _primaryProperty.SetShaderParameter("source_Moon_alpha", 1f);
        _shadowProperty.SetShaderParameter("source_Moon_alpha", 1f);
        BindSampler(_primaryProperty, null); BindSampler(_shadowProperty, null);
        _primary = new MeshInstance3D
        {
            Name = "PrimaryQuad",
            Mesh = _primaryMesh,
            MaterialOverride = _primaryProperty,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _shadow = new MeshInstance3D
        {
            Name = "ShadowQuad",
            Mesh = _shadowMesh,
            MaterialOverride = _shadowProperty,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        // Source (x,y,z) -> Godot (x,z,-y). Source quad translation is
        // (0,512,0), and its source rotation is minus half-pi about X.
        var rotation = new Basis(Vector3.Right, -MathF.PI / 2f);
        _primary.Transform = new(rotation, new(0, 0, -FalloutMoonSource.QuadDistance * _units));
        _shadow.Transform = _primary.Transform;
        _shadowChild.AddChild(_shadow); _primaryChild.AddChild(_primary);
        _parent.AddChild(_shadowChild); _parent.AddChild(_primaryChild);
        if (row.Native is { } saved) RestoreFields(saved);
        else if (mode is 2 or 3)
        {
            _shadowTexture = ReadTexture(FalloutMoonSource.ShadowTexture);
            BindSampler(_shadowProperty, _shadowTexture); _shadowHasTexture = true;
        }
        _host.AddChild(_parent);
        _ = Capture();
    }
    private static void RequireRole(ShaderMaterial property, int role)
    {
        if (!property.HasMeta("opennv_source_Moon_program") ||
            property.GetMeta("opennv_source_Moon_program").AsString().Length != 64 ||
            !property.HasMeta("opennv_sky_object_type") || property.GetMeta("opennv_sky_object_type").AsInt32() != role)
            throw new NotSupportedException("source-Moon-actual-shader-technique-constant-and-render-state-owner-unbound");
    }
    private ArrayMesh Geometry(FalloutMoonMeshInputs input)
    {
        var mesh = new ArrayMesh();
        try
        {
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = input.Vertices.Select(value => new Vector3(value.X, value.Z, -value.Y) * _units).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = input.Uv.Select(value => new Vector2(value.X, value.Y)).ToArray();
            arrays[(int)Mesh.ArrayType.Color] = input.Colors.Select(value => new Color(value.X, value.Y, value.Z, value.W)).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = input.Triangles;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (mesh.GetSurfaceCount() != 1) throw new InvalidDataException("Moon generated quad omitted its actual surface.");
            return mesh;
        }
        catch { mesh.Dispose(); throw; }
    }
    public FalloutMoonNativeFields Capture()
    {
        Require();
        if (_parent is null || _primaryChild is null || _shadowChild is null || _primary is null || _shadow is null ||
            _primaryProperty is null || _shadowProperty is null || _primaryMesh is null || _shadowMesh is null ||
            !GodotObject.IsInstanceValid(_parent) || !GodotObject.IsInstanceValid(_primary) || !GodotObject.IsInstanceValid(_shadow))
            throw new InvalidOperationException("Moon native factory has not published every actual source field.");
        if (_primary.Mesh?.GetInstanceId() != _primaryMesh.GetInstanceId() || _shadow.Mesh?.GetInstanceId() != _shadowMesh.GetInstanceId() ||
            _primary.MaterialOverride?.GetInstanceId() != _primaryProperty.GetInstanceId() ||
            _shadow.MaterialOverride?.GetInstanceId() != _shadowProperty.GetInstanceId())
            throw new InvalidDataException("Moon geometry lost its exact mesh/property owners.");
        RequireSampler(_primaryProperty, _primaryTexture); RequireSampler(_shadowProperty, _shadowTexture);
        RequireAlpha(_primaryProperty, _primaryAlpha); RequireAlpha(_shadowProperty, _shadowAlpha);
        return new(_parent.GetParent() == _host && _parent.IsInsideTree(), _primaryChild.GetParent() == _parent,
            _shadowChild.GetParent() == _parent, _primary.GetParent() == _primaryChild, _shadow.GetParent() == _shadowChild,
            _primaryHasTexture, _shadowHasTexture, _primaryTexture, _shadowTexture, !_parent.Visible,
            !_primaryChild.Visible, !_shadowChild.Visible, _primaryAlpha, _shadowAlpha);
    }
    private static void RequireAlpha(ShaderMaterial property, uint expected)
    {
        using var value = property.GetShaderParameter("source_Moon_alpha");
        if (value.VariantType != Variant.Type.Float || BitConverter.SingleToUInt32Bits((float)value.AsDouble()) != expected)
            throw new InvalidDataException("Moon property changed its actual source Float32 alpha field.");
    }
    public void Apply(FalloutMoonFrame frame, float angle, float primaryAlpha, float shadowAlpha)
    {
        Require(); _ = Capture();
        if (frame.SkyMode is not (2 or 3)) return; // Actual source draw-field early return, not a success receipt.
        throw new NotSupportedException("source-Moon-active-parent-matrix-shader-constant-and-NightSky-property-writer-unowned");
    }
}
