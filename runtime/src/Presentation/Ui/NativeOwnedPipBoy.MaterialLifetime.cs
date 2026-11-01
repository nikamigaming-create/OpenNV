using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoy
{
    /// <summary>Menu illumination borrows a persistent XR device only while focused.</summary>
    private sealed class BorrowedDeviceMaterials
    {
        private static readonly string[] EnvironmentParameters =
        ["source_ambient", "source_fog_color", "source_fog_range", "source_fog_game_units_per_meter", "source_exterior_environment"];
        private sealed record MeshState(MeshInstance3D Mesh, Material? Override, Material?[] Surfaces,
            Variant[] Environment, bool? Visible);
        private MeshState[] _world;
        private MeshState[]? _menu;
        private bool _active;
        internal object State => new { active = _active, meshes = _world.Length, owner = "focused-menu-only;borrowed-device-restoration" };

        internal BorrowedDeviceMaterials(NativeOwnedDeviceSurface surface)
        {
            var meshes = surface.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().ToArray();
            _world = meshes.Select(Capture).ToArray();
        }

        internal void BorrowForMenu()
        {
            _active = true;
            foreach (var state in _world)
            {
                var mesh = state.Mesh;
                // Shader parameters and PC texture substitutions belong to the
                // focused menu. Mesh/texture resources remain shared read-only.
                if (mesh.MaterialOverride is { } material)
                    mesh.MaterialOverride = (Material)material.Duplicate();
                else
                    for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
                        if (mesh.GetActiveMaterial(index) is { } source)
                            mesh.SetSurfaceOverrideMaterial(index, (Material)source.Duplicate());
            }
        }

        private static MeshState Capture(MeshInstance3D mesh)
        {
            var name = mesh.GetMeta("opennv_nif_source_name", "").AsString();
            var menuVisibility = name == "PipboyLightEffect:0" || Glows.Contains(name);
            return new(mesh, mesh.MaterialOverride,
                Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(mesh.GetSurfaceOverrideMaterial).ToArray(),
                EnvironmentParameters.Select(parameter => mesh.GetInstanceShaderParameter(parameter)).ToArray(),
                menuVisibility ? mesh.Visible : null);
        }

        internal void CaptureMenuState()
        {
            _menu = _world.Select(state => Capture(state.Mesh)).ToArray();
            Deactivate();
        }

        internal void Activate()
        {
            if (_menu is null) throw new InvalidOperationException("Pip-Boy menu materials have not been prepared.");
            if (_active) return;
            // Reacquire the current world state at every focus edge. An
            // intentional source visibility/material change made while dormant
            // must survive the following menu close.
            _world = _world.Select(state => Capture(state.Mesh)).ToArray();
            _active = true;
            Apply(_menu);
        }

        internal void Deactivate()
        {
            if (!_active) return;
            Apply(_world);
            _active = false;
        }

        private static void Apply(IEnumerable<MeshState> states)
        {
            foreach (var state in states)
            {
                if (!GodotObject.IsInstanceValid(state.Mesh)) continue;
                state.Mesh.MaterialOverride = state.Override;
                for (var index = 0; index < state.Surfaces.Length; index++)
                    state.Mesh.SetSurfaceOverrideMaterial(index, state.Surfaces[index]);
                for (var index = 0; index < EnvironmentParameters.Length; index++)
                    state.Mesh.SetInstanceShaderParameter(EnvironmentParameters[index], state.Environment[index]);
                if (state.Visible is { } visible) state.Mesh.Visible = visible;
            }
        }
    }
}
