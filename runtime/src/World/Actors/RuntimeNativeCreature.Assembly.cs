using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    // Source decoding is independent of actor state and can run on a content worker.
    internal sealed record PreparedSource(FalloutCreatureAppearance Appearance, FalloutNifFile Skeleton,
        IReadOnlyList<FalloutNifFile> Models, string Idle, byte[] IdleBytes, FalloutNifFile Animation,
        FalloutNifControllerSequence Sequence, IReadOnlySet<string> ControlledNodes, float UnitsToMetres,
        IReadOnlyList<IReadOnlyDictionary<int, IReadOnlyList<FalloutNifHardwareSkinPartition>>> HardwarePartitions)
    {
        internal static PreparedSource Read(FalloutCreatureAppearance appearance, RuntimeLiveContentSource content, float unitsToMetres, CancellationToken token)
        {
            if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0) throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
            token.ThrowIfCancellationRequested();
            var skeleton = NativeOwnedNifCache.Read(content, appearance.SkeletonPath, out _);
            var models = new List<FalloutNifFile>();
            var preparedPartitions = new List<IReadOnlyDictionary<int, IReadOnlyList<FalloutNifHardwareSkinPartition>>>();
            foreach (var path in appearance.Models)
            {
                token.ThrowIfCancellationRequested();
                var model = NativeOwnedNifCache.Read(content, path, out _);
                models.Add(model);
                var partitions = new Dictionary<int, IReadOnlyList<FalloutNifHardwareSkinPartition>>();
                foreach (var block in model.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var geometry = model.ReadGeometry(block.Index);
                        if (geometry.SkinInstance < 0) continue;
                        var mesh = model.ReadMeshData(geometry.Data);
                        var skin = (FalloutNifSkinInstance)model.ReadObject(geometry.SkinInstance);
                        var data = (FalloutNifSkinData)model.ReadObject(skin.Data);
                        var partition = (FalloutNifSkinPartition)model.ReadObject(skin.SkinPartition);
                        partitions.Add(block.Index, FalloutNifHardwareSkin.Read(skin, data, partition, mesh.Vertices.Length, mesh.Triangles));
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidCastException)
                    {
                        // The ordinary live builder owns errors for reachable blocks.
                    }
                }
                preparedPartitions.Add(partitions);
                try { RuntimeNativeNifMeshBuilder.PrepareStaticGeometry(model, unitsToMetres, token, externalSkeleton: true); }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                {
                    // Reachability and refusal remain with the native builder.
                }
            }
            // Warm only source-declared texture payloads. An unused or invalid
            // declaration must not prevent the live material owner from making
            // the same source-specific decision it would make without prefetch.
            foreach (var model in models.Prepend(skeleton))
            {
                foreach (var block in model.Blocks.Where(block => block.TypeName == "BSShaderTextureSet"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var textures = (FalloutNifShaderTextureSet)model.ReadObject(block.Index);
                        foreach (var texture in textures.Textures.Where(texture => texture.Length != 0))
                        {
                            token.ThrowIfCancellationRequested();
                            try { _ = content.TryRead(FalloutNifSurfaceInputs.TexturePath(texture), null, out _, out _); }
                            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException) { }
                        }
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException) { }
                }
            }
            var directory = appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
            var idle = FalloutCreatureAppearanceResolver.SelectIdle(appearance,
                new[] { directory + "/mtidle.kf", directory + "/locomotion/mtidle.kf" }
                    .Where(path => content.TryResolve(path, null, out _)));
            token.ThrowIfCancellationRequested();
            if (!content.TryRead(idle, null, out var bytes, out _)) throw new FileNotFoundException(idle);
            var animation = FalloutNifFile.Read(bytes);
            var sequences = animation.Roots.Select(animation.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
            if (sequences.Length != 1) throw new NotSupportedException("CREA movement idle requires a unique source KF sequence.");
            token.ThrowIfCancellationRequested();
            var sequence = sequences[0];
            return new(appearance, skeleton, models, idle, bytes, animation, sequence,
                sequence.ControlledBlocks.Where(link => link.ControllerType is "NiTransformController" or "NiVisController")
                    .Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal), unitsToMetres, preparedPartitions);
        }
    }

    // Detached geometry owns no gameplay callbacks, actor clock or random state.
    internal sealed class GeometryAssembly : IDisposable
    {
        private readonly PreparedSource _source;
        private readonly RuntimeLiveContentSource _content;
        private readonly float _units;
        private RuntimeNativeNifSkeleton? _skeleton;
        private RuntimeNativeNifSkeleton.Assembly? _skeletonAssembly;
        private RuntimeNativeNifMeshBuilder.ActorPartAssembly? _part;
        private RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly? _attachment;
        private readonly List<RuntimeNativeNifScene> _parts = [];
        private int _index = -1;
        internal string NextStage => _index < 0 ? _skeletonAssembly?.NextStage ?? "skeleton-start" : _index < _source.Models.Count
            ? $"part:{_source.Appearance.Models[_index]}:{_part?.NextStage ?? _attachment?.NextStage ?? "start"}" : "complete";
        internal GeometryAssembly(PreparedSource source, RuntimeLiveContentSource content, float units)
        {
            if (source.UnitsToMetres != units) throw new InvalidDataException("Prepared creature geometry uses a different source scale.");
            (_source, _content, _units) = (source, content, units);
        }

        internal bool Advance()
        {
            if (_index < 0)
            {
                if (_skeletonAssembly is null)
                {
                    _skeletonAssembly = new(_source.Skeleton, _units);
                    return false;
                }
                if (!_skeletonAssembly.Advance()) return false;
                _skeleton = _skeletonAssembly.Take();
                _skeletonAssembly.Dispose(); _skeletonAssembly = null;
                _skeleton.Node.SetMeta("opennv_source_model", _source.Appearance.SkeletonPath);
                _skeleton.Node.Scale = Vector3.One * _source.Appearance.BaseScale;
                _index = 0;
                return false;
            }
            if (_index >= _source.Models.Count) return true;
            var path = _source.Appearance.Models[_index];
            try
            {
                if (_part is null && _attachment is null)
                {
                    _part = RuntimeNativeNifMeshBuilder.ActorPartAssembly.TryCreate(_source.Models[_index], _skeleton!,
                        _content, null, null, null, null, null, 0, _source.ControlledNodes, _source.HardwarePartitions[_index]);
                    if (_part is null)
                        _attachment = RuntimeNativeNifMeshBuilder.ActorAttachmentAssembly.TryCreate(_source.Models[_index], _skeleton!,
                            _content, _source.ControlledNodes);
                }
                RuntimeNativeNifScene scene;
                if (_part is not null)
                {
                    if (!_part.Advance()) return false;
                    scene = _part.Take(); _part.Dispose(); _part = null;
                }
                else if (_attachment is not null)
                {
                    if (!_attachment.Advance()) return false;
                    scene = _attachment.Take(); _attachment.Dispose(); _attachment = null;
                }
                else
                    scene = NativeNifMeshBuilder.AddActorPart(_source.Models[_index], _skeleton!,
                        externalTransformTargets: _source.ControlledNodes, contentSource: _content);
                scene.Root.SetMeta("opennv_source_model", path);
                scene.Root.SetMeta("opennv_source_form", _source.Appearance.ModelOwner.ToString());
                _parts.Add(scene); _index++;
                return _index >= _source.Models.Count;
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
            { throw new NotSupportedException($"CREA {_source.Appearance.Creature} part {path}: {error.Message}", error); }
        }

        internal PreparedGeometry Take()
        {
            if (_skeleton is null || _index < _source.Models.Count) throw new InvalidOperationException("Creature geometry is incomplete.");
            var targets = IndexAttachmentTargets(_skeleton.Node, includeRoot: true);
            var result = new PreparedGeometry(_source, _skeleton, _parts.ToArray(), targets);
            _skeleton = null;
            return result;
        }
        public void Dispose() { _part?.Dispose(); _part = null; _attachment?.Dispose(); _attachment = null; _skeletonAssembly?.Dispose(); _skeletonAssembly = null; _skeleton?.Node.Free(); _skeleton = null; }
    }

    internal sealed class PreparedGeometry(PreparedSource source, RuntimeNativeNifSkeleton skeleton, IReadOnlyList<RuntimeNativeNifScene> parts,
        IReadOnlyDictionary<string, Node3D?> attachmentTargets) : IDisposable
    {
        internal PreparedSource Source { get; } = source;
        internal RuntimeNativeNifSkeleton Skeleton { get; } = skeleton;
        internal IReadOnlyList<RuntimeNativeNifScene> Parts { get; } = parts;
        internal IReadOnlyDictionary<string, Node3D?> AttachmentTargets { get; } = attachmentTargets;
        private bool _adopted;
        internal void Adopt(Node owner) { owner.AddChild(Skeleton.Node); _adopted = true; }
        public void Dispose() { if (!_adopted && GodotObject.IsInstanceValid(Skeleton.Node)) Skeleton.Node.Free(); }
    }
}
