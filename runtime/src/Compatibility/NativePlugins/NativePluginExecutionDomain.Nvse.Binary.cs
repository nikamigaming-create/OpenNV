using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private uint _nvseBinaryTable, _nvseBinaryRead, _nvseBinaryWrite, _nvseBinaryAlternateRead, _nvseBinaryAlternateWrite;

    internal void ConfigureNvseBinaryMethods(NativeNvsePlugin plugin, NativeNvseBinaryDeclaration declaration)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(declaration);
        if (_nvseBinaryDeclaration is not null || _nvseHostSource is null ||
            plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue) ||
            !StringComparer.OrdinalIgnoreCase.Equals(declaration.RuntimeSha256, _nvseHostSource.RuntimeSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(declaration.PluginSha256, plugin.Sha256) ||
            string.IsNullOrWhiteSpace(declaration.LayoutOwner) || declaration.Extent < 0x158 || declaration.Extent > MaximumPayload / 4 ||
            declaration.VirtualSlots is 0 or > 128 || declaration.Methods is null || declaration.Methods.Count is 0 or > 128 ||
            declaration.Procedures is null || string.IsNullOrWhiteSpace(declaration.Procedures.Owner))
            throw new InvalidDataException("Binary methods lack exact selected source/layout/module declarations before entry.");
        var methods = declaration.Methods.ToArray();
        var entries = new List<uint>(); var slots = new HashSet<int>();
        foreach (var method in methods)
        {
            if (method is null || !Enum.IsDefined(method.Method) || method.Abi != NativePluginAbi.Thiscall ||
                string.IsNullOrWhiteSpace(method.Owner) || method.VirtualSlot is { } slot &&
                (slot < 0 || slot >= declaration.VirtualSlots || !slots.Add(slot)))
                throw new InvalidDataException("Binary virtual/member ABI is absent, competing or unowned.");
            Entry(method.Address);
        }
        foreach (var procedure in new[] { declaration.Procedures.Read, declaration.Procedures.Write,
            declaration.Procedures.AlternateRead, declaration.Procedures.AlternateWrite }) Entry(procedure);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseBinaryMethods, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(declaration.Extent); writer.Write(declaration.VirtualSlots);
                writer.Write(checked((uint)methods.Length));
                foreach (var method in methods)
                {
                    writer.Write(method.Address); writer.Write((uint)method.Method); writer.Write((uint)method.Abi);
                    writer.Write(method.VirtualSlot is { } slot ? checked((uint)slot) : uint.MaxValue);
                }
                writer.Write(declaration.Procedures.Read); writer.Write(declaration.Procedures.Write);
                writer.Write(declaration.Procedures.AlternateRead); writer.Write(declaration.Procedures.AlternateWrite);
            }));
            if (reader.ReadUInt32() != methods.Length) throw new InvalidDataException("Native binary mapping omitted a declared member.");
            _nvseBinaryTable = reader.ReadUInt32(); _nvseBinaryRead = reader.ReadUInt32(); _nvseBinaryWrite = reader.ReadUInt32();
            _nvseBinaryAlternateRead = reader.ReadUInt32(); _nvseBinaryAlternateWrite = reader.ReadUInt32(); Finish(reader);
            if (new[] { _nvseBinaryTable, _nvseBinaryRead, _nvseBinaryWrite, _nvseBinaryAlternateRead, _nvseBinaryAlternateWrite }.Any(pointer => pointer == 0))
                throw new InvalidDataException("Native binary callable table/procedure receipt is incomplete.");
            _nvseBinaryDeclaration = declaration with { Methods = Array.AsReadOnly(methods) };
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
        return;

        void Entry(uint address)
        {
            if (address == 0 || address > uint.MaxValue - 7 || entries.Any(previous =>
                (ulong)address < (ulong)previous + 7 && (ulong)previous < (ulong)address + 7))
                throw new InvalidDataException("Binary native entry has a missing/overlapping complete thunk extent.");
            entries.Add(address);
        }
    }

    internal NativeNvseBinaryBinding BindNvseBinaryFile(NativeNvsePlugin plugin, NativeNvseSourceObject contributor,
        NativePluginGuestAllocation image, NativePluginGuestAllocation buffer, NativeNvseBinaryImageAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); VerifyNvseSourceObject(plugin, contributor);
        VerifyGuest(image); VerifyGuest(buffer); ArgumentNullException.ThrowIfNull(authority);
        var declaration = _nvseBinaryDeclaration ?? throw new NotSupportedException("Binary original method declarations are unbound.");
        authority.RequireCurrent(); authority.RequireOriginalCrtFileCurrent(); authority.File.RequireBinaryCurrent();
        if (contributor.Class != NativeNvseSourceClass.ModInfo || image.Access != NativePluginGuestAccess.ReadOnly ||
            buffer.Access != NativePluginGuestAccess.ReadOnly || image.Handle == buffer.Handle ||
            image.Length != declaration.Extent || buffer.Length != authority.File.BinaryCapacity ||
            !StringComparer.OrdinalIgnoreCase.Equals(authority.SourceSha256, contributor.Authority.SourceSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(authority.File.BinarySourceSha256, authority.SourceSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(authority.File.BinaryRuntimeSha256, declaration.RuntimeSha256) ||
            _nvseBinaryFiles.Values.Any(value => value.Image.Handle == image.Handle || value.Buffer.Handle == buffer.Handle ||
                ReferenceEquals(value.Contributor, contributor)))
            throw new InvalidDataException("Binary image/buffer/source leases are incomplete, competing or foreign.");
        var initial = ReadNvseGraphBytes(image);
        var addresses = new NativeNvseBinaryAddresses(image.Address, _nvseBinaryTable, buffer.Address, buffer.Length,
            _nvseBinaryRead, _nvseBinaryWrite, _nvseBinaryAlternateRead, _nvseBinaryAlternateWrite, initial.ToArray());
        var actual = authority.File.InspectBinary();
        var complete = authority.ReadCompleteImage(addresses, actual.Fields, false);
        RequireCompleteBinaryImage(complete, addresses, false, declaration.Extent);
        RequireBinaryFieldProjection(complete, actual.Fields);
        if (!initial.AsSpan().SequenceEqual(complete) || actual.WrittenBuffer.Length > buffer.Length)
            throw new InvalidDataException("Binary constructor publication differs from complete actual native fields.");
        var nativeBuffer = ReadNvseGraphBytes(buffer);
        if (!nativeBuffer.AsSpan(0, actual.WrittenBuffer.Length).SequenceEqual(actual.WrittenBuffer.Span))
            throw new InvalidDataException("Native binary buffer lacks its complete source-written prefix.");
        var lease = authority.RetainSource(); var id = checked(++_nextNvseBinaryFile);
        // Retain the exact owner before native bind so a failed native receipt
        // cannot discard a class/source lease while the child may retain it.
        var binding = new NativeNvseBinaryBinding(Generation, plugin.Module, id, contributor, image, buffer,
            authority, lease, addresses, complete, nativeBuffer);
        _nvseBinaryFiles.Add(id, binding); contributor.Dependents = checked(contributor.Dependents + 1);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseBinaryBind, Payload(writer =>
            {
                writer.Write(plugin.Module); writer.Write(id); writer.Write(contributor.Id); writer.Write(contributor.Address);
                writer.Write(image.Handle); writer.Write(image.Address); writer.Write(image.Length);
                writer.Write(buffer.Handle); writer.Write(buffer.Address); writer.Write(buffer.Length);
            }));
            if (reader.ReadUInt64() != id || reader.ReadUInt32() != image.Address)
                throw new InvalidDataException("Native binary bind returned another receiver lifetime.");
            Finish(reader); return binding;
        }
        catch (Exception error) { throw Fatal(error); }
    }

    private static void RequireCompleteBinaryImage(byte[] image, NativeNvseBinaryAddresses addresses, bool alternate, uint extent)
    {
        if (image is null || image.Length != extent ||
            Word(0) != addresses.Table || Word(8) != (alternate ? addresses.AlternateReadProcedure : addresses.ReadProcedure) ||
            Word(12) != (alternate ? addresses.AlternateWriteProcedure : addresses.WriteProcedure) ||
            Word(0x10) != addresses.Capacity || Word(0x20) != addresses.Buffer || Word(0x24) == 0)
            throw new NotSupportedException("Complete original binary constructor/CRT/table/buffer publication is absent.");
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at, 4));
    }

    private static void RequireBinaryFieldProjection(byte[] image, IReadOnlyList<NativeNvseDataField> fields)
    {
        var coverage = new bool[image.Length];
        foreach (var field in fields)
        {
            if (field is null || field.Offset < 0 || field.Bytes.IsEmpty || field.Offset > image.Length - field.Bytes.Length ||
                string.IsNullOrWhiteSpace(field.Owner) || !image.AsSpan(field.Offset, field.Bytes.Length).SequenceEqual(field.Bytes.Span))
                throw new InvalidDataException("Complete binary image lost an actual selected source scalar/buffer field.");
            for (var at = field.Offset; at < field.Offset + field.Bytes.Length; ++at)
            {
                if (coverage[at]) throw new InvalidDataException("Binary source fields have competing byte ownership.");
                coverage[at] = true;
            }
        }
    }
}
