using System.Runtime.CompilerServices;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceSharedShaderIdentity(Guid Identity, Guid ModelFactory, Guid LogicalLifetime,
    FalloutNifSceneKey Property, string Type, int Kind);
internal sealed class FalloutSourceSharedShaderProperty
{
    private static readonly ConditionalWeakTable<FalloutNifSceneProperty, FalloutSourceSharedShaderProperty> Owners = new();
    private readonly FalloutNifSceneProperty _property;
    private int _leases;
    internal FalloutSourceSharedShaderIdentity Source { get; }
    internal FalloutNifSceneProperty Property => _property;
    internal int CommonLeases => _leases;
    // Source LightingProperty construction clears its independent light list.
    // Scene light wrappers belong to the separate scene collection. This
    // current list receives only the property's actual association writer.
    private readonly LinkedList<FalloutSourcePointLight> _lights = new();
    internal Guid? ReadFirstLight() => _lights.First?.Value.Identity;
    private FalloutSourceSharedShaderProperty(FalloutActorLogicalScene model, FalloutNifSceneProperty property)
    {
        if (property.ShaderKind is not (>= 7 and <= 11) || !model.Properties.Any(value => ReferenceEquals(value, property)))
            throw new InvalidDataException("Common shader field needs the actual selected mutable source property.");
        _property = property; Source = new(Guid.NewGuid(), model.Factory, model.Lifetime, property.Key,
            property.Source.Block.TypeName, property.ShaderKind.Value);
    }
    internal static FalloutSourceSharedShaderProperty From(FalloutActorLogicalScene model, FalloutNifSceneProperty property)
    {
        var result = Owners.GetValue(property, _ => new(model, property));
        if (result.Source.ModelFactory != model.Factory || result.Source.LogicalLifetime != model.Lifetime)
            throw new InvalidDataException("Shared shader property crossed its real source model lifetime.");
        return result;
    }
    internal Lease Retain()
    { _leases = checked(_leases + 1); return new(this); }
    internal sealed class Lease(FalloutSourceSharedShaderProperty resource) : IDisposable
    {
        private bool _retired;
        internal FalloutSourceSharedShaderProperty Resource { get { ObjectDisposedException.ThrowIf(_retired, this); return resource; } }
        public void Dispose()
        {
            if (_retired) return;
            if (resource._leases < 1) throw new InvalidOperationException("Shared shader released a foreign source-field lease.");
            --resource._leases; _retired = true;
        }
    }
}
