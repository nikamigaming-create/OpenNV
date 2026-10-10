using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // Layouts already constructed by the source Script/Quest/ModInfo owners.
    // Pointer fields retain their emitted object/metadata/code allocation.
    // Unspecified allocation padding and vtables are not scalar fields.
    private static IReadOnlyList<NativeSourceFieldRegion> SourceMethodRegions(NativeNvseSourceObject value)
    {
        if (value.Authority is NativeNvseScriptAuthority script)
        {
            var current = script.Read(); ValidateNvseScriptSnapshot(current);
            return [new(4, 1, NativeSourceFieldValueRole.Scalar, "public-Script-TESForm-type"),
                new(8, 8, NativeSourceFieldValueRole.Scalar, "actual-Script-flags-and-FormID"),
                new(16, 4, NativeSourceFieldValueRole.Object, "actual-ordered-contributor-list", true),
                new(20, 4, NativeSourceFieldValueRole.Buffer, "actual-ordered-contributor-list-link", true),
                new(24, 20, NativeSourceFieldValueRole.Scalar, "original-SCHR/native-ScriptInfo"),
                new(44, 4, NativeSourceFieldValueRole.Buffer, "actual-original-SCTX-allocation", true),
                new(48, 4, NativeSourceFieldValueRole.Buffer, "actual-original-SCDA-allocation", true),
                new(52, 12, NativeSourceFieldValueRole.Scalar, "actual-Script-runtime-and-clock-fields"),
                new(64, 4, NativeSourceFieldValueRole.Object, "actual-Script-Quest-link", true),
                new(68, 4, NativeSourceFieldValueRole.Buffer, "actual-source-reference-list", true),
                new(72, 4, NativeSourceFieldValueRole.Buffer, "actual-source-reference-list-link", true),
                new(76, 4, NativeSourceFieldValueRole.Buffer, "actual-ordered-variable-info-list", true),
                new(80, 4, NativeSourceFieldValueRole.Buffer, "actual-ordered-variable-info-link", true)];
        }
        if (value.Authority is not NativeNvseDataAuthority data)
            throw new NotSupportedException("Source field method class has no actual complete field producer.");
        var snapshot = data.Read();
        if (snapshot.Class != value.Class || snapshot.FormId != value.FormId || snapshot.Extent != value.Image.Length)
            throw new InvalidDataException("Source field method class/extent changed before its actual read.");
        // Roles come from the reviewed constructed class layout, never from
        // a word resembling an address or from the diagnostic field name.
        // Require complete coverage by the actual producer for each read.
        NativeSourceFieldRegion[] roles = value.Class switch
        {
            NativeNvseSourceClass.Quest =>
            [Scalar(4,1), Scalar(8,8), Object(16), Buffer(20), Object(28), Scalar(32,1),
                Buffer(40), Scalar(44,4), Buffer(52), Scalar(56,6), Scalar(64,4),
                Buffer(68), Buffer(72), Buffer(76), Buffer(80), Buffer(84), Buffer(88),
                // Offset92 is a real native event-list pointer. This method
                // family does not reinterpret its independent owner as scalar.
                Scalar(96,1), Buffer(100), Scalar(104,4)],
            NativeNvseSourceClass.ModInfo =>
            [Scalar(0x20,260), Scalar(0x124,260), Scalar(0x22c,4), Scalar(0x240,24),
                Scalar(0x258,24), Scalar(0x29c,320), Scalar(0x3dc,12),
                Buffer(0x3ec), Buffer(0x3f0), Buffer(0x3f4), Buffer(0x3f8),
                Scalar(0x3fc,4), Buffer(0x400), Scalar(0x40c,1),
                Buffer(0x410), Scalar(0x414,4), Buffer(0x418), Scalar(0x41c,4)],
            _ => throw new NotSupportedException("Source field method class is not one of the actual constructed native layouts.")
        };
        foreach (var field in snapshot.Fields)
        {
            if (field.Bytes.IsEmpty || string.IsNullOrWhiteSpace(field.Owner))
                throw new InvalidDataException("Source field method encountered an unowned actual field.");
        }
        return roles;
        static NativeSourceFieldRegion Scalar(int at, int length) => new(at, length, NativeSourceFieldValueRole.Scalar, "reviewed-actual-class-scalar");
        static NativeSourceFieldRegion Object(int at) => new(at, 4, NativeSourceFieldValueRole.Object, "reviewed-actual-class-object", true);
        static NativeSourceFieldRegion Buffer(int at) => new(at, 4, NativeSourceFieldValueRole.Buffer, "reviewed-actual-class-buffer", true);
    }

    private NativeSourceFieldValue ReadSourceMethodMemory(NativeNvsePlugin plugin, NativeSourceFieldValue basis, int width)
    {
        if (width is not (1 or 2 or 4)) throw new InvalidDataException("Source field read has no exact scalar width.");
        if (basis.Object is { } value)
        {
            VerifyNvseSourceObject(plugin, value);
            if (basis.Bits < value.Address || (ulong)basis.Bits + (uint)width > (ulong)value.Address + value.Image.Length)
                throw new InvalidDataException("Source field read leaves its genuine complete receiver allocation.");
            var offset = checked((int)(basis.Bits - value.Address));
            var region = SourceMethodRegions(value).SingleOrDefault(row => row.Offset <= offset && offset + width <= row.Offset + row.Bytes) ??
                throw new NotSupportedException("Source field getter reaches a vtable, unspecified padding or unowned field role.");
            if (value.Authority is NativeNvseDataAuthority declared)
            {
                var fields = declared.Read().Fields;
                for (var at = offset; at < offset + width; ++at)
                    if (!fields.Any(field => field.Offset <= at && at < field.Offset + field.Bytes.Length))
                        throw new NotSupportedException("Source field read contains a byte without its actual class producer.");
            }
            var bytes = ReadGuest(value.Image, checked((uint)offset), width); var bits = SourceFieldBits(bytes, width);
            if (_nvseSourceGraphs.TryGetValue(value.Id, out var graph))
            {
                if (graph.Retired || !graph.NativeImages.TryGetValue(value.Id, out var image) ||
                    !bytes.AsSpan().SequenceEqual(image.AsSpan(offset, width)))
                    throw new InvalidDataException("Source field read differs from the published complete current graph.");
            }
            // Non-graph Script objects still derive their fields from the same
            // actual authority; reconstruct the current owned image separately.
            else if (!bytes.AsSpan().SequenceEqual(SourceMethodImage(value).AsSpan(offset, width)))
                throw new InvalidDataException("Source field read differs from the actual current standalone class owner.");
            if (region.Role == NativeSourceFieldValueRole.Scalar) return new(bits, region.Role);
            if (width != 4 || offset != region.Offset)
                throw new NotSupportedException("A partial pointer field has no scalar or alias ownership.");
            if (bits == 0)
            {
                if (!region.Nullable) throw new InvalidDataException("Nonnullable source field was published as null.");
                return new(0, region.Role);
            }
            if (region.Role == NativeSourceFieldValueRole.Object)
            {
                var target = value.Dependencies.SingleOrDefault(target => target.Address == bits) ??
                    throw new NotSupportedException("Source object field has no genuine retained linked class.");
                VerifyNvseSourceObject(plugin, target); return new(bits, region.Role, target);
            }
            return FindSourceMethodBuffer(plugin, value, bits);
        }
        if (basis.Buffer is { } allocation)
        {
            VerifyGuest(allocation);
            if (basis.Bits < allocation.Address || (ulong)basis.Bits + (uint)width > (ulong)allocation.Address + allocation.Length)
                throw new InvalidDataException("Source metadata read leaves its genuine retained buffer.");
            var owner = _nvseSourceObjects.Values.SingleOrDefault(value =>
                ReferenceEquals(value.Metadata, allocation) || ReferenceEquals(value.Code, allocation)) ??
                throw new NotSupportedException("Source metadata buffer lost its actual constructing object.");
            VerifyNvseSourceObject(plugin, owner);
            var at = checked((uint)(basis.Bits - allocation.Address)); var bytes = ReadGuest(allocation, at, width);
            var current = SourceMethodBufferImage(owner, allocation);
            if (!bytes.AsSpan().SequenceEqual(current.AsSpan(checked((int)at), width)))
                throw new InvalidDataException("Source metadata bytes changed outside their authoritative publisher.");
            // The full native allocation and original bytes are owned, but a
            // metadata node's pointer/scalar member role is an independent
            // source declaration. Never infer it from numerical coincidence.
            if (!ReferenceEquals(owner.Code, allocation))
                throw new NotSupportedException("Nested source metadata read requires its exact constructed node-member role.");
            return new(SourceFieldBits(bytes, width), NativeSourceFieldValueRole.Scalar);
        }
        if (basis.Global is { } global)
        {
            if (width != 4 || basis.Bits != global.Address) throw new NotSupportedException("Source global partial/offset read has no actual cell authority.");
            RequireEngineGlobalCurrent(global, plugin); var host = EngineGlobalHost();
            var bits = _process.ReadEngineGlobal(global.Address, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes);
            if (bits != global.Value) throw new InvalidDataException("Native source global differs from its actual current producer.");
            return global.Kind == NativeEngineGlobalKind.Object ? new(bits, NativeSourceFieldValueRole.Object,
                global.Object ?? throw new InvalidDataException("Actual engine global omitted its complete object lease.")) : new(bits, NativeSourceFieldValueRole.Scalar);
        }
        throw new NotSupportedException("Source field address has no complete native object/buffer/global authority.");
    }

    private NativeSourceFieldValue FindSourceMethodBuffer(NativeNvsePlugin plugin, NativeNvseSourceObject owner, uint pointer)
    {
        VerifyNvseSourceObject(plugin, owner);
        var allocations = owner.Code is { } code ? new[] { owner.Metadata, code } : new[] { owner.Metadata };
        var actual = allocations.SingleOrDefault(allocation => pointer >= allocation.Address &&
            (ulong)pointer < (ulong)allocation.Address + allocation.Length) ??
            throw new NotSupportedException("Source buffer pointer has no genuine retained code/metadata allocation.");
        VerifyGuest(actual); return new(pointer, NativeSourceFieldValueRole.Buffer, Buffer: actual);
    }
    private byte[] SourceMethodImage(NativeNvseSourceObject value)
    {
        var image = value.Authority is NativeNvseScriptAuthority ? ComposeNvseGraphObject(value).Image :
            value.Authority is NativeNvseDataAuthority data ? ComposeNvseSourceData(data.Read()) :
            throw new NotSupportedException("Standalone source fields have no actual complete composing authority.");
        if (image.Length != value.Image.Length) throw new InvalidDataException("Current standalone source field extent changed.");
        return image;
    }
    private byte[] SourceMethodBufferImage(NativeNvseSourceObject owner, NativePluginGuestAllocation allocation)
    {
        if (ReferenceEquals(owner.Code, allocation) && owner.Authority is NativeNvseScriptAuthority script)
            return script.Read().Code.ToArray();
        if (_nvseSourceGraphs.TryGetValue(owner.Id, out var graph) && !graph.Retired &&
            graph.NativeMetadata.TryGetValue(owner.Id, out var actual)) return actual;
        if (owner.Authority is NativeNvseScriptAuthority source)
        {
            var metadata = new NativeNvseScriptMetadata(source.Read()); var bytes = metadata.Bytes;
            metadata.Relocate(bytes, allocation.Address); return bytes;
        }
        throw new NotSupportedException("Source metadata has no complete current composing authority.");
    }
    private static uint SourceFieldBits(byte[] bytes, int width) => width switch
    {
        1 => bytes[0],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        _ => throw new InvalidDataException("Source scalar width is unowned.")
    };
}
