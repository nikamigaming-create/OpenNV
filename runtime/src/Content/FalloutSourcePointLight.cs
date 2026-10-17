using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// Mutable source fields are shared by the real placed-light adapter and the
// gameplay light consumer. Render energy, visibility and Godot transforms are
// independent presentation values and are never read back into this owner.
internal sealed class FalloutSourcePointLight
{
    private readonly FalloutPlacedReference _reference;
    private readonly FalloutPlacedLight _parameters;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly FalloutModeledLightPose? _model;
    private float[] _diffuse;
    private Func<float[]>? _emittance;
    private Func<float[]>? _color;
    private Func<FalloutReferencePlacement>? _placement;
    private Func<bool>? _enabled;
    private Guid? _registration;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutFormKey Reference => _reference.FormKey;
    internal float? LastContribution { get; private set; }
    internal long ContributionStores { get; private set; }
    internal FalloutModeledLightPose? ModelPose => _model;

    internal FalloutSourcePointLight(FalloutPlacedReference reference, FalloutPlacedLight parameters,
        FalloutModeledLightPose? model = null)
    {
        if (reference.FormKey != parameters.Reference || reference.Base != parameters.Base ||
            reference.Position.Length != 3 || reference.RotationRadians.Length != 3 ||
            reference.Position.Concat(reference.RotationRadians).Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(reference.Scale) || reference.Scale <= 0 || !float.IsFinite(parameters.RadiusGameUnits) ||
            parameters.RadiusGameUnits <= 0 || !float.IsFinite(parameters.Intensity))
            throw new InvalidDataException("Source point light changed its actual reference, radius or dimmer.");
        _reference = reference with { Position = reference.Position.ToArray(), RotationRadians = reference.RotationRadians.ToArray() };
        _parameters = parameters; _diffuse = Channels(parameters.ShaderColorRgb); _model = model;
    }
    internal void ConfigureColor(Func<float[]> color)
    { RequireThread(); ArgumentNullException.ThrowIfNull(color); _color = color; }
    internal void ConfigureDiffuse(IReadOnlyList<float> diffuse, Func<float[]>? emittance)
    { RequireThread(); _color = null; _emittance = emittance; _diffuse = Channels(diffuse); }
    internal void StoreDiffuse(FalloutNifColor3 value)
    { RequireThread(); _diffuse = Channels([value.R, value.G, value.B]); }
    internal float[] ReadDiffuse()
    {
        RequireThread();
        if (_color is not null) return Channels(_color());
        var color = _diffuse.ToArray();
        if (_emittance is not null)
        {
            var emittance = Channels(_emittance());
            for (var channel = 0; channel < color.Length; channel++) color[channel] *= emittance[channel];
        }
        return Channels(color);
    }
    internal void Bind(Guid registration, Func<FalloutReferencePlacement> placement, Func<bool> enabled)
    {
        RequireThread(); ArgumentNullException.ThrowIfNull(placement); ArgumentNullException.ThrowIfNull(enabled);
        if (registration == Guid.Empty || _registration is not null)
            throw new InvalidOperationException("Source point light already has a current scene registration.");
        _registration = registration; _placement = placement; _enabled = enabled;
    }
    internal void Unbind(Guid registration)
    {
        RequireThread();
        if (_registration != registration) throw new InvalidOperationException("Point light retired a different scene registration.");
        _registration = null; _placement = null; _enabled = null;
    }
    internal FalloutDetectionLightInput Read(Guid registration)
    {
        RequireThread();
        if (_registration != registration || _placement is null || _enabled is null)
            throw new InvalidOperationException("Point light has no current source scene membership.");
        var placement = _placement(); placement.Validate();
        var local = _model?.ReadPoint().Translation ?? new FalloutNifVector3(0, 0, 0);
        var matrix = FalloutModeledLightPose.ReferenceRotation(placement.RotationRadians);
        var offset = FalloutNifSceneMath.Rotate(local, matrix);
        var position = new[] { placement.Position[0] + offset.X * _reference.Scale,
            placement.Position[1] + offset.Y * _reference.Scale, placement.Position[2] + offset.Z * _reference.Scale };
        return new(position, ReadDiffuse(), _parameters.Intensity, _parameters.RadiusGameUnits, true, false, false);
    }
    internal bool ReadEnabled(Guid registration)
    {
        RequireThread();
        if (_registration != registration || _enabled is null)
            throw new InvalidOperationException("Point enable read has no actual current scene membership.");
        return _enabled();
    }
    internal void StoreContribution(Guid registration, float value)
    {
        RequireThread();
        if (_registration != registration || !float.IsFinite(value))
            throw new InvalidOperationException("Light contribution changed its actual source wrapper or finite value.");
        LastContribution = value; ContributionStores = checked(ContributionStores + 1);
    }
    private void RequireThread()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Source light crossed its actual calling thread.");
    }
    private static float[] Channels(IReadOnlyList<float> values) => values.Count == 3 && values.All(float.IsFinite)
        ? values.ToArray() : throw new InvalidDataException("Source light needs three finite current color channels.");
}
