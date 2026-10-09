using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // Complete source/runtime field owners may publish contributor ModInfo or
    // quest objects. This method does not provide either missing owner or fill
    // an uncovered field, fabricate a handle or alias a Script as another class.
    internal NativeNvseSourceObject PublishNvseSourceData(NativeNvsePlugin plugin, NativeNvseDataAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); authority.RequireCurrent();
        if (string.IsNullOrWhiteSpace(authority.SourceOwner) || authority.SourceSha256.Length != 64 || !authority.SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Native data publication lacks its exact original source identity.");
        var snapshot = authority.Read(); var fields = ComposeNvseSourceData(snapshot);
        foreach (var dependency in snapshot.Dependencies) VerifyNvseSourceObject(plugin, dependency);
        if (_nvseSourceObjects.Values.Any(value => snapshot.FormId != 0 && value.FormId == snapshot.FormId))
            throw new InvalidOperationException("An actual source form already owns this native identity.");
        var lease = authority.RetainSource();
        try
        {
            var editor = snapshot.EditorId.ToArray().Concat(new byte[] { 0 }).ToArray();
            var metadata = PublishNvseBytes(editor);
            var image = AllocateGuest(checked((uint)fields.Length), NativePluginGuestAccess.ReadWrite, fields.Length <= MaximumGuestTransfer ? fields : default);
            if (fields.Length > MaximumGuestTransfer) WriteGuestChunks(image, fields);
            var result = new NativeNvseSourceObject(Generation, plugin.Module, checked(++_nextNvseSourceObject), snapshot.FormId,
                image, metadata, null, authority, lease, snapshot.Dependencies, FingerprintNvseData(authority, snapshot), snapshot.Class);
            using var reader = Exchange(NativePluginDomainOperation.NvseObjectBind, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(result.Id); writer.Write((uint)snapshot.Class); writer.Write(image.Handle); writer.Write(image.Address); writer.Write(image.Length);
                writer.Write(snapshot.FormId); writer.Write(metadata.Handle); writer.Write(metadata.Address);
            }));
            if (reader.ReadUInt64() != result.Id || reader.ReadUInt32() != image.Address) throw new InvalidDataException("Native data object identity drifted.");
            var vtable = reader.ReadUInt32(); Finish(reader);
            if ((snapshot.Class == NativeNvseSourceClass.ModInfo) != (vtable == 0)) throw new InvalidDataException("Native class/vtable declaration drifted.");
            result.Image = image with { Access = NativePluginGuestAccess.ReadOnly }; _guestAllocations[image.Handle] = result.Image;
            _nvseSourceObjects.Add(result.Id, result); foreach (var dependency in result.Dependencies) ++dependency.Dependents;
            return result;
        }
        catch (Exception error) { lease.Dispose(); throw Fatal(error); }
    }

    private static byte[] ComposeNvseSourceData(NativeNvseDataSnapshot snapshot)
    {
        if (snapshot.Class is not (NativeNvseSourceClass.ModInfo or NativeNvseSourceClass.Quest) ||
            snapshot.Extent <= 0 || snapshot.Extent > MaximumGuestCommittedBytes || string.IsNullOrWhiteSpace(snapshot.LayoutOwner) ||
            snapshot.Fields is null || snapshot.Dependencies is null || snapshot.EditorId.Span.Contains((byte)0) ||
            (snapshot.Class == NativeNvseSourceClass.ModInfo) != (snapshot.FormId == 0))
            throw new NotSupportedException("Source data object needs its complete actual class/layout/identity declaration.");
        var bytes = new byte[snapshot.Extent]; var coverage = new bool[snapshot.Extent];
        // Actual first-party class/component tables own their native pointer fields.
        if (snapshot.Class == NativeNvseSourceClass.Quest)
        {
            if (bytes.Length != 108) throw new InvalidDataException("Quest class extent differs from its complete source declaration.");
            foreach (var at in new[] { 0, 24, 36, 48 }) Array.Fill(coverage, true, at, 4);
        }
        foreach (var field in snapshot.Fields)
        {
            if (field.Offset < 0 || field.Bytes.Length == 0 || field.Offset > bytes.Length - field.Bytes.Length || string.IsNullOrWhiteSpace(field.Owner))
                throw new InvalidDataException("A native class field has no exact extent/source owner.");
            for (var at = field.Offset; at < field.Offset + field.Bytes.Length; ++at)
                if (coverage[at]) throw new InvalidDataException("Native class field ownership overlaps."); else coverage[at] = true;
            field.Bytes.Span.CopyTo(bytes.AsSpan(field.Offset));
        }
        var missing = Array.IndexOf(coverage, false);
        if (missing >= 0) throw new NotSupportedException($"Native {snapshot.Class} byte {missing:x} has no source/runtime field owner.");
        if (snapshot.Class == NativeNvseSourceClass.Quest && (bytes[4] != 71 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) != snapshot.FormId))
            throw new InvalidDataException("Native quest source fields disagree with its actual class/identity.");
        return bytes;
    }
}
