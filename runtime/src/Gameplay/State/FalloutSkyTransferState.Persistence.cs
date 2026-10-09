using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    internal const string Schema = "opennv-source-Sky-transfer/v4";
    internal FalloutSkyTransferSnapshot Capture()
    {
        RequireLiving();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        if (_process == Guid.Empty) throw new NotSupportedException("Source Sky has no actual campaign process association.");
        if (_clouds is not null) _cloudBinding = _cloudBinding with { Source = _clouds.Capture() };
        if (_precipitation is not null) _precipitationBinding = _precipitationBinding with { Source = _precipitation.Capture() };
        var result = new FalloutSkyTransferSnapshot(Schema, Source, _stack, Identity, _process, _changed, Flags, Mode,
            HourBits, BlendBits, TransitionBits, Climate, TimeCaches, CurrentWeather, PreviousWeather, OverrideWeather, TargetWeather,
            _cloudBinding, _precipitationBinding, OrderedInstances(),
            _last, _handoff, _modeFailure, _clockFailure, _retired, _retirementFailure, CaptureStandaloneBaseTimes(), _moonHourStore);
        Validate(result, Source, _stack, _records); return result;
    }
    internal static void Validate(FalloutSkyTransferSnapshot saved, FalloutSkyTransferDeclaration source,
        string stack, FalloutPluginStack records)
    {
        source.Validate(); saved.Source.Validate();
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.CapturedSky == Guid.Empty ||
            saved.CapturedProcess == Guid.Empty || saved.Changed < 1 || saved.TimeCaches is null || saved.Clouds is null || saved.Precipitation is null ||
            saved.Images is null || saved.Retired || saved.RetirementFailure is not null ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.HourBits)) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.BlendBits)) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(saved.TransitionBits)))
            throw new InvalidDataException("Saved Sky omitted current source/selection/process/field ownership.");
        if (saved.Climate is { } climate) _ = FalloutClimateLighting.Read(records.GetEffective(climate));
        // This packet owns the constructor and reset writes. Other weather
        // field writers remain separate; neither a snapshot nor a renderer
        // observation can introduce them as admitted source execution.
        if (saved.Mode != 4 ||
            saved.BlendBits != BitConverter.SingleToUInt32Bits(1f) || saved.TransitionBits != 0 ||
            saved.CurrentWeather is not null || saved.PreviousWeather is not null || saved.OverrideWeather is not null || saved.TargetWeather is not null)
            throw new InvalidDataException("Saved Sky introduced an unowned constructor/reset field writer.");
        foreach (var bits in new[] { saved.TimeCaches.SunriseStart, saved.TimeCaches.SunriseEnd, saved.TimeCaches.SunsetStart, saved.TimeCaches.SunsetEnd })
            if (!float.IsFinite(BitConverter.UInt32BitsToSingle(bits))) throw new InvalidDataException("Saved Sky time cache is not an actual finite source cell.");
        ValidateMoonHourStore(saved, source, records);
        ValidateStandaloneTimes(saved, records);
        if (saved.Images.Count is not (0 or 4) || saved.Images.Select(row => row.Identity).Distinct().Count() != saved.Images.Count ||
            saved.Images.Select(row => row.Slot).Distinct().Count() != saved.Images.Count ||
            saved.Images.Select(row => row.ManagerOrdinal).Distinct().Count() != saved.Images.Count)
            throw new InvalidDataException("Saved Sky omitted/aliased one of its four source manager instances.");
        foreach (var instance in saved.Images)
        {
            FalloutSourceSkyImageProgram.Validate(instance, source, records);
            if (instance.Sky != saved.CapturedSky || instance.ManagerOrdinal != Array.IndexOf(ManagerOrder, instance.Slot) ||
                instance.Changed > saved.Changed) throw new InvalidDataException("Saved Sky image has a foreign field or source registration order.");
        }
        ValidatePrecipitationBinding(saved.Precipitation, source);
        foreach (var child in new[] { saved.Clouds, saved.Precipitation })
        {
            if (child.Disposition == FalloutSkyChildDisposition.ConstructorNull && (child.Source is not null || child.Failure is not null) ||
                child.Disposition == FalloutSkyChildDisposition.Published && (child.Source is null || child.Failure is not null) ||
                child.Disposition == FalloutSkyChildDisposition.Unowned && string.IsNullOrWhiteSpace(child.Failure))
                throw new InvalidDataException("Saved Sky substituted a missing factory for a known-null constructor field.");
        }
        if (saved.LastCall is { } call && (call.Context is null || !call.Returned || call.FailureType is not null || call.Error is not null ||
            call.Context.Sky != saved.CapturedSky || call.Context.Process != saved.CapturedProcess || call.Context.SourceContract != source.Contract ||
            call.Context.Call == Guid.Empty || call.Context.Ordinal < 1 || call.Context.Ordinal >= call.Changed || call.Changed > saved.Changed ||
            call.EnteredChild is not null || !call.Completed.SequenceEqual(ResetSteps(source))))
            throw new InvalidDataException("Saved Sky pretended an entered or partial reset had returned.");
        if (saved.LastCall is { } originalCall) ValidateSelectedResetOrigin(originalCall.Context);
        if (saved.Handoff is { } handoff && (handoff.ImageInstances is null ||
            handoff.CurrentSky != saved.CapturedSky || handoff.CurrentProcess != saved.CapturedProcess ||
            handoff.CapturedSky == Guid.Empty || handoff.CapturedProcess == Guid.Empty ||
            handoff.CapturedSky == handoff.CurrentSky || handoff.CapturedProcess == handoff.CurrentProcess ||
            !handoff.ImageInstances.Values.ToHashSet().SetEquals(saved.Images.Select(instance => instance.Identity)) ||
            handoff.ImageInstances.Keys.Any(identity => identity == Guid.Empty) ||
            handoff.ImageInstances.Values.Distinct().Count() != handoff.ImageInstances.Count ||
            handoff.ImageInstances.Any(pair => pair.Key == pair.Value)))
            throw new InvalidDataException("Saved Sky promoted a previous native/process instance into the current epoch.");
    }
    internal void Restore(FalloutSkyTransferSnapshot saved, Guid process)
    {
        RequireLiving();
        if (_process != Guid.Empty || _instances.Count != 0 || _last is not null || process == Guid.Empty || process == saved.CapturedProcess)
            throw new InvalidOperationException("Sky cold continuation needs a genuinely new current process and untouched factory.");
        Validate(saved, Source, _stack, _records); _process = process;
        Flags = saved.Flags; Mode = saved.Mode; HourBits = saved.HourBits; BlendBits = saved.BlendBits;
        TransitionBits = saved.TransitionBits; Climate = saved.Climate; TimeCaches = saved.TimeCaches;
        RestoreStandaloneTimes(saved);
        CurrentWeather = saved.CurrentWeather; PreviousWeather = saved.PreviousWeather;
        OverrideWeather = saved.OverrideWeather; TargetWeather = saved.TargetWeather;
        _changed = saved.Changed; _modeFailure = saved.ModeFailure; _clockFailure = saved.ClockFailure;
        RestoreMoonHourStore(saved, process);
        _cloudBinding = saved.Clouds; _precipitationBinding = saved.Precipitation;
        var map = new Dictionary<Guid, Guid>();
        foreach (var slot in ManagerOrder)
        {
            var previous = saved.Images.SingleOrDefault(row => row.Slot == slot);
            if (previous is null) continue;
            var identity = Guid.NewGuid(); map.Add(previous.Identity, identity);
            var current = previous with { Identity = identity, Sky = Identity, Changed = Next() };
            _images.RegisterSourceSkyInstance(this, identity, current.ManagerOrdinal!.Value); _instances.Add(slot, current);
        }
        _handoff = new(saved.CapturedSky, Identity, saved.CapturedProcess, process, map);
        // A previous return remains evidence in the saved receipt. It is not
        // a live invocation token and cannot authorize a new transfer call.
        _last = null; Next();
    }
}
