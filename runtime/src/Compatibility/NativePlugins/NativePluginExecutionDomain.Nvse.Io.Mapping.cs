using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private byte[] DispatchPrivateMapping(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var category = reader.ReadUInt32();
        if (category == 1)
        {
            var operation = (NativePluginMappingOperation)reader.ReadUInt32(); var file = reader.ReadUInt64(); var handle = reader.ReadUInt32();
            var protection = reader.ReadUInt32(); var maximum = reader.ReadUInt64(); var effectiveMaximum = reader.ReadUInt64();
            var hasName = reader.ReadUInt32(); var name = ReadText(reader); Finish(reader);
            if (operation is not (NativePluginMappingOperation.Create or NativePluginMappingOperation.Open) || hasName > 1 ||
                hasName == 0 && name.Length != 0 || operation == NativePluginMappingOperation.Open && hasName == 0)
                throw new InvalidDataException("Mapping request has no exact original name/API declaration.");
            string? physical = null; ulong existing = 0; var writable = false;
            if (hasName != 0)
            {
                if (name.StartsWith("Global\\", StringComparison.Ordinal) ||
                    (name.StartsWith("Local\\", StringComparison.Ordinal) ? name[6..] : name).Contains('\\'))
                    throw new NotSupportedException("Global/cross-process mapping namespaces have no genuine shared campaign owner.");
                // Preserve the declared spelling/case. The hash only chooses
                // its isolated Windows object name, never a plugin success arm.
                if (name == "Local\\")
                    throw new NotSupportedException("The original local mapping name has no object identity.");
                var localName = name.StartsWith("Local\\", StringComparison.Ordinal) ? name[6..] : name;
                physical = name.Length == 0 ? "" : "Local\\OpenNV-" + Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(
                    owner.ModuleRoot + "\0" + Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + localName)));
                if (physical.Length != 0) existing = _mappingObjects.Values.SingleOrDefault(value => value.PhysicalName == physical)?.Id ?? 0;
            }
            if (operation == NativePluginMappingOperation.Create)
            {
                var page = protection & 0xffU;
                if (page is not (2U or 4U or 8U) || (protection & ~0xdc0000ffU) != 0)
                    throw new NotSupportedException("Executable/image/unknown mapping protection has no data-only native owner.");
                if (file != 0)
                {
                    if (!_ioFiles.TryGetValue(file, out var admitted) || admitted.Handle != handle)
                        throw new InvalidDataException("Mapping backing file has no exact source/private native handle lifetime.");
                    writable = admitted.Writable;
                    if (existing == 0 && !writable && page == 4) throw new NotSupportedException("Writable mapping targets an original read-only file capability.");
                }
                else if (existing == 0 && handle != uint.MaxValue) throw new InvalidDataException("Anonymous mapping lacks the actual paging-file handle sentinel.");
                if (file == 0) writable = true;
                if (existing != 0) writable = _mappingObjects[existing].Writable;
                if (maximum != 0 && maximum != effectiveMaximum)
                    throw new InvalidDataException("Native mapping altered its caller-declared maximum extent.");
            }
            else if (file != 0 || handle != 0 || maximum != 0 || effectiveMaximum != 0)
                throw new InvalidDataException("Named mapping Open invented a backing file/extent.");
            var id = checked(++_nextMappingCapability);
            _mappingPlans.Add(id, new(id, operation, existing, file, handle, writable, protection,
                effectiveMaximum, 0, 0, 0, hasName != 0 ? name : null, physical));
            return Payload(writer => { writer.Write(id); writer.Write(existing); WriteText(writer, physical ?? ""); });
        }
        if (category == 2)
        {
            var id = reader.ReadUInt64(); var objectId = reader.ReadUInt64(); var handle = reader.ReadUInt32();
            var error = reader.ReadUInt32(); var success = reader.ReadUInt32(); Finish(reader);
            if (!_mappingPlans.Remove(id, out var plan) || plan.Operation is not (NativePluginMappingOperation.Create or NativePluginMappingOperation.Open) ||
                success > 1 || (handle != 0) != (success != 0) || success == 0 && objectId != 0 ||
                success != 0 && error != 0 && !(plan.Operation == NativePluginMappingOperation.Create && error == 183))
                throw new InvalidDataException("Native mapping result has a repeated/fabricated Windows handle disposition.");
            if (success != 0)
            {
                if (_mappingHandles.Values.Any(value => value.Handle == handle) || _ioFiles.Values.Any(value => value.Handle == handle))
                    throw new InvalidDataException("Mapping handle aliases another current native file/mapping lifetime.");
                var existing = plan.Object;
                if (existing == 0)
                {
                    if (plan.Operation != NativePluginMappingOperation.Create || error == 183 || objectId != id || plan.Maximum == 0)
                        throw new InvalidDataException("Successful mapping lacks a genuine source-owned new extent/object.");
                    _mappingObjects.Add(id, new(id, plan.File, plan.Writable, plan.Maximum, plan.ProtectionOrAccess, plan.Name, plan.PhysicalName));
                }
                else if (objectId != existing || !_mappingObjects.ContainsKey(existing) || plan.Operation == NativePluginMappingOperation.Create && error != 183)
                    throw new InvalidDataException("Existing named mapping did not preserve its original live object/extent.");
                _mappingHandles.Add(id, new(id, handle, objectId, plan.ProtectionOrAccess));
            }
            RecordMapping(parent, plan.Operation, id, objectId, success != 0 ? _mappingObjects[objectId].BackingFile : plan.File, handle, plan.ProtectionOrAccess, 0,
                success != 0 ? _mappingObjects[objectId].Maximum : plan.Maximum, 0, 0, 0, error, success != 0, plan.Name, plan.PhysicalName,
                plan.Operation == NativePluginMappingOperation.Create ? plan.File : null,
                plan.Operation == NativePluginMappingOperation.Create ? plan.Handle : null);
            return Payload(writer => writer.Write(1U));
        }
        if (category == 3)
        {
            var handleId = reader.ReadUInt64(); var handle = reader.ReadUInt32(); var access = reader.ReadUInt32();
            var offset = reader.ReadUInt64(); var bytes = reader.ReadUInt32(); var preferred = reader.ReadUInt32(); Finish(reader);
            if (!_mappingHandles.TryGetValue(handleId, out var mapped) || mapped.Handle != handle || !_mappingObjects.TryGetValue(mapped.Object, out var obj))
                throw new InvalidDataException("View request uses a foreign/retired mapping handle.");
            if ((access & ~0x000f001fU) != 0)
                throw new NotSupportedException("Executable/large-page/unknown view access has no admitted data-view owner.");
            var id = checked(++_nextMappingCapability);
            _mappingPlans.Add(id, new(id, NativePluginMappingOperation.Map, obj.Id, obj.BackingFile, handle,
                obj.Writable, access, obj.Maximum, offset, bytes, preferred, obj.Name, obj.PhysicalName));
            return Payload(writer => writer.Write(id));
        }
        if (category == 4)
        {
            var id = reader.ReadUInt64(); var address = reader.ReadUInt32(); var logical = reader.ReadUInt64(); var region = reader.ReadUInt64();
            var state = reader.ReadUInt32(); var protection = reader.ReadUInt32(); var error = reader.ReadUInt32(); var success = reader.ReadUInt32(); Finish(reader);
            if (!_mappingPlans.Remove(id, out var plan) || plan.Operation != NativePluginMappingOperation.Map ||
                success > 1 || (address != 0) != (success != 0) || success != 0 && error != 0)
                throw new InvalidDataException("Mapped view lost its real native result/address lifetime.");
            if (success != 0)
            {
                if (!_mappingObjects.TryGetValue(plan.Object, out var obj) || plan.Offset >= obj.Maximum ||
                    logical != (plan.Bytes == 0 ? obj.Maximum - plan.Offset : plan.Bytes) || logical == 0 || logical > uint.MaxValue ||
                    logical > obj.Maximum - plan.Offset || region < logical || region > (1UL << 32) - address ||
                    state is not (0x1000U or 0x2000U) || (protection & 0xff) is not (0U or 2U or 4U or 8U) ||
                    !obj.Writable && (protection & 0xff) == 4 || _mappingViews.Values.Any(value => value.Address == address))
                    throw new InvalidDataException("Native view fabricated its logical/page extent, data protection or original read-only access.");
                _mappingViews.Add(id, new(id, address, obj.Id, plan.ProtectionOrAccess, plan.Offset, logical, region, state, protection));
            }
            else if ((logical | region | state | protection) != 0)
                throw new InvalidDataException("Failed mapping invented a native memory extent.");
            RecordMapping(parent, NativePluginMappingOperation.Map, id, plan.Object, plan.File, address,
                plan.ProtectionOrAccess, plan.Offset, logical, region, state, protection, error, success != 0, plan.Name, plan.PhysicalName);
            return Payload(writer => writer.Write(1U));
        }
        if (category == 5)
        {
            var operation = (NativePluginMappingOperation)reader.ReadUInt32(); var id = reader.ReadUInt64(); var address = reader.ReadUInt32();
            var requested = reader.ReadUInt32(); var success = reader.ReadUInt32(); var error = reader.ReadUInt32(); Finish(reader);
            if (success > 1 || success != 0 && error != 0)
                throw new InvalidDataException("Mapping member has an invalid actual Windows result.");
            ulong objectId, file; uint access; ulong offset, logical, region; uint state, protection; string? name, physical;
            if (operation == NativePluginMappingOperation.Close)
            {
                if (!_mappingHandles.TryGetValue(id, out var mapped) || mapped.Handle != address || requested != 0)
                    throw new InvalidDataException("Mapping close repeats or targets a foreign handle.");
                var obj = _mappingObjects[mapped.Object]; objectId = obj.Id; file = obj.BackingFile; access = mapped.RequestFlags;
                offset = 0; logical = obj.Maximum; region = 0; state = 0; protection = obj.Protection; name = obj.Name; physical = obj.PhysicalName;
                if (success != 0) _mappingHandles.Remove(id);
            }
            else if (operation is NativePluginMappingOperation.Unmap or NativePluginMappingOperation.Flush)
            {
                if (!_mappingViews.TryGetValue(id, out var view) || view.Address != address ||
                    operation == NativePluginMappingOperation.Unmap && requested != 0 ||
                    operation == NativePluginMappingOperation.Flush && requested != 0 && requested > view.LogicalBytes)
                    throw new InvalidDataException("View operation repeats or exceeds its exact native mapping lifetime.");
                var obj = _mappingObjects[view.Object]; objectId = obj.Id; file = obj.BackingFile; access = view.Access;
                offset = view.Offset; logical = view.LogicalBytes; region = view.RegionBytes; state = view.State;
                protection = view.Protection; name = obj.Name; physical = obj.PhysicalName;
                if (success != 0 && operation == NativePluginMappingOperation.Unmap) _mappingViews.Remove(id);
            }
            else throw new NotSupportedException("Mapping member has no actual native consumer.");
            RecordMapping(parent, operation, id, objectId, file, address, access, offset, logical, region, state, protection,
                error, success != 0, name, physical);
            if (!_mappingHandles.Values.Any(value => value.Object == objectId) && !_mappingViews.Values.Any(value => value.Object == objectId))
                _mappingObjects.Remove(objectId);
            return Payload(writer => writer.Write(1U));
        }
        throw new NotSupportedException("Native mapping callback has no first-party object/view operation owner.");
    }
    private void RecordMapping(ulong parent, NativePluginMappingOperation operation, ulong capability, ulong obj, ulong file,
        uint address, uint access, ulong offset, ulong logical, ulong region, uint state, uint protection, uint error,
        bool success, string? name, string? physical, ulong? declaredFile = null, uint? declaredHandle = null) =>
        _mappingReceipts.Add(new(checked((ulong)_mappingReceipts.Count + 1), Generation, parent, operation, capability, obj, file,
            address, access, offset, logical, region, state, protection, error, success, name, physical, declaredFile, declaredHandle));
    private void RequirePrivateMappingsRetired()
    {
        if (_mappingPlans.Count != 0 || _mappingHandles.Count != 0 || _mappingViews.Count != 0 || _mappingObjects.Count != 0)
            throw new InvalidDataException("Original module retirement retains real mapping plans/handles/views/backing objects.");
    }
    internal void RequirePrivateCryptoMappingSaveOwned()
    {
        VerifyOwner();
        if (_mappingReceipts.Count != 0 || _cryptoReceipts.Count != 0 || _mappingPlans.Count != 0)
            throw new NotSupportedException("Original native mapped pages, CNG hash state and current/cold pointer graph have no unified campaign continuation owner.");
    }
    private void ClearPrivateMappings()
    {
        if (!ChildExited) throw new InvalidOperationException("Mapping source/view cleanup requires verified exact child closure.");
        _mappingPlans.Clear(); _mappingHandles.Clear(); _mappingViews.Clear(); _mappingObjects.Clear();
        // Retain complete failed/successful operation evidence after closure.
    }
}
