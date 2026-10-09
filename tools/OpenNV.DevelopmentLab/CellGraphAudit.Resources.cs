using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    // Audit-local reuse only. Metadata/failures belong to the full denominator;
    // evicting a decoded asset never evicts a source/resource row.
    internal sealed class CellAuditResources(RuntimeLiveContentSource source, float units, FalloutPluginStack? addonRecords = null)
    {
        private const int RecentModelCount = 64;
        private const long RecentModelCost = 128L * 1024 * 1024;
        private readonly Dictionary<string, ResourceRow> _resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (Model Model, long Cost, ulong Use)> _models = new(StringComparer.OrdinalIgnoreCase);
        private ulong _use;
        private long _cost;
        private bool _facesRead;
        private FalloutFaceGeometryControls? _faces;
        private Exception? _facesFailure;
        private readonly Dictionary<string, string?> _appearanceValidation = new(StringComparer.Ordinal);
        private readonly Dictionary<FalloutFormKey, (FalloutNavigationMesh[] Meshes, object[] Failures, long Cost, ulong Use)> _navigation = new(FalloutFormKeyComparer.Instance);
        private long _navigationCost;
        private readonly SourceAddonDependencies? _addons = addonRecords is null ? null : new(source, addonRecords);
        private readonly SourceAnimationSoundDependencies _animationSounds = new(source, addonRecords);
        internal IReadOnlyDictionary<string, ResourceRow> Resources => _resources;
        internal bool AddonCatalogPassed => _addons?.CatalogPassed == true;
        internal object AddonCoverage => _addons?.Coverage ?? new { disposition = "uninspected; exact owned stack was not supplied", nativeAdmission = "unverified" };
        internal IReadOnlyList<string> AddonModelRoots => _addons?.ModelRoots ?? [];
        internal void ReadAddonDeclarations()
        {
            if (_addons is null) throw new NotSupportedException("ADDN declarations have no exact owned stack.");
            _addons.InspectAll(ReadResource);
        }
        internal FalloutFaceGeometryControls? Faces
        {
            get
            {
                if (!_facesRead)
                {
                    _facesRead = true;
                    try { _faces = source.TryRead("facegen/si.ctl", null, out var bytes, out _) ? FalloutFaceGeometryControls.Read(bytes) : null; }
                    catch (Exception error) { _facesFailure = error; }
                }
                if (_facesFailure is not null) throw new InvalidDataException("Owned face-controls read failed: " + _facesFailure.Message, _facesFailure);
                return _faces;
            }
        }

        internal ResourceRow Read(string path, string kind)
        {
            var row = ReadResource(path, kind);
            _addons?.Inspect(row, ReadResource);
            return row;
        }

        private ResourceRow ReadResource(string path, string kind)
        {
            path = Canonical(path);
            if (_resources.TryGetValue(path, out var retained))
            {
                if (!retained.InspectionRoles.Add(kind) || retained.Kind == kind) return retained;
                if ((retained.Kind is "model" or "animation" or "lod-model") && (kind is "model" or "animation" or "lod-model"))
                {
                    if (kind == "model" && retained.Kind != "model")
                    {
                        retained.Kind = "model";
                        if (retained.Stream is null) return retained; // Original format failure remains visible.
                        try
                        {
                            var nif = _models.GetValueOrDefault(path).Model?.File;
                            if (nif is null)
                            {
                                if (!source.TryRead(path, null, out var bytes, out var identity) || Hash(bytes) != retained.Sha256 || identity != retained.Source)
                                    throw new InvalidDataException("Winning NIF changed before an additional inspection role: " + path);
                                nif = FalloutNifFile.Read(bytes);
                            }
                            var collision = ReadPlacedCollision(nif, units);
                            retained.CollisionShapes = collision.Shapes; retained.CollisionTriangles = collision.PackedTriangles;
                            retained.Failures.AddRange(collision.Failures); Remember(path, new(nif, retained, collision.Triangles));
                        }
                        catch (Exception error) { retained.Failures.Add(new { lane = "resource-role-model", error = error.Message }); }
                    }
                    return retained;
                }
                retained.Failures.Add(new { lane = "resource-role-owner", declaredKind = retained.Kind, requiredKind = kind,
                    error = "The same source resource has incompatible format inspection roles." });
                return retained;
            }
            var row = new ResourceRow { Path = path, Kind = kind }; _resources.Add(path, row);
            row.InspectionRoles.Add(kind);
            row.Native = new { admission = "unverified", sourceReadIsNotNativeAdmission = true };
            try
            {
                if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
                    throw new InvalidDataException("Resource path leaves the owned logical namespace.");
                if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException("Owned resource is missing: " + path);
                row.Source = identity; row.Sha256 = Hash(bytes); row.Bytes = bytes.Length;
                if (kind == "texture")
                {
                    NativeOwnedMediaFormat.ValidateDds(bytes); _ = FalloutDdsMipChain.ReadPartial(bytes);
                }
                else if (kind is "model" or "animation" or "lod-model")
                {
                    var nif = FalloutNifFile.Read(bytes); row.Stream = nif.UserVersion2; row.Blocks = nif.Blocks.Count; row.Roots = nif.Roots.Count;
                    row.BlockTypes = nif.Blocks.Select(block => block.TypeName).Distinct().Order(StringComparer.Ordinal).ToArray();
                    foreach (var block in nif.Blocks)
                        try
                        {
                            var obj = nif.ReadObject(block.Index); row.DecodedBlocks++;
                            SourceControllerLinks.Inspect(nif, obj, row);
                            SourcePhysicsDeclarations.Inspect(nif, obj, row);
                            if (obj is FalloutNifGeometry geometry)
                            {
                                row.Geometry++;
                                InspectGeometryData(nif, geometry, row);
                            }
                            if (obj is FalloutNifCollisionObject) row.CollisionAttachments++;
                            if (obj is FalloutNifRigidBody) row.CollisionBodies++;
                            if (obj is FalloutNifTextKeyExtraData textKeys)
                            {
                                var inspection = _animationSounds.Read(textKeys);
                                row.DependencyDeclarations.AddRange(inspection.Keys);
                                row.Failures.AddRange(inspection.Failures);
                                foreach (var dependency in inspection.DependencyEdges)
                                {
                                    row.AddDependency(dependency.Path, dependency.Kind);
                                    _ = Read(dependency.Path, dependency.Kind);
                                }
                            }
                            if (obj is FalloutNifNode { Value: not null } addonNode)
                            {
                                if (_addons is null) SourceAddonDependencies.RefuseMissingOwner(row, addonNode);
                                else _addons.Capture(row, addonNode);
                            }
                            IEnumerable<string> declaredTextures = obj switch
                            {
                                FalloutNifShaderTextureSet set => set.Textures,
                                FalloutNifNoLightingProperty { FileName.Length: 0 } => [""],
                                FalloutNifSourceTexture { FileName.Length: 0 } => [""],
                                FalloutNifSkyShaderProperty { FileName.Length: 0 } => [""],
                                FalloutNifTileShaderProperty { FileName.Length: 0 } => [""],
                                _ => Textures(obj)
                            };
                            var declarationOrdinal = 0;
                            foreach (var texture in declaredTextures)
                            {
                                var ordinal = declarationOrdinal++;
                                string? logical = null;
                                try
                                {
                                    if (obj is FalloutNifSkyShaderProperty or FalloutNifTileShaderProperty)
                                        throw new NotSupportedException("This NIF texture declaration has a separate uninspected native consumer policy; no resource key is substituted.");
                                    if (texture.Length == 0)
                                    {
                                        row.DependencyDeclarations.Add(new { block = block.Index, type = block.TypeName, ordinal,
                                            declaredPath = texture, logicalPath = logical, disposition = "encoded-empty; no external resource substituted" });
                                        continue;
                                    }
                                    logical = Canonical(FalloutNifSurfaceInputs.TexturePath(texture));
                                    row.AddDependency(logical, "texture");
                                    _ = Read(logical, "texture");
                                    row.DependencyDeclarations.Add(new { block = block.Index, type = block.TypeName, ordinal,
                                        declaredPath = texture, logicalPath = logical, disposition = "shared-FalloutNifSurfaceInputs.TexturePath; native draw admission unverified" });
                                }
                                catch (Exception error)
                                {
                                    row.DependencyDeclarations.Add(new { block = block.Index, type = block.TypeName, ordinal,
                                        declaredPath = texture, logicalPath = logical, disposition = "resource-declaration-refused", error = error.Message });
                                    row.Failures.Add(new { lane = "nif-resource-declaration", block = block.Index, type = block.TypeName,
                                        ordinal, declaredPath = texture, logicalPath = logical, error = error.Message });
                                }
                            }
                        }
                        catch (Exception error) { row.Failures.Add(new { lane = "nif-block", block = block.Index, type = block.TypeName, error = error.Message }); }
                    var collision = kind == "model" ? ReadPlacedCollision(nif, units) : new CollisionMath([], 0, 0, []);
                    row.CollisionShapes = collision.Shapes; row.CollisionTriangles = collision.PackedTriangles; row.Failures.AddRange(collision.Failures);
                    if (kind is "model" or "animation") Remember(path, new(nif, row, collision.Triangles));
                }
                else row.Failures.Add(new { lane = "resource-format-owner", error = "No CellGraph decoder for resource kind " + kind });
            }
            catch (Exception error) { row.Failures.Add(new { lane = "owned-resource", error = error.Message }); }
            return row;
        }

        private static void InspectGeometryData(FalloutNifFile nif, FalloutNifGeometry geometry, ResourceRow row)
        {
            FalloutNifBlock? target = geometry.Data >= 0 && geometry.Data < nif.Blocks.Count ? nif.Blocks[geometry.Data] : null;
            var link = new GeometryDataLinkRow
            {
                Block = geometry.Block.Index, Type = geometry.Block.TypeName,
                Offset = geometry.Block.Offset, Bytes = geometry.Block.Size,
                TargetBlock = geometry.Data, TargetType = target?.TypeName,
                TargetOffset = target?.Offset, TargetBytes = target?.Size
            };
            row.GeometryDataLinks.Add(link);
            try
            {
                var mesh = nif.ReadMeshData(geometry.Data);
                link.ReaderDisposition = "typed-mesh-data-decoded";
                link.Vertices = mesh.Vertices.Length;
                link.Triangles = mesh.Triangles.Length;
            }
            catch (Exception error)
            {
                link.ReaderDisposition = "reader-refused";
                link.Error = error.Message;
                row.Failures.Add(new { lane = "nif-geometry-data-link", block = geometry.Block.Index,
                    type = geometry.Block.TypeName, field = "Data", targetBlock = geometry.Data,
                    targetType = target?.TypeName, error = error.Message });
            }
        }

        internal Model ReadModel(string path)
        {
            path = Canonical(path);
            var report = Read(path, path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) ? "animation" : "model");
            if (_models.TryGetValue(path, out var retained))
            {
                _models[path] = (retained.Model, retained.Cost, ++_use); return retained.Model;
            }
            // A failed header is not decoded again once per placed instance.
            if (report.Stream is null) return new(null, report, []);
            try
            {
                if (!source.TryRead(path, null, out var bytes, out var identity) || Hash(bytes) != report.Sha256 || identity != report.Source)
                    throw new InvalidDataException("Winning model changed after its original resource declaration: " + path);
                var nif = FalloutNifFile.Read(bytes);
                var collision = report.Kind == "animation" ? new CollisionMath([], 0, 0, []) : ReadPlacedCollision(nif, units);
                var model = new Model(nif, report, collision.Triangles); Remember(path, model); return model;
            }
            catch (Exception error)
            {
                report.Failures.Add(new { lane = "model-reuse-binding", error = error.Message }); return new(null, report, []);
            }
        }

        internal (FalloutNavigationMesh[] Meshes, object[] Failures) Navigation(FalloutPluginStack records, FalloutFormKey cell)
        {
            if (_navigation.TryGetValue(cell, out var retained))
            {
                _navigation[cell] = (retained.Meshes, retained.Failures, retained.Cost, ++_use);
                return (retained.Meshes, retained.Failures);
            }
            var meshes = new List<FalloutNavigationMesh>(); var failures = new List<object>();
            // The complete source graph independently inspects disabled and
            // orphan NAVM rows. This current-state projection uses the same
            // live disabled policy, indexed by exact winning CELL ancestry.
            foreach (var record in records.EffectiveCellChildren(cell, new HashSet<string> { "NAVM" }))
            {
                if ((record.Flags & 0x800) != 0) continue;
                try
                {
                    var mesh = FalloutNavigationMesh.Read(record);
                    if (mesh.Cell != cell) throw new InvalidDataException("NAVM DATA CELL differs from winning group ancestry.");
                    meshes.Add(mesh);
                }
                catch (Exception error) { failures.Add(new { form = record.FormKey.ToString(), lane = "navm-reader", error = error.Message }); }
            }
            try
            {
                // Feed the actual shared adjacency validator from the already
                // decoded source rows, without rescanning every corpus NAVM.
                ValidateNavigationDeclarations(meshes, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cell.ToString() });
            }
            catch (Exception error) { failures.Add(new { lane = "runtime-navigation-graph", error = error.Message }); }
            var result = (Meshes: meshes.ToArray(), Failures: failures.ToArray());
            var cost = meshes.Sum(mesh => mesh.Vertices.Length * 24L + mesh.Triangles.Length * 96L + mesh.Edges.Length * 32L);
            const long budget = 64L * 1024 * 1024;
            if (cost <= budget)
            {
                while (_navigation.Count >= RecentModelCount || _navigationCost + cost > budget)
                {
                    var oldest = _navigation.MinBy(pair => pair.Value.Use); _navigationCost -= oldest.Value.Cost; _navigation.Remove(oldest.Key);
                }
                _navigation.Add(cell, (result.Meshes, result.Failures, cost, ++_use)); _navigationCost += cost;
            }
            return result;
        }

        private void Remember(string path, Model model)
        {
            // Cost is a conservative budget proxy for source buffers, decoded
            // object graphs and collision triangles, not a measured heap claim.
            var cost = model.Report.Bytes * 8L + model.Collision.Count * 96L;
            // A role upgrade may exceed the budget. Never leave the previous
            // animation-only cache entry standing in for that model consumer.
            if (_models.Remove(path, out var replaced)) _cost -= replaced.Cost;
            if (cost > RecentModelCost) return;
            while (_models.Count >= RecentModelCount || _cost + cost > RecentModelCost)
            {
                var oldest = _models.MinBy(pair => pair.Value.Use); _cost -= oldest.Value.Cost; _models.Remove(oldest.Key);
            }
            _models[path] = (model, cost, ++_use); _cost += cost;
        }

        internal void ValidateAppearance(FalloutNpcAppearance appearance)
        {
            var key = Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(appearance with { Reference = null }, Json));
            if (!_appearanceValidation.TryGetValue(key, out var failure))
            {
                try { _ = FalloutNpcPreparedGeometry.Read(appearance, source); }
                catch (Exception error) { failure = error.Message; }
                _appearanceValidation.Add(key, failure);
            }
            if (failure is not null) throw new InvalidDataException(failure);
        }
    }
}
