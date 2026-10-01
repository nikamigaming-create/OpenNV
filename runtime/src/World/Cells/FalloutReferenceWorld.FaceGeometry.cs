using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutFaceGeometryControls? _faceControls = faceControls;
    private FalloutFaceGeometryControls FaceControls => _faceControls ??= FalloutFaceGeometryControls.Read(
        RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Face matching has no owned content source."));

    private FalloutPluginRecord ActorModel(FalloutFormKey reference)
    {
        var source = NpcAppearanceSource(reference).Base;
        return FalloutActorTemplateOwner.Resolve(records, source, 64,
            reference == records.RuntimeFormKey(0x14) ? null : Actor(reference).Templates);
    }

    internal FalloutNpcFaceGen ActorFace(FalloutFormKey reference)
    {
        var model = ActorModel(reference);
        if (reference == records.RuntimeFormKey(0x14) && _playerAppearance?.Invoke().FaceGen is { } player) return player;
        var face = FalloutNpcAppearanceResolver.ReadFaceGen(model, model.ReadSubrecords().ToArray());
        var changed = _actorOverrides.GetValueOrDefault(NpcAppearanceSource(reference).Base.FormKey)?.FaceGeometry;
        return changed is null ? face : face with
        {
            SymmetricGeometry = changed.SymmetricGeometry.ToArray(),
            AsymmetricGeometry = changed.AsymmetricGeometry.ToArray(),
        };
    }

    internal void MatchFaceGeometry(FalloutFormKey target, FalloutFormKey source, int percentage)
    {
        var targetBase = NpcAppearanceSource(target).Base;
        var sourceTraits = NpcAppearanceSource(source).Traits;
        if (target == records.RuntimeFormKey(0x14))
            throw new NotSupportedException("Scripted player face changes require the shared character-identity transition owner.");
        var female = source == records.RuntimeFormKey(0x14) ?
            (_playerAppearance ?? throw new NotSupportedException("Face matching has no current player appearance."))().Female ??
                throw new InvalidDataException("Current player appearance has no sex.") :
            (BinaryPrimitives.ReadUInt32LittleEndian(sourceTraits.ReadSubrecords().Single(row => row.Signature == "ACBS").Data.Span) & 1) != 0;
        var presets = FalloutNpcFacePresets.Resolve(records, records.RuntimeFormKey(7), ActorRace(source), female,
            npc => _actorOverrides.GetValueOrDefault(npc.FormKey)?.Race?.Form ??
                npc.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(npc.ReadSubrecords().Single(row => row.Signature == "RNAM").Data.Span)));
        var preset = FalloutNpcFacePresets.First(presets);
        var presetModel = FalloutActorTemplateOwner.Resolve(records, preset, 64);
        var presetFace = FalloutNpcAppearanceResolver.ReadFaceGen(presetModel, presetModel.ReadSubrecords().ToArray());
        if (_actorOverrides.GetValueOrDefault(preset.FormKey)?.FaceGeometry is { } changedPreset)
            presetFace = presetFace with { SymmetricGeometry = changedPreset.SymmetricGeometry, AsymmetricGeometry = changedPreset.AsymmetricGeometry };
        var race = RequireRace(ActorRace(target)); var controls = FaceControls;
        var next = FalloutFaceGeometryMatch.Apply(ActorFace(target), ActorFace(source), presetFace,
            FalloutNpcAppearanceResolver.ReadRaceFaceGen(race, false), controls.Model, percentage);
        var model = ActorModel(target);
        var entry = new FalloutActorFaceGeometryOverride(model.FormKey, RecordHash(model), race.FormKey, RecordHash(race),
            controls.Sha256, next.SymmetricGeometry, next.AsymmetricGeometry);
        _actorOverrides[targetBase.FormKey] = Overrides(targetBase.FormKey) with { FaceGeometry = entry };
        _appearanceRevisions[targetBase.FormKey] = ++_appearanceRevision;
    }

    private void ValidateFaceGeometry(FalloutActorOverrides snapshot, FalloutActorFaceGeometryOverride face)
    {
        var target = ActorOverrideSource(snapshot.Target);
        var model = records.GetEffective(face.ModelOwner);
        if (target.Signature != "NPC_" || snapshot.Target == records.RuntimeFormKey(7) || model.Signature != "NPC_" ||
            FalloutActorTemplateOwner.Resolve(records, target, 64).FormKey != face.ModelOwner ||
            !RecordHash(model).Equals(face.ModelSha256, StringComparison.OrdinalIgnoreCase) ||
            !RecordHash(RequireRace(face.Race)).Equals(face.RaceSha256, StringComparison.OrdinalIgnoreCase) ||
            !FaceControls.Sha256.Equals(face.ControlsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved face geometry differs from its winning NPC/RACE/CTL source.");
        foreach (var (bytes, count) in new[] { (face.SymmetricGeometry, FaceControls.Model.BasisCounts[0]),
            (face.AsymmetricGeometry, FaceControls.Model.BasisCounts[1]) })
        {
            if (bytes is null || bytes.Length != count * 4L) throw new InvalidDataException("Saved face geometry has invalid coefficient extents.");
            for (var offset = 0; offset < bytes.Length; offset += 4)
                if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset))))
                    throw new InvalidDataException("Saved face geometry has a non-finite coefficient.");
        }
    }
}
