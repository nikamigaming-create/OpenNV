using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly Dictionary<ulong, NativeNvseSourceGraph> _nvseSourceGraphs = [];

    internal NativeNvseSourceGraph PublishNvseSourceGraph(NativeNvsePlugin plugin,
        IReadOnlyList<NativeNvseSourceGraphNode> declarations)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall();
        if (declarations.Count == 0 || declarations.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != declarations.Count ||
            declarations.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Extent == 0 || row.Extent > MaximumGuestCommittedBytes ||
                row.Class is not (NativeNvseSourceClass.Script or NativeNvseSourceClass.ModInfo or NativeNvseSourceClass.Quest) ||
                (row.Class == NativeNvseSourceClass.ModInfo) != (row.FormId == 0) || row.Class == NativeNvseSourceClass.Script && row.Extent != 84) ||
            declarations.Where(row => row.FormId != 0).Select(row => row.FormId).Distinct().Count() != declarations.Count(row => row.FormId != 0) ||
            declarations.Any(row => row.FormId != 0 && _nvseSourceObjects.Values.Any(value => value.FormId == row.FormId)))
            throw new InvalidDataException("Native graph declarations have incomplete or competing actual identities.");
        var context = new NativeNvseSourceGraphContext();
        var leases = new List<IDisposable>();
        try
        {
            // Actual OS allocations own undefined constructor padding. The
            // receipt is distinct from source-initialized or live-state fields.
            foreach (var declaration in declarations)
            {
                var image = AllocateGuest(declaration.Extent, NativePluginGuestAccess.ReadWrite);
                var authority = declaration.Bind(context); authority.RequireCurrent();
                if (string.IsNullOrWhiteSpace(authority.SourceOwner) || authority.SourceSha256.Length != 64 || !authority.SourceSha256.All(Uri.IsHexDigit) ||
                    declaration.Class == NativeNvseSourceClass.Script && authority is not NativeNvseScriptAuthority ||
                    declaration.Class != NativeNvseSourceClass.Script && authority is not NativeNvseGraphDataAuthority)
                    throw new InvalidDataException("Native graph class has no exact source/runtime authority.");
                var lease = authority.RetainSource(); leases.Add(lease);
                // Metadata=image is a private staging sentinel, replaced before
                // registration. Neither the object nor the sentinel is exposed.
                var value = new NativeNvseSourceObject(Generation, plugin.Module, checked(++_nextNvseSourceObject), declaration.FormId,
                    image, image, null, authority, lease, [], "", declaration.Class)
                { Staged = true };
                context.Add(declaration.Key, value, ReadNvseGraphBytes(image));
            }
            foreach (var value in context.Objects.Values)
            {
                if (value.Authority is NativeNvseScriptAuthority script)
                {
                    var snapshot = script.Read(); ValidateNvseScriptSnapshot(snapshot);
                    if (snapshot.FormId != value.FormId || !StringComparer.OrdinalIgnoreCase.Equals(script.CodeSha256,
                        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot.Code.Span))))
                        throw new InvalidDataException("Staged Script source/code identity changed.");
                    if (!snapshot.Code.IsEmpty) value.Code = PublishNvseBytes(snapshot.Code.Span);
                    var metadata = new NativeNvseScriptMetadata(snapshot);
                    var bytes = metadata.Bytes;
                    var allocation = AllocateGuest(checked((uint)bytes.Length), NativePluginGuestAccess.ReadWrite);
                    metadata.Relocate(bytes, allocation.Address); WriteGuestChunks(allocation, bytes);
                    value.Metadata = SealNvseGuest(allocation);
                }
                else
                {
                    var owner = (NativeNvseGraphDataAuthority)value.Authority;
                    var initial = owner.Metadata(0);
                    if (initial.Bytes.Length == 0 || initial.EditorOffset >= initial.Bytes.Length)
                        throw new InvalidDataException("Native class metadata has no complete editor identity.");
                    var allocation = AllocateGuest(checked((uint)initial.Bytes.Length), NativePluginGuestAccess.ReadWrite);
                    value.Metadata = allocation;
                    var relocated = owner.Metadata(allocation.Address);
                    if (relocated.Bytes.Length != initial.Bytes.Length || relocated.EditorOffset != initial.EditorOffset)
                        throw new InvalidDataException("Native class relocation changed its metadata extent/identity.");
                    WriteGuestChunks(allocation, relocated.Bytes); value.Metadata = SealNvseGuest(allocation);
                }
            }
            var graph = new NativeNvseSourceGraph(Generation, plugin.Module, context);
            foreach (var value in graph.Objects.Values)
            {
                var (bytes, editor, dependencies, fingerprint) = ComposeNvseGraphObject(value);
                if (bytes.Length != value.Image.Length || dependencies.Any(target => target.Generation != Generation || target.Module != plugin.Module || target.Retired ||
                    !graph.Objects.Values.Contains(target) && !_nvseSourceObjects.Values.Contains(target)))
                    throw new InvalidDataException("Native graph field extent or linked lifetime changed.");
                value.Dependencies = System.Collections.Immutable.ImmutableArray.CreateRange(dependencies.Distinct());
                value.PublishedSha256 = fingerprint;
                WriteGuestChunks(value.Image, bytes);
                using var reader = Exchange(NativePluginDomainOperation.NvseObjectBind, Payload(writer =>
                {
                    writer.Write(plugin.Module); writer.Write(value.Id); writer.Write((uint)value.Class); writer.Write(value.Image.Handle);
                    writer.Write(value.Address); writer.Write(value.Image.Length); writer.Write(value.FormId);
                    writer.Write(value.Metadata.Handle); writer.Write(editor);
                }));
                if (reader.ReadUInt64() != value.Id || reader.ReadUInt32() != value.Address)
                    throw new InvalidDataException("Native cyclic object bind returned a different identity.");
                var table = reader.ReadUInt32(); Finish(reader);
                if ((value.Class == NativeNvseSourceClass.ModInfo) != (table == 0))
                    throw new InvalidDataException("Native cyclic object class/vtable receipt changed.");
                value.Image = value.Image with { Access = NativePluginGuestAccess.ReadOnly };
                _guestAllocations[value.Image.Handle] = value.Image;
                graph.NativeImages.Add(value.Id, ReadNvseGraphBytes(value.Image));
                graph.NativeMetadata.Add(value.Id, ReadNvseGraphBytes(value.Metadata));
            }
            foreach (var value in graph.Objects.Values) value.Authority.RequireCurrent();
            foreach (var value in graph.Objects.Values)
            {
                value.Staged = false; _nvseSourceObjects.Add(value.Id, value); _nvseSourceGraphs.Add(value.Id, graph);
                foreach (var dependency in value.Dependencies) ++dependency.Dependents;
            }
            return graph;
        }
        catch (Exception error)
        {
            foreach (var lease in leases) try { lease.Dispose(); } catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            context.Retire(); throw Fatal(error);
        }
    }

    private byte[] ReadNvseGraphBytes(NativePluginGuestAllocation allocation)
    {
        var bytes = new byte[checked((int)allocation.Length)];
        for (var at = 0; at < bytes.Length; at += checked((int)MaximumGuestTransfer))
            ReadGuest(allocation, checked((uint)at), Math.Min(bytes.Length - at, checked((int)MaximumGuestTransfer))).CopyTo(bytes, at);
        return bytes;
    }

    private static (byte[] Image, uint Editor, IEnumerable<NativeNvseSourceObject> Dependencies, string Fingerprint)
        ComposeNvseGraphObject(NativeNvseSourceObject value)
    {
        if (value.Authority is NativeNvseScriptAuthority authority)
        {
            var snapshot = authority.Read(); ValidateNvseScriptSnapshot(snapshot);
            var metadata = new NativeNvseScriptMetadata(snapshot); var fields = new byte[84];
            fields[4] = 17; snapshot.FormRuntimeBytes.Span.CopyTo(fields.AsSpan(5, 3));
            Put(8, snapshot.Flags); Put(12, snapshot.FormId); metadata.List(fields, 16, metadata.Contributors, value.Metadata.Address);
            snapshot.Info.Span.CopyTo(fields.AsSpan(24, 20));
            Put(44, metadata.Text is { } text ? checked(value.Metadata.Address + text) : 0); Put(48, value.Code?.Address ?? 0);
            Put(52, snapshot.RuntimeWord); Put(56, BitConverter.SingleToUInt32Bits(snapshot.DelayCounter));
            Put(60, BitConverter.SingleToUInt32Bits(snapshot.SecondsPassed)); Put(64, snapshot.Quest?.Address ?? 0);
            metadata.List(fields, 68, metadata.References, value.Metadata.Address); metadata.List(fields, 76, metadata.Variables, value.Metadata.Address);
            return (fields, checked(value.Metadata.Address + metadata.EditorId),
                snapshot.Contributors.Concat(snapshot.References.Where(row => row.Form is not null).Select(row => row.Form!))
                    .Concat(snapshot.Quest is { } quest ? [quest] : Array.Empty<NativeNvseSourceObject>()), FingerprintNvseScript(authority, snapshot));
            void Put(int at, uint word) => BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(at), word);
        }
        var data = (NativeNvseGraphDataAuthority)value.Authority; var declared = data.Read();
        return (ComposeNvseSourceData(declared), checked(value.Metadata.Address + data.Metadata(value.Metadata.Address).EditorOffset),
            declared.Dependencies, FingerprintNvseData(data, declared));
    }
}
