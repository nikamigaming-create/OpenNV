using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private IReadOnlyList<string> DiscoverResources()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in content.ArchivePaths)
            {
                try
                {
                    using var archive = new FalloutBsaArchive(path);
                    foreach (var member in archive.MemberPaths) paths.Add(Canonical(member));
                }
                catch (Exception error) when (SourceError(error))
                { _discoveryComplete = false; _failures.Add(Failure("archive-discovery", path, error)); }
            }
            foreach (var root in content.ContentRoots)
            {
                try
                {
                    var layer = new FalloutContentLayers([root]);
                    foreach (var path in layer.TopLevelFiles().Keys) paths.Add(Canonical(path));
                    foreach (var directoryPath in Directory.EnumerateDirectories(root))
                        foreach (var path in layer.ResourcePathsUnder(Path.GetFileName(directoryPath))) paths.Add(Canonical(path));
                }
                catch (Exception error) when (SourceError(error))
                { _discoveryComplete = false; _failures.Add(Failure("loose-discovery", root, error)); }
            }
            return paths.Where(path => path.EndsWith(".nif", StringComparison.Ordinal) || path.EndsWith(".kf", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToArray();
        }

        private CatalogResource ReadResource(string requested)
        {
            var path = Canonical(requested);
            if (_resources.TryGetValue(path, out var existing)) return existing;
            string? identity = null, hash = null, bindingHash = null;
            long length = 0;
            var nodes = new List<CatalogNode>();
            var sequences = new List<CatalogSequence>();
            var controllers = new List<CatalogController>();
            var failures = new List<CatalogFailure>();
            IReadOnlyList<int> roots = [];
            try
            {
                if (!content.TryRead(path, null, out var bytes, out var source))
                    throw new FileNotFoundException("Declared owned resource is absent.", path);
                identity = source; length = bytes.Length; hash = Hash(bytes);
                var nif = FalloutNifFile.Read(bytes);
                roots = nif.Roots;
                var decoded = new List<FalloutNifObject>();
                using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                fingerprint.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { nif.UserVersion2, nif.Roots, nif.Strings }, Json)));
                foreach (var block in nif.Blocks)
                {
                    // Only texture/material values can be ignored for a pose-binding
                    // equivalence. Their type and index still bind controller targets.
                    // Every other byte, including skin weights, rest transforms,
                    // topology, priorities, extra data and unknown blocks is retained.
                    fingerprint.AppendData(Encoding.UTF8.GetBytes($"{block.Index}:{block.TypeName}\n"));
                    if (!MaterialValueBlock(block.TypeName)) fingerprint.AppendData(bytes.AsSpan(block.Offset, block.Size));
                    try
                    {
                        var value = nif.ReadObject(block.Index);
                        decoded.Add(value);
                        if (value is FalloutNifControllerSequence sequence)
                            sequences.Add(new(block.Index, sequence.Name, sequence.TargetName, sequence.StartTime,
                                sequence.StopTime, sequence.Frequency, sequence.CycleType, sequence.Manager, sequence.TextKeys,
                                sequence.ControlledBlocks.Select(link => new CatalogChannel(link.NodeName, link.PropertyType,
                                    link.ControllerType, link.Variable1, link.Variable2, link.Interpolator, link.Controller, link.Priority)).ToArray(),
                                sequence.TextKeys >= 0 && nif.ReadObject(sequence.TextKeys) is FalloutNifTextKeyExtraData text
                                    ? text.Keys.Select(key => new CatalogTextKey(key.Time, key.Value)).ToArray() : []));
                        if (block.TypeName.Contains("Controller", StringComparison.Ordinal) && value is not FalloutNifControllerSequence)
                            controllers.Add(new(block.Index, block.TypeName, Hash(bytes.AsSpan(block.Offset, block.Size)),
                                value.GetType().GetProperty("Time")?.GetValue(value),
                                (value as FalloutNifControllerManager)?.Sequences, (value as FalloutNifControllerManager)?.ObjectPalette));
                    }
                    catch (Exception error) when (SourceError(error))
                    {
                        failures.Add(Failure("resource-block", $"{path}#{block.Index}:{block.TypeName}", error, BlockDomain(block.TypeName)));
                        if (block.TypeName.Contains("Controller", StringComparison.Ordinal))
                            controllers.Add(new(block.Index, block.TypeName, Hash(bytes.AsSpan(block.Offset, block.Size)), null));
                    }
                }
                var parents = decoded.OfType<FalloutNifNode>().SelectMany(node => node.Children.Select(child => (child, node.Block.Index)))
                    .GroupBy(pair => pair.child).ToDictionary(group => group.Key, group => group.Select(pair => pair.Index).ToArray());
                foreach (var value in decoded)
                {
                    if (value is FalloutNifNode node)
                        nodes.Add(new(node.Block.Index, node.Block.TypeName, node.Name, parents.GetValueOrDefault(node.Block.Index) ?? [],
                            node.Children, node.Controller, -1));
                    else if (value is FalloutNifGeometry geometry)
                        nodes.Add(new(geometry.Block.Index, geometry.Block.TypeName, geometry.Name,
                            parents.GetValueOrDefault(geometry.Block.Index) ?? [], [], geometry.Controller, geometry.SkinInstance));
                }
                if (failures.Count == 0) bindingHash = Convert.ToHexString(fingerprint.GetHashAndReset()).ToLowerInvariant();
            }
            catch (Exception error) when (SourceError(error)) { failures.Add(Failure("resource", path, error)); }
            var row = new CatalogResource(path, path.EndsWith(".kf", StringComparison.Ordinal) ? "external-animation" : "model",
                identity, hash, length, failures.Count == 0 ? "decoded" : "failed-or-partial", bindingHash,
                nodes, sequences, controllers, failures, roots);
            _resources.Add(path, row); _failures.AddRange(failures);
            if (_resources.Count % 500 == 0) progress?.Invoke($"Catalog resources {_resources.Count}; explicit failures {_failures.Count}.");
            return row;
        }

        private static bool MaterialValueBlock(string type) => type is "BSShaderTextureSet" or "NiSourceTexture" or
            "NiPixelData" or "NiMaterialProperty";

        private static string BlockDomain(string type) => type.StartsWith("bhk", StringComparison.Ordinal) ? "collision" :
            type.Contains("Skin", StringComparison.Ordinal) ? "skin-binding" :
            type.Contains("Particle", StringComparison.Ordinal) || type.Contains("PSys", StringComparison.Ordinal) ? "particles" :
            type.Contains("Controller", StringComparison.Ordinal) || type.Contains("Interpolator", StringComparison.Ordinal) ||
                type.Contains("TransformData", StringComparison.Ordinal) || type.Contains("Keyframe", StringComparison.Ordinal) ? "animation-controller-data" :
            type.EndsWith("Node", StringComparison.Ordinal) ? "source-hierarchy" :
            type.Contains("Shader", StringComparison.Ordinal) || type.Contains("Property", StringComparison.Ordinal) ||
                type.Contains("Texture", StringComparison.Ordinal) ? "material" : "other-source-block";
    }
}
