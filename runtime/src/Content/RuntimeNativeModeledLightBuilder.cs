using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal static class RuntimeNativeModeledLightBuilder
{
    internal static RuntimeNativeNifScene Build(FalloutPlacedReference reference, FalloutBaseObjectDefinition basis,
        FalloutNifFile model, Transform3D placement, float units, float energyScale, float minimumEnergy,
        bool shadows, FalloutPluginStack? records = null, Func<FalloutFormKey, float[]>? regionEmittance = null,
        RuntimeLiveContentSource? content = null, Action<RuntimeNativePlacedLight>? configureLight = null)
    {
        var binding = FalloutModeledLightSource.Read(model, basis.ModelPath ??
            throw new InvalidDataException("Modeled light has no source MODL."));
        var source = FalloutPlacedLightResolver.ResolveModeled(reference, basis, binding, records, regionEmittance);
        var pose = new FalloutModeledLightPose(binding);
        var emittance = reference.Emittance is { } form ? new FalloutExternalEmittance(records ??
            throw new InvalidDataException("Modeled light has no XEMI records."), form, regionEmittance) : null;
        RuntimeNativePlacedLight Create(Transform3D transform)
        {
            var point = RuntimeNativePlacedLightBuilder.BuildResolved(source, reference, basis, transform, units,
                energyScale, minimumEnergy, shadows, records, regionEmittance, pose);
            try
            {
                point.ConfigureSourceDiffuse(FalloutPlacedLightResolver.NormalizeModeledRecordColor(source.ColorRgb),
                    emittance is null ? null : emittance.Sample);
                point.SetMeta("opennv_ligh_model_sha256", model.Sha256);
                point.SetMeta("opennv_ligh_source_radius_owner", "LIGH+XRDS-unscaled");
                point.SetMeta("opennv_ligh_rendering_boundary", "calibrated-light-remap;custom-attenuation-pixels-unverified");
                configureLight?.Invoke(point);
                return point;
            }
            catch (Exception primary)
            {
                try { point.Free(); }
                catch (Exception retirement) { throw new AggregateException("Point setup and detached retirement failed.", primary, retirement); }
                throw;
            }
        }
        var scene = RuntimeNativeNifMeshBuilder.Build(model, units, contentSource: content,
            pointLightOwner: new(binding, Create, pose));
        try
        {
            // A source node root is replaced by REFR. A non-node export is
            // wrapped by the ordinary source factory and keeps its local pose.
            if (binding.ReplaceNodeRootTransform)
            {
                var root = scene.Root.GetChild<Node3D>(0);
                root.SetMeta("opennv_nif_authored_root_transform", root.Transform);
                root.SetMeta("opennv_nif_root_transform_owner", "placed-reference");
                root.Transform = Transform3D.Identity;
            }
            scene.Root.Transform = placement;
            scene.Root.SetMeta("opennv_source_model", basis.ModelPath!);
            scene.Root.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            scene.Root.SetMeta("opennv_source_form", basis.FormKey.ToString());
            scene.Root.SetMeta("opennv_ligh_selected_point", binding.Point.Block.Index);
            scene.Root.SetMeta("opennv_ligh_reachable_points", binding.ReachablePoints.ToArray());
            scene.Root.SetMeta("opennv_ligh_clock_owner", "shared-reference-object-animations");
            RequireRestorableClocks(scene.Root);
            return scene;
        }
        catch (Exception primary)
        {
            try { scene.Root.Free(); }
            catch (Exception retirement) { throw new AggregateException("Modeled light placement and detached retirement failed.", primary, retirement); }
            throw;
        }
    }

    internal static void RestoreObjectClocks(Node3D root, IReadOnlyList<FalloutObjectAnimationSnapshot>? snapshots)
    {
        var bindings = ObjectClockBindings(root, snapshots);
        foreach (var (player, state) in bindings) player.RestoreObjectState(state);
    }

    internal static void ValidateObjectClocks(Node3D root, IReadOnlyList<FalloutObjectAnimationSnapshot>? snapshots)
        => _ = ObjectClockBindings(root, snapshots);

    private static (RuntimeNifControllerPlayer Player, FalloutObjectAnimationSnapshot State)[] ObjectClockBindings(
        Node3D root, IReadOnlyList<FalloutObjectAnimationSnapshot>? snapshots)
    {
        RequireRestorableClocks(root);
        var players = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
        var states = snapshots ?? [];
        foreach (var state in states) state.Validate();
        if (states.GroupBy(state => (state.Controller, state.Sha256.ToUpperInvariant())).Any(group => group.Count() != 1))
            throw new InvalidDataException("Modeled light has duplicate saved source clock owners.");
        var bindings = states.Select(state =>
        {
            var player = players.SingleOrDefault(value => value.SourceController == state.Controller &&
                value.SourceSha256.Equals(state.Sha256, StringComparison.OrdinalIgnoreCase)) ??
                throw new NotSupportedException("Modeled light saved clock differs from its current source manager.");
            player.ValidateObjectState(state);
            return (Player: player, State: state);
        }).ToArray();
        // Validate the complete suffix before shared presentation publication
        // restores its clocks. Construction must not consume this state twice.
        return bindings;
    }

    private static void RequireRestorableClocks(Node3D root)
    {
        foreach (var player in root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>())
            if (player.SourceController < 0 && player.ActiveSequence is not null && !player.CompletedDirectInitialization)
                throw new NotSupportedException("Modeled light active direct clock has no current/cold reference owner.");
    }
}
