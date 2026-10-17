using Godot;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativePlacedLight : OmniLight3D
{
    private float[]? _lastColor;
    private FalloutSourcePointLight? _sourceFields;
    private Func<FalloutSourcePointLight, IDisposable>? _attachScene;
    private IDisposable? _sceneMembership;
    private bool _dynamicColor;
    internal FalloutSourcePointLight SourceFields => _sourceFields ??
        throw new InvalidOperationException("Placed light has no actual C# source fields.");
    internal void ConfigureSourceFields(FalloutSourcePointLight fields)
    {
        if (_sourceFields is not null) throw new InvalidOperationException("Placed light source fields are already constructed.");
        _sourceFields = fields;
    }
    internal void BindSourceScene(Func<FalloutSourcePointLight, IDisposable> attach)
    {
        if (_attachScene is not null || IsInsideTree()) throw new InvalidOperationException("Light scene binding must precede real attachment.");
        _attachScene = attach;
    }
    internal void ConfigureRegionColor(Func<float[]> color)
    { SourceFields.ConfigureColor(color); _dynamicColor = true; }
    public override void _EnterTree()
    {
        if (_sceneMembership is not null) throw new InvalidOperationException("Placed light already owns a live scene membership.");
        _sceneMembership = _attachScene?.Invoke(SourceFields);
    }
    public override void _ExitTree()
    {
        _sceneMembership?.Dispose(); _sceneMembership = null;
    }

    public override void _Process(double delta)
    {
        if (!_dynamicColor) return;
        try
        {
            PublishSourceDiffuse();
        }
        catch (Exception error)
        {
            SetMeta("opennv_ligh_error", error.Message);
            SetProcess(false);
            GetTree().Paused = true;
            GD.PushError($"OPENNV_LIGHT_EMITTANCE_UNBOUND {Name}: {error.Message}");
        }
    }
}
