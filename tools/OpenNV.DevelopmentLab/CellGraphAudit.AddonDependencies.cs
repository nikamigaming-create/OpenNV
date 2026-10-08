using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    // Only compact source declarations live here. No NIF/block payload, native
    // scene, decoded collision array or transformed retail launch asset is held.
    internal sealed class AddonDependencyDeclaration
    {
        public string Resource { get; init; } = "";
        public string? ResourceSource { get; init; }
        public string? ResourceSha256 { get; init; }
        public int Block { get; init; }
        public int Offset { get; init; }
        public int Bytes { get; init; }
        public string Type { get; init; } = "";
        public string Node { get; init; } = "";
        public uint NodeFlags { get; init; }
        public float[] Translation { get; init; } = [];
        public float[] Rotation { get; init; } = [];
        public float Scale { get; init; }
        public uint Index { get; init; }
        public byte ValueFlags { get; init; }
        public AddonDefinitionDeclaration? Definition { get; set; }
        public AddonModelDeclaration? Model { get; set; }
        public string Disposition { get; set; } = "encoded-declaration; index-binding-uninspected";
        public string NativeConfiguration { get; set; } = "uninspected";
        public string NativeInstance { get; } = "unverified; no instance or draw created";
        public string ComposedCollision { get; } = "uninspected; nested model read is not placed collision projection";
        public string NativeAudio { get; set; } = "uninspected";
        public List<object> Failures { get; } = [];
        internal bool Resolved { get; set; }
    }

    internal sealed record AddonFieldDeclaration(int Ordinal, string Signature, int Bytes, string Sha256);
    internal sealed record AddonSoundDeclaration(uint? Encoded, string? Form, string? Winner, string? Signature,
        bool? Deleted, string AudioOwner);
    internal sealed record AddonDefinitionDeclaration(string Form, uint Index, string Winner, string Sha256,
        uint RecordFlags, string DeclaredModel, string Model, ushort ParticleCap, ushort Flags,
        AddonFieldDeclaration[] Fields, AddonSoundDeclaration Sound);
    internal sealed record AddonModelDeclaration(string Path, string Kind, string? Source, string? Sha256,
        string ReaderDisposition, string NativeAdmission);

    internal sealed class SourceAddonDependencies
    {
        private sealed record Binding(AddonDefinitionDeclaration? Definition, Exception? Failure);
        private sealed class Frame(ResourceRow resource, uint? index)
        {
            internal readonly ResourceRow Resource = resource;
            internal readonly uint? Index = index;
            internal int Next;
        }

        private readonly FalloutPluginStack _records;
        private readonly Lazy<FalloutAddonNodes> _catalog;
        private readonly Dictionary<uint, Binding> _bindings = [];
        private readonly Dictionary<uint, bool> _visited = [];
        private readonly Dictionary<uint, AddonModelDeclaration> _targets = [];
        private readonly HashSet<string> _rootInspected = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<AddonDefinitionDeclaration> _definitions = [];
        private Exception? _catalogFailure;
        private int _catalogAttempts;
        private int _captured;
        private bool _inventoryRead;
        private bool _processing;
        private string[] _modelRoots = [];

        internal SourceAddonDependencies(RuntimeLiveContentSource source, FalloutPluginStack records)
        {
            if (!ReferenceEquals(records.OwnedSource, source))
                throw new ArgumentException("ADDN catalog belongs to a different owned content selection.", nameof(records));
            _records = records;
            _catalog = new(() => { _catalogAttempts++; return new(records); });
        }

        internal bool CatalogPassed { get { ReadInventory(); return _catalogFailure is null; } }
        internal IReadOnlyList<string> ModelRoots { get { ReadInventory(); return _modelRoots; } }
        internal object Coverage
        {
            get
            {
                ReadInventory();
                return new { catalog = "actual FalloutAddonNodes; every effective ADDN", effectiveRecords = _records.EffectiveRecords("ADDN").Count,
                    catalogPassed = _catalogFailure is null, catalogAttempts = _catalogAttempts,
                    catalogFailure = _catalogFailure is null ? null : new { type = _catalogFailure.GetType().Name, error = _catalogFailure.Message },
                    definitions = _definitions, decodedValueDeclarations = _captured, addonBearingRootModels = _rootInspected.Count,
                    visitedAddonIndices = _visited.Count, nativeInstances = (int?)null, nativeAdmission = "unverified",
                    audio = "SNAM retains the existing OPENNV_ADDON_AUDIO_UNBOUND owner",
                    instanceCollision = "uninspected; no nested placement shapes were fabricated" };
            }
        }

        internal static void RefuseMissingOwner(ResourceRow row, FalloutNifNode node)
        {
            var declaration = Declaration(row, node); row.AddonLinks.Add(declaration);
            Refuse(row, declaration, "nif-addon-source-owner", "BSValueNode has no exact owned ADDN stack.");
        }

        internal void Capture(ResourceRow row, FalloutNifNode node)
        {
            row.AddonLinks.Add(Declaration(row, node)); _captured++;
        }

        private static AddonDependencyDeclaration Declaration(ResourceRow row, FalloutNifNode node)
        {
            var value = node.Value ?? throw new ArgumentException("Node has no encoded ADDN index.", nameof(node));
            return new() { Resource = row.Path, ResourceSource = row.Source, ResourceSha256 = row.Sha256,
                Block = node.Block.Index, Offset = node.Block.Offset, Bytes = node.Block.Size, Type = node.Block.TypeName,
                Node = node.Name, NodeFlags = node.Flags,
                Translation = [node.Transform.Translation.X, node.Transform.Translation.Y, node.Transform.Translation.Z],
                Rotation = node.Transform.RotationRowMajor.ToArray(), Scale = node.Transform.Scale,
                Index = value.Value, ValueFlags = value.Flags };
        }

        private bool TryCatalog(out FalloutAddonNodes? catalog)
        {
            try { catalog = _catalog.Value; return true; }
            catch (Exception error) { _catalogFailure = error; catalog = null; return false; }
        }

        private Binding Resolve(uint index)
        {
            if (_bindings.TryGetValue(index, out var previous)) return previous;
            Binding result;
            try
            {
                if (!TryCatalog(out var catalog)) throw _catalogFailure!;
                var definition = catalog!.Get(index);
                var record = _records.GetEffective(definition.Form);
                if (record.Signature != "ADDN") throw new InvalidDataException("Indexed addon does not belong to the exact winning ADDN.");
                var fields = record.ReadSubrecords().ToArray();
                var sound = fields.SingleOrDefault(field => field.Signature == "SNAM").Data;
                var encoded = sound.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(sound.Span) : (uint?)null;
                FalloutPluginRecord? soundWinner = null;
                if (definition.Sound is { } soundForm && _records.TryGetWinner(soundForm, out var winningSound)) soundWinner = winningSound;
                var declaration = new AddonDefinitionDeclaration(definition.Form.ToString(), index, record.Plugin.Name,
                    Hash(record.ReadData()), record.Flags,
                    FalloutPlugin.DecodeZeroTerminated(fields.Single(field => field.Signature == "MODL").Data.Span, "addon model"),
                    Canonical(definition.Model), definition.ParticleCap, definition.Flags,
                    fields.Select((field, ordinal) => new AddonFieldDeclaration(ordinal, field.Signature, field.Data.Length, Hash(field.Data.Span))).ToArray(),
                    new(encoded, definition.Sound?.ToString(), soundWinner?.Plugin.Name, soundWinner?.Signature, soundWinner?.IsDeleted,
                        definition.Sound is null ? "not-declared by actual optional FormID owner" : "unbound; OPENNV_ADDON_AUDIO_UNBOUND"));
                result = new(declaration, null);
            }
            catch (Exception error) { result = new(null, error); }
            _bindings.Add(index, result); return result;
        }

        private void ReadInventory()
        {
            if (_inventoryRead) return;
            _inventoryRead = true;
            if (!TryCatalog(out _)) return;
            foreach (var record in _records.EffectiveRecords("ADDN"))
            {
                // The actual constructor validated this field. This reread is
                // provenance for its returned definition, not another parser.
                try
                {
                    var index = BinaryPrimitives.ReadUInt32LittleEndian(record.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
                    var binding = Resolve(index);
                    if (binding.Definition is { } definition) _definitions.Add(definition);
                    else _catalogFailure ??= binding.Failure;
                }
                catch (Exception error) { _catalogFailure ??= error; }
            }
            _modelRoots = _definitions.Select(definition => definition.Model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal void InspectAll(Func<string, string, ResourceRow> read)
        {
            ReadInventory();
            foreach (var definition in _definitions) Inspect(read(definition.Model, "model"), read);
        }

        internal void Inspect(ResourceRow resource, Func<string, string, ResourceRow> read)
        {
            if (_processing || resource.AddonLinks.Count == 0 || !_rootInspected.Add(resource.Path)) return;
            _processing = true;
            var frames = new List<Frame> { new(resource, null) };
            try
            {
                while (frames.Count != 0)
                {
                    var frame = frames[^1];
                    if (frame.Next == frame.Resource.AddonLinks.Count)
                    {
                        if (frame.Index is { } completed) _visited[completed] = true;
                        frames.RemoveAt(frames.Count - 1); continue;
                    }
                    var use = frame.Resource.AddonLinks[frame.Next++];
                    if (!use.Resolved)
                    {
                        use.Resolved = true;
                        if (use.ValueFlags != 0)
                            Refuse(frame.Resource, use, "nif-addon-value-flags", "BSValueNode flags need the unbound player-adjust/world-Z owner.");
                        var binding = Resolve(use.Index);
                        if (binding.Definition is not { } definition)
                        {
                            Refuse(frame.Resource, use, _catalogFailure is null ? "nif-addon-index" : "nif-addon-catalog",
                                binding.Failure!.Message); continue;
                        }
                        use.Definition = definition;
                        use.NativeAudio = definition.Sound.AudioOwner;
                        if (use.ValueFlags == 0) use.NativeConfiguration = "source transport matched; native instance admission unverified";
                        if (definition.Flags > 1)
                            Refuse(frame.Resource, use, "nif-addon-configuration-flags", "ADDN configuration flags are unbound in native BindAddon.");
                        frame.Resource.AddDependency(definition.Model, "model");
                        use.Disposition = "actual catalog index resolved; typed model dependency declared";
                    }
                    if (use.Definition is not { } target) continue;
                    if (_visited.TryGetValue(use.Index, out var complete))
                    {
                        use.Model = _targets.GetValueOrDefault(use.Index);
                        if (!complete)
                            Refuse(frame.Resource, use, "nif-addon-cycle", "ADDN model graph repeats an active index.",
                                frames.Where(active => active.Index is not null).Select(active => active.Index!.Value).Append(use.Index).ToArray());
                        continue;
                    }
                    ResourceRow child;
                    try { child = read(target.Model, "model"); }
                    catch (Exception error) { Refuse(frame.Resource, use, "nif-addon-model-read", error.Message); continue; }
                    var model = new AddonModelDeclaration(child.Path, "model", child.Source, child.Sha256,
                        child.Stream is null ? "model layout/read refused; see target resource failures" : "model source layout inspected; see target resource failures",
                        "unverified; no instance, composed shape or draw created");
                    use.Model = model; _targets[use.Index] = model;
                    _visited.Add(use.Index, false);
                    frames.Add(new(child, use.Index));
                }
            }
            finally
            {
                // Never let an unexpected delegate fault leave active state
                // standing in for a completed dependency on the next caller.
                foreach (var frame in frames)
                    if (frame.Index is { } active) _visited.Remove(active);
                if (frames.Count != 0) _rootInspected.Remove(resource.Path);
                _processing = false;
            }
        }

        private static void Refuse(ResourceRow row, AddonDependencyDeclaration use, string lane, string error, uint[]? ancestors = null)
        {
            var failure = new { lane, resource = row.Path, block = use.Block, index = use.Index,
                valueFlags = use.ValueFlags, addon = use.Definition?.Form, ancestors, error };
            use.Failures.Add(failure); row.Failures.Add(failure);
            if (lane is "nif-addon-value-flags" or "nif-addon-configuration-flags") use.NativeConfiguration = "refused; source flags have no native owner";
            else use.Disposition = "addon dependency refused; original source declaration retained";
        }
    }
}
