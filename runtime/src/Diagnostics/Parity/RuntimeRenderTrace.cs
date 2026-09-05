using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Diagnostics.Parity;

// A diagnostic observation owner, never a gameplay or render-state authority.
// It walks the actual scene and bound resources at an explicit draw boundary.
// Scene submission is distinguished from GPU execution and pixel attribution.
internal sealed class RuntimeRenderTrace : IDisposable
{
    private readonly Node _owner;
    private readonly string _directory;
    private readonly Func<FalloutPluginStack?> _stack;
    private readonly Func<object> _gameplay;
    private readonly ConcurrentDictionary<string, (FalloutPluginRecord Record, ReadOnlyMemory<byte> Bytes)> _records = new();
    private readonly ConcurrentQueue<object> _events = new();
    private readonly Dictionary<ulong, object> _renderResources = [];
    private readonly Dictionary<string, object> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _missing = [];
    private RenderTraceBlobStore? _blobs;
    private string? _captureDirectory;
    private object? _before;
    private ulong _request;
    private long _eventOrdinal;
    private int _queuedEvents;
    private int _lostEvents;
    private string? _error;
    private string? _lastReport;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal RuntimeRenderTrace(Node owner, string directory, Func<FalloutPluginStack?> stack, Func<object> gameplay)
    {
        _owner = owner; _directory = directory; _stack = stack; _gameplay = gameplay;
    }

