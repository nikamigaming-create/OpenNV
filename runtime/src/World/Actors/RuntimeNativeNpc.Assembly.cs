using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    // The incomplete body is detached and has no gameplay/physics residency.
    // Advance performs one native part at a time, on the scene thread only.
    internal sealed class Assembly : IDisposable
    {
        private readonly FalloutNpcPreparedGeometry _prepared;
        private readonly RuntimeLiveContentSource _source;
        private readonly float _units;
        private readonly Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? _materialOwner;
        private RuntimeNativeNpc? _actor;
        private readonly List<RuntimeNativeNifScene> _parts = [];
        private int _stage = -1;
        private bool _complete;

        internal Assembly(FalloutNpcPreparedGeometry prepared, RuntimeLiveContentSource source, float units,
            Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? materialOwner)
        {
            (_prepared, _source, _units, _materialOwner) = (prepared, source, units, materialOwner);
            var appearance = prepared.Appearance;
            _actor = new()
            {
                Name = appearance.Reference is { } reference ? $"Reference_{reference}" : $"Actor_{appearance.Npc}",
                Appearance = appearance,
            };
        }

        internal bool Advance()
        {
            var actor = _actor ?? throw new ObjectDisposedException(nameof(Assembly));
            if (_complete) return true;
            var appearance = _prepared.Appearance;
            if (_stage < 0)
            {
                actor.Skeleton = NativeNifMeshBuilder.BuildActorSkeleton(_prepared.Skeleton, _units);
                actor.Skeleton.Node.SetMeta("opennv_source_model", appearance.SkeletonPath);
                actor.AddChild(actor.Skeleton.Node);
                // NAM6/NAM7 are unused; their legal zero values cannot collapse the body.
                actor.Skeleton.Node.Scale = Vector3.One * appearance.RaceHeight;
                _stage = 0;
                return false;
            }
            if (_stage < _prepared.Parts.Count)
            {
                var prepared = _prepared.Parts[_stage++];
                var part = prepared.Part;
                try
                {
                    var scene = NativeNifMeshBuilder.AddActorPart(prepared.Model, actor.Skeleton,
                        materialOverride: (nif, geometry) => _materialOwner?.Invoke(appearance, part, nif, geometry),
                        geometryOwner: prepared.Geometry.Count == 0 ? null : (_, geometry, mesh) => prepared.Geometry.GetValueOrDefault(geometry.Block.Index, mesh),
                        rigidFaceBinds: FalloutNpcFaceAttachment.UsesHeadModelSpace(part.Role)
                            ? _prepared.HeadBinds ?? throw new NotSupportedException("Rigid FaceGen part has no source skinned head owner.") : null,
                        selectedGeometryName: prepared.SelectedShape,
                        morphOwner: prepared.Morphs.Count == 0 ? null : (_, geometry, _) => prepared.Morphs[geometry.Block.Index],
                        contentSource: _source, bipedSlots: part.Role is "armor" or "armor-addon" ? part.BipedSlots : 0);
                    scene.Root.SetMeta("opennv_source_model", part.ModelPath!);
                    scene.Root.SetMeta("opennv_source_part", part.Role);
                    scene.Root.SetMeta("opennv_source_form", part.Source.ToString());
                    if (prepared.MorphIdentity is { } identity) scene.Root.SetMeta("opennv_source_egm", identity);
                    _parts.Add(scene);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
                {
                    throw new NotSupportedException($"NPC {appearance.Npc} part {part.Role} ({part.ModelPath}): {error.Message}", error);
                }
                return false;
            }
            actor.Parts = _parts;
            actor.BindFaceTargets();
            if (appearance.Reference is { } key) actor.SetMeta("opennv_reference_form_key", key.ToString());
            actor.SetMeta("opennv_npc_form_key", appearance.Npc.ToString());
            actor.SetMeta("opennv_source_skeleton", appearance.SkeletonPath);
            return _complete = true;
        }

        internal RuntimeNativeNpc Take()
        {
            if (!_complete || _actor is null) throw new InvalidOperationException("NPC assembly is not complete.");
            var actor = _actor; _actor = null; return actor;
        }

        public void Dispose() { _actor?.Free(); _actor = null; }
    }

    internal void BindSourceBehavior(FalloutPluginStack stack, FalloutActorTemplateSelection? selection)
    {
        _templates = selection;
        ConfigureFaceAnimation(stack);
    }
}
