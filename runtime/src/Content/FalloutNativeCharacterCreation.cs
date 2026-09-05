using System.Buffers.Binary;
using OpenNV.Runtime.Formats.FaceGen;

namespace OpenNV.Runtime.Content;

/// <summary>Authoritative edits to the player's winning appearance graph.</summary>
internal sealed class FalloutNativeCharacterCreation
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutNativeRaceSexContract _contract;
    private readonly FalloutCtlFile _model;
    private readonly FalloutInstallationSettings _settings;
    private FalloutNpcAppearance? _appearance;
    private int _appearanceRevision = -1;
    internal FalloutNativeRaceSexSelection Selection { get; private set; }
    internal FalloutFaceControlTable Controls { get; }
    internal IReadOnlyList<string> Headers { get; }
    internal int PresetIndex { get; private set; } = 1;
    internal int Revision { get; private set; }
    internal int AgeValue { get; private set; } = 1;
    internal int HairPresetIndex { get; private set; }
    private bool _customHair = true;
    internal string HairPresetLabel => FalloutGameSettingStrings.Read(_records, _customHair ? "sRSMCustom" : $"sHairColor{HairPresetIndex}");
    internal FalloutNativeRaceSexRace Race => _contract.Races.Single(row => row.RuntimeFormId == Selection.RaceRuntimeFormId);
    internal IReadOnlyList<FalloutNativeRaceSexRace> Races => _contract.Races.OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    internal FalloutNativeCharacterCreation(FalloutPluginStack records, FalloutNativeRaceSexContract contract,
        FalloutNativeRaceSexSelection current, FalloutInstallationSettings settings)
    {
        _records = records; _contract = contract; _settings = settings;
        FalloutNativeRaceSexResolver.Validate(contract, current);
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Character creation has no owned content source.");
        var executable = Path.Combine(Path.GetDirectoryName(content.ContentRoot)!,
            content.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe");
        Controls = FalloutExecutableStringTable.ReadFaceControls(executable);
        Headers = FalloutExecutableStringTable.ReadCreationHeaders(executable);
        if (!content.TryRead("facegen/si.ctl", null, out var ctl, out _)) throw new FileNotFoundException("facegen/si.ctl");
        _model = FalloutCtlFile.Read(ctl);
        Selection = current;
        if (current.Face is null)
        {
            var source = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [], appearanceState: State(current));
            Selection = current with { Face = Face(source, records.GetEffective(source.ModelOwner)) };
        }
        Validate();
        foreach (var row in Controls.Controls) _ = Axis(row);
    }

    internal string Header(int page) => FalloutGameSettingStrings.Read(_records, Headers[page]);
    internal FalloutNpcAppearance Appearance()
    {
        if (_appearanceRevision != Revision)
        {
            _appearance = FalloutNpcAppearanceResolver.Resolve(_records, _contract.Player, equippedArmor: [], appearanceState: State(Selection));
            _appearanceRevision = Revision;
        }
        return _appearance!;
    }

    internal FalloutActorAppearanceState State(FalloutNativeRaceSexSelection selection)
    {
        var face = selection.Face;
        return new(selection.Female, _records.RuntimeFormKey(selection.RaceRuntimeFormId),
            _records.RuntimeFormKey(selection.HairRuntimeFormId), _records.RuntimeFormKey(selection.EyesRuntimeFormId),
            face is null ? null : new(_contract.Player, face.SymmetricGeometry, face.AsymmetricGeometry, face.SymmetricTexture),
            face?.HairColor, face?.HairLength, face?.HeadParts.Select(_records.RuntimeFormKey).ToArray());
    }

    internal IReadOnlyList<FalloutPluginRecord> Presets()
    {
        var candidates = _records.EffectiveRecords("NPC_").Where(row => row.FormKey != _contract.Player &&
            Linked(row, "RNAM") == _records.RuntimeFormKey(Selection.RaceRuntimeFormId) &&
            ((Flags(row) & 1) != 0) == Selection.Female).ToArray();
        var marked = candidates.Where(row => (Flags(row) & 4) != 0).ToArray();
        return (marked.Length == 0 ? candidates : marked).OrderBy(row => Text(row, "FULL"), StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal void ChangeIdentity(uint race, bool female)
    {
        if (race == Selection.RaceRuntimeFormId && female == Selection.Female) return;
        var before = Selection;
        try
        {
            Selection = _contract.Select(race, female, before);
            var presets = Presets();
            if (presets.Count == 0) throw new NotSupportedException("Selected race and sex have no native character presets.");
            ApplyPreset(1);
        }
        catch { Selection = before; throw; }
    }

    internal void ApplyPreset(int index)
    {
        var presets = Presets();
        if (index < 1 || index > presets.Count) throw new InvalidDataException("Face preset index is outside the source list.");
        var preset = presets[index - 1];
        var appearance = FalloutNpcAppearanceResolver.Resolve(_records, preset.FormKey, equippedArmor: []);
        var hair = Race.HairFor(Selection.Female).Single(row => row.RuntimeFormId == _records.RuntimeFormId(appearance.Hair!.Value));
        var eye = Race.EyesFor(Selection.Female).Single(row => row.RuntimeFormId == _records.RuntimeFormId(appearance.Eyes!.Value));
        Commit(Selection with
        {
            HairRuntimeFormId = hair.RuntimeFormId,
            HairEditorId = hair.EditorId,
            EyesRuntimeFormId = eye.RuntimeFormId,
            EyesEditorId = eye.EditorId,
            Face = Face(appearance, _records.GetEffective(appearance.ModelOwner))
        });
        PresetIndex = index;
        HairPresetIndex = 0; _customHair = true;
    }

    internal void SetHair(FalloutNativeRaceSexPart part) => Commit(Selection with { HairRuntimeFormId = part.RuntimeFormId, HairEditorId = part.EditorId });
    internal void SetEyes(FalloutNativeRaceSexPart part) => Commit(Selection with { EyesRuntimeFormId = part.RuntimeFormId, EyesEditorId = part.EditorId });
    internal void SetHairComponent(int component, int value)
    {
        if (component is < 0 or > 2 || value is < 0 or > 255) throw new InvalidDataException("Hair colour component is outside its source domain.");
        var color = Selection.Face!.HairColor.ToArray(); color[component] = (byte)value;
        Commit(Selection with { Face = Selection.Face with { HairColor = color } });
        _customHair = true;
    }

    internal void SetHairPreset(int index)
    {
        if (index is < 0 or > 15) throw new InvalidDataException("Hair preset is outside the native palette interval.");
        var value = FalloutGameSettingIntegers.Read(_records, $"iHairColor{index:00}");
        var color = Selection.Face!.HairColor.ToArray();
        for (var component = 0; component < 3; component++) color[component] = (byte)(value >> (component * 8));
        Commit(Selection with { Face = Selection.Face with { HairColor = color } });
        HairPresetIndex = index; _customHair = false;
    }

    internal void SetAge(int value)
    {
        if (value is < 1 or > 10) throw new InvalidDataException("Creation age slider is outside the native interval.");
        var source = Appearance();
        FalloutCtlAffineAxis[] Axes(int domain) => [_model.AffineAxes[0][0][domain], _model.AffineAxes[0][1][domain]];
        var geometryAxes = Axes(0); var textureAxes = Axes(1);
        var geometryAge = FalloutFaceGenControls.Attribute(source.FaceGen.SymmetricGeometry, source.RaceFaceGen.SymmetricGeometry, geometryAxes[0]);
        var textureAge = FalloutFaceGenControls.Attribute(source.FaceGen.SymmetricTexture, source.RaceFaceGen.SymmetricTexture, textureAxes[0]);
        // Native menu mapping uses Float64 operands promoted from Float32 and
        // truncates to integer years before clamping; rounding changes the face.
        var years = Math.Clamp((int)(value * (double)5.55f + (double)9.45f), 15, 65);
        var textureYears = Math.Clamp(years + textureAge - geometryAge, 15, 65);
        Commit(Selection with
        {
            Face = Selection.Face! with
            {
                SymmetricGeometry = FalloutFaceGenControls.SetAttribute(source.FaceGen.SymmetricGeometry, source.RaceFaceGen.SymmetricGeometry, geometryAxes, 0, years),
                SymmetricTexture = FalloutFaceGenControls.SetAttribute(source.FaceGen.SymmetricTexture, source.RaceFaceGen.SymmetricTexture, textureAxes, 0, textureYears),
            }
        });
        AgeValue = value;
    }

    internal IReadOnlyList<FalloutNativeRaceSexPart> FacialHair() => _records.EffectiveRecords("HDPT")
        .Select(row => (Record: row, Flags: Byte(row, "DATA"))).Where(row => (row.Flags & 1) != 0 &&
            (Selection.Female ? (row.Flags & 2) != 0 : (row.Flags & 2) == 0))
        .Select(row => new FalloutNativeRaceSexPart(_records.RuntimeFormId(row.Record.FormKey), Text(row.Record, "EDID"), Text(row.Record, "FULL"), !Selection.Female, Selection.Female))
        .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    internal void ToggleHeadPart(FalloutNativeRaceSexPart part)
    {
        if (!FacialHair().Any(row => row.RuntimeFormId == part.RuntimeFormId)) throw new InvalidDataException("Head part is outside the playable list.");
        var parts = Selection.Face!.HeadParts;
        Commit(Selection with
        {
            Face = Selection.Face with
            {
                HeadParts = parts.Contains(part.RuntimeFormId)
            ? [] : [part.RuntimeFormId]
            }
        });
    }

    internal (int Minimum, int Maximum) Limits(FalloutFaceControlBinding row)
    {
        var minimum = row.Minimum.Resolve(_settings.Number); var maximum = row.Maximum.Resolve(_settings.Number);
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum >= maximum) throw new InvalidDataException("Face control range is invalid.");
        return (checked((int)(minimum * 10)), checked((int)(maximum * 10)));
    }
    internal int Value(FalloutFaceControlBinding row)
    {
        var source = Appearance(); var bounds = Limits(row);
        var value = FalloutFaceGenControls.Project(Coefficients(source.FaceGen, row.Group), Coefficients(source.RaceFaceGen, row.Group), Axis(row));
        return (int)Math.Clamp(value * 10, bounds.Minimum, bounds.Maximum);
    }
    internal void SetControl(FalloutFaceControlBinding row, int value)
    {
        var bounds = Limits(row);
        if (value < bounds.Minimum || value > bounds.Maximum) throw new InvalidDataException("Face control exceeds its owned limits.");
        var source = Appearance();
        var bytes = FalloutFaceGenControls.SetControl(Coefficients(source.FaceGen, row.Group), Coefficients(source.RaceFaceGen, row.Group), Axis(row), value / 10f);
        Commit(Selection with
        {
            Face = row.Group switch
            {
                0 => Selection.Face! with { SymmetricGeometry = bytes },
                2 => Selection.Face! with { SymmetricTexture = bytes },
                _ => throw new NotSupportedException("Creation control domain is unbound."),
            }
        });
    }

    private float[] Axis(FalloutFaceControlBinding row) => row.Group < _model.Controls.Length && row.Index < _model.Controls[row.Group].Count
        ? _model.Controls[row.Group][row.Index].Axis : throw new InvalidDataException("Creation declaration is outside the owned CTL.");
    private static byte[] Coefficients(FalloutNpcFaceGen face, int group) => group switch { 0 => face.SymmetricGeometry, 2 => face.SymmetricTexture, _ => throw new NotSupportedException("Face domain is unbound.") };
    private void Commit(FalloutNativeRaceSexSelection next)
    {
        var before = Selection; Selection = next;
        try { Validate(); } catch { Selection = before; throw; }
        Revision++;
    }
    private void Validate()
    {
        FalloutNativeRaceSexResolver.Validate(_contract, Selection);
        var face = Selection.Face ?? throw new InvalidDataException("Creation has no authoritative face state.");
        if (face.SymmetricGeometry.Length != _model.BasisCounts[0] * 4 || face.AsymmetricGeometry.Length != _model.BasisCounts[1] * 4 || face.SymmetricTexture.Length != _model.BasisCounts[2] * 4)
            throw new InvalidDataException("Player appearance is incompatible with the owned CTL dimensions.");
        foreach (var id in face.HeadParts)
            if (_records.GetEffective(_records.RuntimeFormKey(id)).Signature != "HDPT") throw new InvalidDataException("Player head part is not an owned HDPT.");
    }
    private FalloutNativeFaceState Face(FalloutNpcAppearance source, FalloutPluginRecord owner) => new(
        source.FaceGen.SymmetricGeometry.ToArray(), source.FaceGen.AsymmetricGeometry.ToArray(), source.FaceGen.SymmetricTexture.ToArray(),
        source.HairColorBytes.ToArray(), source.HairLengthBytes.ToArray(), owner.ReadSubrecords().Where(row => row.Signature == "PNAM")
            .Select(row => _records.RuntimeFormId(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span)))).ToArray());
    private static FalloutFormKey Linked(FalloutPluginRecord row, string signature) => row.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(Field(row, signature).Span));
    private static uint Flags(FalloutPluginRecord row) => BinaryPrimitives.ReadUInt32LittleEndian(Field(row, "ACBS").Span);
    private static byte Byte(FalloutPluginRecord row, string signature) => Field(row, signature).Length == 1 ? Field(row, signature).Span[0] : throw new InvalidDataException("Head-part flags have an unsupported extent.");
    private static string Text(FalloutPluginRecord row, string signature) => FalloutDialogueTopic.Text(Field(row, signature).Span);
    private static ReadOnlyMemory<byte> Field(FalloutPluginRecord row, string signature) => row.ReadSubrecords().Single(value => value.Signature == signature).Data;
}
