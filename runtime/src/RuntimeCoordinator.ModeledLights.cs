using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record NativeReferenceModel(Node3D Instance, FalloutNifFile Source);
    private sealed record ModeledLightResource(string LogicalPath, string WinningSource, FalloutNifFile Model);
    private readonly Dictionary<string, ModeledLightResource> _nativeModeledLightSources = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeLiveContentSource? _nativeModeledLightContent;

    private NativeReferenceModel ConstructNativeReferenceModel(FalloutPlacedReference reference,
        FalloutBaseObjectDefinition basis, RuntimeLiveContentSource content, FalloutNifFile? preparedModel)
    {
        var path = basis.ModelPath ?? throw new InvalidDataException("Native reference model has no source MODL.");
        if (basis.Light is not null)
        {
            var records = _nativePluginStack ?? throw new InvalidOperationException("Modeled light has no source records.");
            var record = records.GetEffective(basis.FormKey);
            if (record.Signature != "LIGH") throw new InvalidDataException("Modeled emitter has a foreign winning base.");
            // These independent original reference-construction branches have
            // not acquired their script/random-clock producer in this owner.
            if ((record.Flags & 0x00010010) != 0)
                throw new NotSupportedException("Modeled light has an unowned flagged source construction child.");
            var model = ReadModeledLightSource(path, content, preparedModel);
            var scene = RuntimeNativeModeledLightBuilder.Build(reference, basis, model, ReferenceTransform(reference),
                _configuration.World.GameUnitsToMeters, _configuration.Renderer.PointLightEnergyScale,
                _configuration.Renderer.MinimumPointLightEnergy, _configuration.Renderer.AuthoredPointLightShadows,
                records, region => (_nativeSkyLighting ?? throw new InvalidOperationException("Light sky state is absent."))
                    .RegionEmittance(region, (_nativeGameTime ?? throw new InvalidOperationException("Light has no simulation clock.")).Hour), content);
            try
            {
                var state = _nativeReferences!.Get(reference.FormKey);
                RuntimeNativeModeledLightBuilder.ValidateObjectClocks(scene.Root,
                    state.CaptureObjectAnimations?.Invoke() ?? state.ObjectAnimations);
                GD.Print($"OPENNV_NATIVE_MODELED_LIGHT_CONSTRUCTED reference={reference.FormKey} model={path} " +
                    $"sha256={model.Sha256} selectedPoint={scene.Root.GetMeta("opennv_ligh_selected_point").AsInt32()} " +
                    $"nodes={scene.Nodes} surfaces={scene.Surfaces} scale={reference.Scale:R} " +
                    "clock=shared-reference-object-animations radiusOwner=LIGH+XRDS-unscaled pixels=unverified");
                return new(scene.Root, model);
            }
            catch (Exception primary)
            {
                try { scene.Root.Free(); }
                catch (Exception retirement) { throw new AggregateException("Modeled light source continuation and retirement failed.", primary, retirement); }
                throw;
            }
        }
        if (!_nativeNifPrototypes.TryGetValue(path, out var prototype))
        {
            if (!content.TryRead(path, null, out var bytes, out var winningSource))
                throw new FileNotFoundException($"Winning model {path} for {basis.FormKey} is missing.");
            GD.Print($"OPENNV_NATIVE_NIF_LOADING model={path} source={winningSource} base={basis.FormKey}");
            if (path.EndsWith(".spt", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Owned SpeedTree model {path} is present at {winningSource}; its procedural geometry decoder is unbound.");
            prototype = preparedModel is null ? new RuntimeNativeNifPrototype(bytes, _configuration.World.GameUnitsToMeters) :
                new RuntimeNativeNifPrototype(preparedModel, _configuration.World.GameUnitsToMeters);
            var built = prototype.Scene;
            prototype.Scene.Root.Name = $"Prototype_{basis.FormKey}";
            _nativeNifPrototypes.Add(path, prototype);
            GD.Print($"OPENNV_NATIVE_NIF_READY model={path} source={winningSource} " +
                $"nodes={built.Nodes} surfaces={built.Surfaces} vertices={built.Vertices} triangles={built.Triangles} " +
                $"collisionBodies={built.CollisionBodies} collisionShapes={built.CollisionShapes} collisionTriangles={built.CollisionTriangles}");
        }
        return new(prototype.InstantiatePlaced(ReferenceTransform(reference)), prototype.Source);
    }

    private FalloutNifFile ReadModeledLightSource(string path, RuntimeLiveContentSource content, FalloutNifFile? prepared)
    {
        if (_nativeModeledLightContent is not null && !ReferenceEquals(_nativeModeledLightContent, content))
            throw new InvalidOperationException("Modeled light cache belongs to a different living source selection.");
        var key = path.Replace('\\', '/');
        if (_nativeModeledLightSources.TryGetValue(key, out var saved))
        {
            if (prepared is not null && !saved.Model.Sha256.Equals(prepared.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Prepared light model differs from its retained winning source.");
            return saved.Model;
        }
        if (!content.TryRead(path, null, out var bytes, out var winner))
            throw new FileNotFoundException($"Winning LIGH model {path} is missing.");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (prepared is not null && !prepared.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prepared light model differs from its actual winning resource.");
        var model = prepared ?? FalloutNifFile.Read(bytes);
        _nativeModeledLightSources.Add(key, new(path, winner, model));
        _nativeModeledLightContent = content;
        return model;
    }

    private IEnumerable<RuntimeNativePlacedLight> NativePlacedLightNodes() => _nativeCurrentCellRoot is { } root ?
        NodeTraversal.SelfAndDescendants<RuntimeNativePlacedLight>(root) : [];

    private void PruneNativeModeledLightSources(IReadOnlySet<string> retained)
    {
        var keys = retained.Select(path => path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _nativeModeledLightSources.Keys.Where(key => !keys.Contains(key)).ToArray())
            _nativeModeledLightSources.Remove(key);
        if (_nativeModeledLightSources.Count == 0) _nativeModeledLightContent = null;
    }

    private void RetireNativeModeledLightSources()
    {
        _nativeModeledLightSources.Clear();
        _nativeModeledLightContent = null;
    }
}
