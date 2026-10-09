using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    private sealed class AlternativeRow
    {
        public string Identity { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Winner { get; init; } = "";
        public string? Sha256 { get; set; }
        public bool Deleted { get; init; }
        public object[] SourceFields { get; set; } = [];
        public List<int> SemanticReadSlots { get; } = [];
        public int[] UninspectedSemanticFieldSlots => Enumerable.Range(0, SourceFields.Length).Except(SemanticReadSlots).ToArray();
        public object? SelectionDeclaration { get; set; }
        public List<object> Edges { get; } = [];
        public List<string> Resources { get; } = [];
        public object? Packages { get; set; }
        public List<object> Failures { get; } = [];
        public string RuntimeAlternatives { get; init; } = "unverified; declaration coverage does not execute template, inventory, outfit or script permutations";
    }

    private sealed class AlternativeInventory
    {
        public List<AlternativeRow> Records { get; } = [];
        public List<ResourceRow> Resources { get; } = [];
        public List<object> Worldspaces { get; } = [];
        public List<object> Landscapes { get; } = [];
        public List<object> Failures { get; } = [];
        public AlternativeQueryReuse? QueryReuse { get; set; }
        public ActorAnimationInspection? ActorAnimations { get; set; }
        public IncomingAnimationInspection? IncomingAnimations { get; set; }
        public string Domain { get; init; } = "Finite authored actor/template/list/inventory/race/headpart/armor/model/texture declarations for every winning actor and placed base, with all winning/deleted IDLE/ANIO and incoming PACK/IDLM/INFO animation declarations. Both enabled states share source declarations; deleted bases are retained and never substituted. Arbitrary script-created resources and state combinations remain unverified.";
    }

    // Enumerate declarations, not random seeds or one fabricated saved choice.
    // Each link keeps its declaring record/master scope and authored slot.
    private static AlternativeInventory DiscoverAlternativeDeclarations(RuntimeLiveContentSource source, FalloutPluginStack records,
        SourceGraphInventory graph, IReadOnlySet<FalloutFormKey> selected, CellAuditResources auditResources)
    {
        var result = new AlternativeInventory();
        var resources = new Dictionary<string, ResourceRow>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var active = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var actorAnimations = new SourceActorAnimationDependencies(source, records).Read();
        result.ActorAnimations = actorAnimations;
        result.IncomingAnimations = new SourceIncomingAnimationDependencies(source, records, actorAnimations).Read();
        foreach (var dependency in actorAnimations.DependencyEdges) _ = ReadResource(dependency.Path, dependency.Kind);
        foreach (var actor in actorAnimations.Records.Where(row => row.Signature is "NPC_" or "CREA"))
            Visit(ParseForm(actor.Form), null, "authored-actor-source", -1, null);
        foreach (var graphRow in graph.Rows.Values.Where(row => !row.Deleted && PlacedSignatures.Contains(row.Signature)))
        {
            if (graphRow.ParentCell is null || !selected.Contains(ParseForm(graphRow.ParentCell))) continue;
            if (graphRow.Base is not { } basis)
            {
                result.Failures.Add(new { reference = graphRow.Identity, lane = "alternative-base-owner", error = "A source placement has no bound base declaration." });
                continue;
            }
            var basisKey = ParseForm(basis);
            if (!records.TryGetEffective(basisKey, out _) && graph.Winners.TryGetValue(ParseForm(graphRow.Identity), out var reference))
            {
                try
                {
                    if (FalloutReferencePrimitive.Read(reference) is not null) continue;
                }
                catch (Exception error) { result.Failures.Add(new { reference = graphRow.Identity, lane = "alternative-primitive-owner", error = error.Message }); }
            }
            Visit(basisKey, null, "placed-base", -1, null);
        }
        var queries = new AlternativeSourceQueries(source, records);
        foreach (var pair in graph.Rows.Where(pair => pair.Value.Signature == "WRLD" && !pair.Value.Deleted))
        {
            var world = pair.Key; var worldResources = new List<string>(); var errors = new List<object>();
            string? lodName = null; object? catalog = null; object? map = null;
            try
            {
                lodName = FalloutExteriorLod.WorldName(records, world);
                var paths = queries.ResourcePathsUnder("meshes/landscape/lod/" + lodName);
                var settings = FalloutInstallationSettings.Read(source);
                var lod = new FalloutExteriorLod(lodName, paths, settings.Number("TerrainManager", "fBlockLoadDistance"),
                    settings.Number("TerrainManager", "fSplitDistanceMult"), settings.Number("TerrainManager", "fBlockMorphDistanceMult"));
                catalog = new { lod.Blocks, lod.LoadDistance, lod.SplitMultiplier, lod.MorphMultiplier,
                    residencyAndAllCameraSelections = "unverified; no chosen position replaces authored coverage" };
                foreach (var path in paths)
                {
                    var resource = ReadResource(path, path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ? "lod-model" : "lod-resource-unbound");
                    worldResources.Add(resource.Path);
                }
            }
            catch (Exception error) { errors.Add(new { lane = "world-lod-declarations", error = error.Message }); }
            try
            {
                var fields = graph.Winners[world].ReadSubrecords().ToArray();
                var parentFlags = fields.SingleOrDefault(field => field.Signature == "PNAM").Data;
                if (fields.Any(field => field.Signature is "MNAM" or "ICON") || parentFlags.Length == 2 && (BinaryPrimitives.ReadUInt16LittleEndian(parentFlags.Span) & 4) != 0)
                {
                    var declared = FalloutWorldMap.Read(records, world); map = declared;
                    worldResources.Add(ReadResource(declared.Texture, "texture").Path);
                }
            }
            catch (Exception error) { errors.Add(new { lane = "world-map-declarations", error = error.Message }); }
            foreach (var error in errors) result.Failures.Add(new { world = world.ToString(), issue = error });
            result.Worldspaces.Add(new { world = world.ToString(), lodName, catalog, map, resources = worldResources, errors,
                environmentWaterWeatherAndPixels = "unverified; source extents retained in winning graph" });
        }
        foreach (var cell in selected)
        {
            if (!graph.CellChildren.TryGetValue(cell, out var children)) continue;
            var lands = children.Where(record => record.Signature == "LAND" && !record.IsDeleted).ToArray();
            if (lands.Length == 0) continue;
            try
            {
                var definition = graph.Definitions.GetValueOrDefault(cell) ?? throw new InvalidDataException("LAND owner has no bound CELL definition.");
                var world = definition.Worldspace ?? throw new InvalidDataException("LAND CELL has no source worldspace.");
                var baseRows = lands.Length == 1 ? lands[0].ReadSubrecords().Where(field => field.Signature == "BTXT").ToArray() : [];
                var needsDefault = baseRows.Length < 4 || baseRows.Any(field => field.Data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == 0);
                var defaults = needsDefault ? FalloutLandscapeTransportResolver.ReadDefaultTexture(FalloutInstallationSettings.Read(source)) : null;
                var land = FalloutLandscapeTransportResolver.ResolveCell(records, definition, defaults);
                foreach (var texture in land.Textures.Values)
                {
                    _ = ReadResource(texture.DiffusePath, "texture");
                    if (texture.NormalPath is { } normal) _ = ReadResource(normal, "texture");
                }
                result.Landscapes.Add(new { cell = cell.ToString(), landscape = land.Landscape.ToString(), world = world.ToString(),
                    heightOwner = land.HeightDefault is null ? "authored-VHGT" : "winning-WRLD-DNAM",
                    heightDefault = land.HeightDefault, persistentResidency = "independent runtime owner; not a LAND declaration prerequisite",
                    vertices = land.Heights.Length, land.Flags, land.BaseLayers, land.AlphaLayers,
                    textures = land.Textures.Values, nativeHeightfieldMaterialsAndPixels = "unverified" });
            }
            catch (Exception error)
            {
                foreach (var land in lands)
                {
                    graph.Rows[land.FormKey].Failures.Add(new { lane = "land-reader", error = error.Message });
                    result.Failures.Add(new { cell = cell.ToString(), landscape = land.FormKey.ToString(), lane = "land-reader", error = error.Message });
                }
            }
        }
        foreach (var path in auditResources.AddonModelRoots) _ = ReadResource(path, "model");
        result.Resources.AddRange(resources.Values.OrderBy(resource => resource.Path, StringComparer.Ordinal));
        result.QueryReuse = queries.Reuse;
        return result;

        ResourceRow ReadResource(string path, string kind)
        {
            var row = auditResources.Read(path, kind);
            if (!resources.TryAdd(row.Path, row)) return row;
            foreach (var dependency in row.DependencyEdges) _ = ReadResource(dependency.Path, dependency.Kind);
            return row;
        }

        void AddResource(AlternativeRow row, string path, string kind)
        {
            var resource = ReadResource(path, kind);
            if (!row.Resources.Contains(resource.Path, StringComparer.OrdinalIgnoreCase)) row.Resources.Add(resource.Path);
            foreach (var issue in resource.Failures) row.Failures.Add(new { lane = "alternative-resource", resource = resource.Path, issue });
        }

        void Visit(FalloutFormKey key, AlternativeRow? declaring, string field, int slot, string[]? expected)
        {
            declaring?.Edges.Add(new { field, slot, target = key.ToString(), expectedSignatures = expected,
                outcome = "authored-declaration; eligibility/selection/combination are separate runtime owners" });
            if (!graph.Winners.TryGetValue(key, out var record))
            {
                (declaring?.Failures ?? result.Failures).Add(new { lane = "alternative-link", target = key.ToString(), field, slot, error = "Source target is absent from the exact winning stack." }); return;
            }
            if (record.IsDeleted || expected is not null && !expected.Contains(record.Signature, StringComparer.Ordinal))
                (declaring?.Failures ?? result.Failures).Add(new { lane = "alternative-link", target = key.ToString(), field, slot,
                    error = record.IsDeleted ? "Source target is a winning deletion." : "Source target signature differs: " + record.Signature });
            if (active.Contains(key))
            {
                (declaring?.Failures ?? result.Failures).Add(new { lane = "alternative-cycle", target = key.ToString(), field, slot, error = "Authored finite alternative graph contains a cycle." }); return;
            }
            if (!visited.Add(key)) return;
            active.Add(key);
            var row = new AlternativeRow { Identity = key.ToString(), Signature = record.Signature, Winner = record.Plugin.Name, Deleted = record.IsDeleted };
            result.Records.Add(row);
            try
            {
                row.Sha256 = Hash(record.ReadData()); var fields = record.ReadSubrecords().ToArray();
                row.SourceFields = fields.Select((sourceField, index) => (object)new { index, sourceField.Signature, bytes = sourceField.Data.Length,
                    sha256 = Hash(sourceField.Data.Span) }).ToArray();
                if (record.IsDeleted) return;
                if (record.Signature is "LVLN" or "LVLC" or "LVLI")
                    try
                    {
                        var chance = fields.Single(sourceField => sourceField.Signature == "LVLD").Data;
                        var flags = fields.Single(sourceField => sourceField.Signature == "LVLF").Data;
                        var global = fields.SingleOrDefault(sourceField => sourceField.Signature == "LVLG").Data;
                        if (chance.Length != 1 || flags.Length != 1 || chance.Span[0] > 100 ||
                            (flags.Span[0] & ~(record.Signature == "LVLI" ? 7 : 3)) != 0 || !global.IsEmpty && global.Length != 4)
                            throw new InvalidDataException("Leveled chance/flags/global declaration differs from the existing selection owner.");
                        var chanceOwner = global.IsEmpty ? null : record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(global.Span));
                        row.SelectionDeclaration = new { chanceNone = chance.Span[0], flags = flags.Span[0], chanceGlobal = chanceOwner?.ToString(),
                            noOutcomeAlternative = "Chance/empty eligibility/global values retained; no random or numeric valuation is fabricated." };
                        if (chanceOwner is { } owner) Visit(owner, row, "LVLG", -1, ["GLOB"]);
                        row.SemanticReadSlots.AddRange(fields.Select((sourceField, index) => (sourceField.Signature, index))
                            .Where(sourceField => sourceField.Signature is "LVLD" or "LVLF" or "LVLG").Select(sourceField => sourceField.index));
                    }
                    catch (Exception error) { row.Failures.Add(new { lane = "alternative-selection-declaration", error = error.Message }); }
                for (var index = 0; index < fields.Length; index++)
                {
                    var item = fields[index];
                    try
                    {
                        void FormLink(int offset, string[]? signatures)
                        {
                            var target = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(item.Data.Span[offset..]));
                            if (target is { } bound) Visit(bound, row, item.Signature, index, signatures);
                            else row.Edges.Add(new { field = item.Signature, slot = index, byteOffset = offset, target = (string?)null,
                                outcome = "encoded-null; no live alternative substituted" });
                        }
                        if (item.Signature == "TPLT" && record.Signature is "NPC_" or "CREA")
                        {
                            if (item.Data.Length != 4) throw new InvalidDataException("Actor TPLT is not one FormID.");
                            FormLink(0, record.Signature == "NPC_" ? ["NPC_", "LVLN"] : ["CREA", "LVLC"]);
                        }
                        else if (item.Signature == "LVLO" && record.Signature is "LVLN" or "LVLC" or "LVLI")
                        {
                            if (item.Data.Length != 12) throw new InvalidDataException("Leveled declaration LVLO extent is not 12.");
                            var quantity = BinaryPrimitives.ReadUInt16LittleEndian(item.Data.Span[8..]);
                            if (quantity == 0 || record.Signature != "LVLI" && quantity != 1)
                                throw new NotSupportedException("Leveled quantity has no existing item/actor selection semantics.");
                            row.Edges.Add(new { field = "LVLO-eligibility", slot = index, level = BinaryPrimitives.ReadUInt16LittleEndian(item.Data.Span),
                                count = BinaryPrimitives.ReadUInt16LittleEndian(item.Data.Span[8..]), noOutcomeAndChanceGlobal = "unverified; source fields retained" });
                            FormLink(4, record.Signature == "LVLN" ? ["NPC_", "LVLN"] : record.Signature == "LVLC" ? ["CREA", "LVLC"] : null);
                        }
                        else if (item.Signature == "CNTO" && record.Signature is "NPC_" or "CREA" or "CONT")
                        {
                            if (item.Data.Length != 8) throw new InvalidDataException("Inventory declaration CNTO extent is not 8.");
                            row.Edges.Add(new { field = "CNTO-count", slot = index, count = BinaryPrimitives.ReadInt32LittleEndian(item.Data.Span[4..]),
                                inventoryEquipmentAndExtraDataExecution = "unverified" }); FormLink(0, null);
                        }
                        else if (record.Signature == "NPC_" && item.Signature is "RNAM" or "HNAM" or "ENAM" or "PNAM")
                        {
                            if (item.Data.Length != 4) throw new InvalidDataException("NPC appearance declaration is not one FormID.");
                            FormLink(0, item.Signature switch { "RNAM" => ["RACE"], "HNAM" => ["HAIR"], "ENAM" => ["EYES"], _ => ["HDPT"] });
                        }
                        else if (record.Signature == "RACE" && item.Signature is "HNAM" or "ENAM" or "DNAM")
                        {
                            if (item.Data.Length % 4 != 0 || item.Signature == "DNAM" && item.Data.Length != 8)
                                throw new InvalidDataException("Race finite appearance array has malformed FormID extent.");
                            for (var offset = 0; offset < item.Data.Length; offset += 4) FormLink(offset, item.Signature == "ENAM" ? ["EYES"] : ["HAIR"]);
                        }
                        else if (record.Signature == "HDPT" && item.Signature == "HNAM")
                        {
                            if (item.Data.Length != 4) throw new InvalidDataException("HDPT HNAM is not one FormID."); FormLink(0, ["HDPT"]);
                        }
                        else if (record.Signature == "ARMO" && item.Signature == "BIPL")
                        {
                            if (item.Data.Length != 4) throw new InvalidDataException("Armor BIPL is not one FormID."); FormLink(0, ["FLST"]);
                        }
                        else if (record.Signature == "FLST" && item.Signature == "LNAM")
                        {
                            if (item.Data.Length != 4) throw new InvalidDataException("Biped list LNAM is not one FormID."); FormLink(0, ["ARMA"]);
                        }
                        else if (record.Signature == "CREA" && item.Signature is "NIFZ" or "KFFZ")
                        {
                            if (item.Data.IsEmpty || item.Data.Span[^1] != 0) throw new InvalidDataException("Creature resource list is not terminated.");
                            var skeleton = FalloutNpcAppearanceResolver.PathField(record, "MODL", "meshes", true)!;
                            var directory = skeleton[..skeleton.LastIndexOf('/')];
                            foreach (var path in Encoding.Latin1.GetString(item.Data.Span).TrimEnd('\0').Split('\0').Where(path => path.Length != 0))
                                AddResource(row, path.Replace('\\', '/').StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : directory + "/" + path,
                                    item.Signature == "KFFZ" ? "animation" : "model");
                        }
                        else if (item.Signature is "MODL" or "MOD2" or "MOD3" or "MOD4")
                        {
                            var adjacent = new List<FalloutPluginSubrecord> { item };
                            var alternate = item.Signature switch { "MOD2" => "MO2S", "MOD3" => "MO3S", "MOD4" => "MO4S", _ => "MODS" };
                            var flags = item.Signature == "MOD3" ? "MOSD" : "MODD";
                            for (var next = index + 1; next < fields.Length && fields[next].Signature is "MODT" or "MODS" or "MODD" or "MO2T" or "MO2S" or "MO2D" or "MO3T" or "MO3S" or "MOSD" or "MO4T" or "MO4S" or "MO4D"; next++) adjacent.Add(fields[next]);
                            var part = FalloutNpcAppearanceResolver.ReadModel(records, record, "authored-alternative", item.Signature, alternate, flags, 0, null, adjacent);
                            if (part.ModelPath is { } modelPath)
                                AddResource(row, record.Signature == "TREE" ? FalloutCellSceneReader.NormalizeModelPath(FalloutDialogueTopic.Text(item.Data.Span), "TREE") : modelPath,
                                    modelPath.EndsWith(".spt", StringComparison.OrdinalIgnoreCase) ? "speedtree-unbound" : "model");
                            foreach (var texture in part.AlternateTextures)
                            {
                                Visit(texture.TextureSet, row, alternate, index, ["TXST"]);
                                foreach (var path in texture.Textures.Values) AddResource(row, path, "texture");
                            }
                        }
                        else if (item.Signature.StartsWith("TX0", StringComparison.Ordinal) && record.Signature == "TXST" ||
                            item.Signature == "ICON" && record.Signature is "RACE" or "HAIR" or "EYES" or "ARMO" or "ARMA")
                        {
                            var path = FalloutNpcAppearanceResolver.PathField(record, item.Signature, "textures", false, [item]);
                            if (path is not null) AddResource(row, path, "texture");
                        }
                        else continue;
                        row.SemanticReadSlots.Add(index);
                    }
                    catch (Exception error) { row.Failures.Add(new { lane = "alternative-declaration", field = item.Signature, slot = index, error = error.Message }); }
                }
                if (record.Signature is "NPC_" or "CREA")
                {
                    row.Packages = PackageAlternatives(records, key);
                    var packages = System.Text.Json.JsonSerializer.SerializeToElement(row.Packages, Json);
                    var errors = packages.GetProperty("failures").GetArrayLength() + packages.GetProperty("packageOwnerAlternatives").EnumerateArray()
                        .Sum(owner => owner.GetProperty("authoredPriorityAlternatives").EnumerateArray().Sum(package => package.GetProperty("errors").GetArrayLength()));
                    if (errors != 0) row.Failures.Add(new { lane = "alternative-package-graph", errorEvents = errors,
                        error = "Retained authored package readers/conditions failed; see exact nested source declarations." });
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "alternative-record", error = error.Message }); }
            finally { active.Remove(key); }
        }
    }
}