    internal bool Enabled { get; private set; }
    internal bool Pending => _request != 0;
    internal object Status => new
    {
        enabled = Enabled,
        pending = Pending,
        events = _eventOrdinal,
        lostEvents = _lostEvents,
        report = _lastReport,
        error = _error,
        coverage = "source-reads,scene-submission,bound-resources,pre-post-draw,pixels",
        missing = new[] { "native-GPU-draw-execution", "per-pixel-contributor-IDs", "retail-frame-join", "complete-audio-events" }
    };

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (RuntimeLiveContentSource.Current is { } source)
            source.ResourceReadObserver = enabled ? ObserveResource : null;
        FalloutPluginRecord.ReadObserver = enabled ? ObserveRecord : null;
        if (!enabled)
        {
            _request = 0; _before = null; _blobs = null;
            _records.Clear(); _events.Clear(); _queuedEvents = 0;
            _renderResources.Clear(); _sources.Clear(); _missing.Clear();
        }
    }

    internal void Request(ulong request)
    {
        if (!Enabled) throw new InvalidOperationException("Enable render telemetry before requesting a trace.");
        if (Pending) throw new InvalidOperationException("A render trace is already pending.");
        _request = request;
    }

    private void Event(string kind, string identity)
    {
        var ordinal = Interlocked.Increment(ref _eventOrdinal);
        if (Interlocked.Increment(ref _queuedEvents) > 100000)
        {
            Interlocked.Decrement(ref _queuedEvents); Interlocked.Increment(ref _lostEvents); return;
        }
        _events.Enqueue(new { ordinal, nanoseconds = Nanoseconds(), kind, identity });
    }

    private void ObserveResource(string path, string identity, ReadOnlyMemory<byte> bytes) => Event("resource-read", identity);
    private void ObserveRecord(FalloutPluginRecord record, ReadOnlyMemory<byte> bytes)
    {
        _records[record.Plugin.Path + ":" + record.HeaderOffset] = (record, bytes);
        Event("record-read", record.FormKey.ToString());
    }

    internal void BeforeDraw()
    {
        if (!Enabled || !Pending) return;
        try
        {
            _error = null; _missing.Clear(); _sources.Clear(); _renderResources.Clear();
            _captureDirectory = Path.Combine(_directory, $"trace-{_request:D10}");
            if (Directory.Exists(_captureDirectory)) throw new IOException("Trace capture already exists.");
            Directory.CreateDirectory(_captureDirectory);
            _blobs = new RenderTraceBlobStore(Path.Combine(_captureDirectory, "blobs"));
            var begin = Nanoseconds();
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned content is absent.");
            var nodes = Walk(_owner.GetTree().Root).Select(NodeState).ToArray();
            // Backfill is an inventory of payloads actually held by the live
            // source owner. It is not fabricated history of earlier reads.
            foreach (var (identity, bytes) in content.CachedResources())
            {
                var extent = content.ResourceExtent(identity);
                _sources[identity] = new
                {
                    identity,
                    decoded = _blobs.Put(bytes.Span),
                    storage = DiskRange(extent.File, extent.Offset, extent.StoredBytes),
                    extent.Compressed,
                    evidence = "live-cache-backfill",
                    blocks = NifBlocks(identity, bytes)
                };
            }
            var records = _records.Values.Select(row => RecordState(row.Record, row.Bytes)).ToArray();
            var events = new List<object>();
            while (_events.TryDequeue(out var entry)) { events.Add(entry); Interlocked.Decrement(ref _queuedEvents); }
            _before = new
            {
                beginNanoseconds = begin,
                endNanoseconds = Nanoseconds(),
                drawCount = Engine.GetFramesDrawn(),
                gameplay = _gameplay(),
                nodes,
                resources = _renderResources,
                sources = _sources.Values.ToArray(),
                records,
                events
            };
        }
        catch (Exception exception) { Fail(exception); }
    }

    internal void AfterDraw(Image image)
    {
        if (_before is null || _captureDirectory is null || _blobs is null) return;
        try
        {
            var pixels = _blobs.Put(image.GetData());
            var preview = Path.Combine(_captureDirectory, "frame.png");
            File.WriteAllBytes(preview, image.SavePngToBuffer());
            var end = Nanoseconds();
            var viewports = Walk(_owner.GetTree().Root).OfType<Viewport>().Select(viewport =>
            {
                using var buffer = viewport.GetTexture().GetImage();
                var path = Path.Combine(_captureDirectory, "viewport-" + viewport.GetInstanceId() + ".png");
                File.WriteAllBytes(path, buffer.SavePngToBuffer());
                return new
                {
                    path = viewport.GetPath().ToString(),
                    preview = path,
                    width = buffer.GetWidth(),
                    height = buffer.GetHeight(),
                    pixels = _blobs.Put(buffer.GetData()),
                    format = buffer.GetFormat().ToString()
                };
            }).ToArray();
            var missing = _missing.Concat(new[] { "native-GPU-draw-execution", "per-pixel-contributor-IDs",
                "retail-frame-join", "complete-audio-events" }).Distinct().Order().ToArray();
            var report = new
            {
                schema = "opennv-render-trace/v1",
                engine = "opennv",
                process = System.Environment.ProcessId,
                request = _request,
                before = _before,
                after = new { nanoseconds = end, drawCount = Engine.GetFramesDrawn(), gameplay = _gameplay() },
                frame = new { pixels, preview, width = image.GetWidth(), height = image.GetHeight(), format = image.GetFormat().ToString() },
                viewports,
                missing,
                lostEvents = _lostEvents,
                parity = "unverified",
                completeness = "incomplete",
                observation = "scene-state-at-pre-draw-and-viewport-readback-at-post-draw"
            };
            _lastReport = Path.Combine(_captureDirectory, "trace.json");
            File.WriteAllText(_lastReport, JsonSerializer.Serialize(report, Json));
            File.WriteAllText(Path.Combine(_directory, "trace-latest.json"), JsonSerializer.Serialize(new { report = _lastReport, preview, missing, pixels }, Json));
            _request = 0; _before = null;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Fail(Exception exception)
    {
        _error = exception.Message; _before = null; _request = 0;
        // Failure remains diagnostic state; tracing must not advance, reset or
        // terminate the game when an observation owner fails.
        GD.PushError("OPENNV_RENDER_TRACE_FAILED " + exception);
    }

    private object NodeState(Node node)
    {
        var path = node.GetPath().ToString();
        var properties = new Dictionary<string, object?>();
        if (node is Node3D spatial)
        {
            properties["localTransform"] = Value(spatial.Transform);
            properties["worldTransform"] = Value(spatial.GlobalTransform);
            properties["visible"] = spatial.IsVisibleInTree();
        }
        if (node is Camera3D camera)
        {
            properties["projection"] = Value(camera.GetCameraProjection());
            properties["cameraTransform"] = Value(camera.GetCameraTransform());
            properties["current"] = camera.Current;
            properties["cullMask"] = camera.CullMask;
        }
        if (node is MeshInstance3D mesh && mesh.Mesh is { } geometry)
        {
            var model = SourceModel(mesh);
            if (model is null) _missing.Add(path + ":source-model");
            properties["sourceModel"] = model;
            properties["geometry"] = ResourceState(geometry);
            properties["bounds"] = Value(mesh.GetAabb());
            properties["projectedBounds"] = ProjectedBounds(mesh);
            properties["layers"] = mesh.Layers;
            properties["skeletonPath"] = mesh.Skeleton.ToString();
            properties["skin"] = mesh.Skin is { } skin ? ResourceState(skin) : null;
            properties["materials"] = Enumerable.Range(0, geometry.GetSurfaceCount()).Select(index => new
            {
                surface = index,
                bound = mesh.GetActiveMaterial(index) is { } material ? ResourceState(material) : null,
            }).ToArray();
            properties["instanceParameters"] = new[] { "source_ambient", "source_fog_color", "source_fog_range" }
                .ToDictionary(name => name, name => Value(mesh.GetInstanceShaderParameter(name)));
            properties["blendShapes"] = Enumerable.Range(0, mesh.GetBlendShapeCount()).Select(index => new
            {
                name = geometry is ArrayMesh shaped ? shaped.GetBlendShapeName(index).ToString() : index.ToString(CultureInfo.InvariantCulture),
                value = Value(mesh.GetBlendShapeValue(index)),
            }).ToArray();
            properties["submissionEvidence"] = "scene-instance;GPU-execution-unobserved";
        }
        if (node is Skeleton3D skeleton)
            properties["bones"] = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => new
            {
                index,
                name = skeleton.GetBoneName(index),
                parent = skeleton.GetBoneParent(index),
                rest = Value(skeleton.GetBoneRest(index)),
                pose = Value(skeleton.GetBonePose(index)),
                globalPose = Value(skeleton.GetBoneGlobalPose(index)),
            }).ToArray();
        if (node is Light3D or WorldEnvironment or Viewport or Camera3D)
            properties["storageProperties"] = Properties(node);
        if (node is CanvasItem canvas)
        {
            properties["canvasTransform"] = Value(canvas.GetGlobalTransformWithCanvas());
            properties["visible"] = canvas.IsVisibleInTree();
            if (canvas is Control control) properties["rect"] = Value(control.GetGlobalRect());
            if (canvas.Material is { } material) properties["material"] = ResourceState(material);
        }
        return new
        {
            path,
            parent = node.GetParent()?.GetPath().ToString(),
            type = node.GetClass().ToString(),
            viewport = node.GetViewport()?.GetPath().ToString(),
            metadata = Metadata(node),
            properties
        };
    }

    private object ResourceState(Resource resource)
    {
        var id = resource.GetInstanceId();
        if (!_renderResources.ContainsKey(id))
        {
            _renderResources[id] = new { building = true };
            var state = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["type"] = resource.GetClass().ToString(),
                ["name"] = resource.ResourceName,
                ["metadata"] = Metadata(resource)
            };
            state["properties"] = Properties(resource);
            if (resource is Shader shader) state["program"] = shader.Code;
            if (resource is ShaderMaterial material)
                state["uniforms"] = material.Shader?.GetShaderUniformList().Select(item => item.AsGodotDictionary()).ToDictionary(
                    item => item["name"].AsString(), item => Value(material.GetShaderParameter(item["name"].AsString())));
            if (resource is Mesh mesh)
                state["surfaces"] = Enumerable.Range(0, mesh.GetSurfaceCount()).Select(index => new
                {
                    index,
                    format = mesh is ArrayMesh arrays ? arrays.SurfaceGetFormat(index).ToString() : "unobserved",
                    primitive = mesh is ArrayMesh topology ? topology.SurfaceGetPrimitiveType(index).ToString() : "unobserved",
                    arrays = mesh.SurfaceGetArrays(index).Select((value, slot) => new { slot = ((Mesh.ArrayType)slot).ToString(), data = Value(value) }).ToArray(),
                }).ToArray();
            if (resource is Skin skin)
                state["binds"] = Enumerable.Range(0, skin.GetBindCount()).Select(index => new
                { index, bone = skin.GetBindBone(index), name = skin.GetBindName(index).ToString(), pose = Value(skin.GetBindPose(index)) }).ToArray();
            if (resource is Texture2D texture)
            {
                using var image = texture.GetImage();
                if (image is not null && !image.IsEmpty()) state["decodedImage"] = new
                { width = image.GetWidth(), height = image.GetHeight(), format = image.GetFormat().ToString(), bytes = _blobs!.Put(image.GetData()) };
                else _missing.Add("texture:" + id + ":readback");
            }
            _renderResources[id] = state;
        }
        return new { resource = id };
    }

    private Dictionary<string, object?> Properties(GodotObject value) => value.GetPropertyList()
        .Where(item => (item["usage"].AsInt64() & (long)PropertyUsageFlags.Storage) != 0 && item["name"].AsString() != "script")
        .ToDictionary(item => item["name"].AsString(), item => Value(value.Get(item["name"].AsString())));

    private Dictionary<string, object?> Metadata(GodotObject value)
    {
        var metadata = value.GetMetaList().ToDictionary(name => name.ToString(), name => Value(value.GetMeta(name)));
        foreach (var name in value.GetMetaList())
        {
            var content = value.GetMeta(name);
            if (name.ToString().Contains("unbound", StringComparison.Ordinal) || name.ToString().Contains("unresolved", StringComparison.Ordinal))
                _missing.Add(value.GetInstanceId() + ":" + name + "=" + content);
            if (content.VariantType == Variant.Type.String) ObserveForm(content.AsString());
        }
        return metadata;
    }

    private void ObserveForm(string text)
    {
        var split = text.LastIndexOf(':');
        if (split <= 0 || !uint.TryParse(text.AsSpan(split + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var objectId)) return;
        if (_stack()?.TryGetWinner(new FalloutFormKey(text[..split], objectId), out var record) != true) return;
        var identity = record.Plugin.Path + ":" + record.HeaderOffset;
        if (!_records.ContainsKey(identity)) _records[identity] = (record, record.ReadDataForObservation());
    }

    private object? Value(Variant value)
    {
        if (value.VariantType == Variant.Type.Nil) return null;
        if (value.VariantType == Variant.Type.Object)
        {
            var target = value.AsGodotObject();
            return target is Resource resource ? ResourceState(resource) : new { objectId = target?.GetInstanceId(), type = target?.GetClass().ToString() };
        }
        if (value.VariantType == Variant.Type.Array) return value.AsGodotArray().Select(Value).ToArray();
        if (value.VariantType == Variant.Type.Dictionary)
            return value.AsGodotDictionary().ToDictionary(pair => pair.Key.ToString(), pair => Value(pair.Value));
        var bytes = GD.VarToBytes(value);
        return new
        {
            type = value.VariantType.ToString(),
            display = bytes.Length <= 256 ? value.ToString() : $"{value.VariantType}: {bytes.Length} encoded bytes",
            encoding = "Godot-Variant-little-endian",
            bytes = bytes.Length <= 256 ? (object)Convert.ToBase64String(bytes) : _blobs!.Put(bytes)
        };
    }

    private object RecordState(FalloutPluginRecord record, ReadOnlyMemory<byte> data) => new
    {
        identity = record.FormKey.ToString(),
        record.Signature,
        record.RawFormId,
        record.Flags,
        record.FormVersion,
        plugin = record.Plugin.Path,
        record.HeaderOffset,
        record.DataOffset,
        record.StoredSize,
        record.IsCompressed,
        storage = DiskRange(record.Plugin.Path, record.HeaderOffset, checked(record.StoredSize + (int)(record.DataOffset - record.HeaderOffset))),
        decoded = _blobs!.Put(data.Span),
        evidence = "observed-read-or-current-winning-record-backfill",
    };

    private object DiskRange(string file, long offset, int length)
    {
        using var stream = new FileStream(file, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        stream.Position = offset;
        var bytes = new byte[length]; stream.ReadExactly(bytes);
        return new { file, offset, length, bytes = _blobs!.Put(bytes), evidence = "disk-range-at-capture" };
    }

    private object? NifBlocks(string identity, ReadOnlyMemory<byte> bytes)
    {
        if (!identity.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) && !identity.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var source = FalloutNifFile.Read(bytes);
            return source.Blocks.Select(block => new
            {
                block.Index,
                block.TypeName,
                block.Offset,
                block.Size,
                bytes = _blobs!.Put(bytes.Span.Slice(block.Offset, block.Size))
            }).ToArray();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        { _missing.Add(identity + ":block-map:" + error.Message); return null; }
    }

    private static string? SourceModel(Node node)
    {
        for (Node? current = node; current is not null; current = current.GetParent())
            if (current.HasMeta("opennv_source_model")) return current.GetMeta("opennv_source_model").AsString();
        return null;
    }
    private static object? ProjectedBounds(MeshInstance3D mesh)
    {
        var camera = mesh.GetViewport().GetCamera3D();
        if (camera is null || !mesh.IsVisibleInTree()) return null;
        var bounds = mesh.GetAabb();
        var points = Enumerable.Range(0, 8).Select(index => mesh.GlobalTransform * bounds.GetEndpoint(index)).ToArray();
        if (points.Any(camera.IsPositionBehind)) return null;
        var screen = points.Select(camera.UnprojectPosition).ToArray();
        return new
        {
            x = screen.Min(point => point.X),
            y = screen.Min(point => point.Y),
            width = screen.Max(point => point.X) - screen.Min(point => point.X),
            height = screen.Max(point => point.Y) - screen.Min(point => point.Y),
            evidence = "static-AABB-candidate;occlusion-alpha-skinning-not-resolved"
        };
    }
    private static IEnumerable<Node> Walk(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren()) foreach (var node in Walk(child)) yield return node;
    }
    private static long Nanoseconds() => checked((long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)));
    public void Dispose() => SetEnabled(false);
}
