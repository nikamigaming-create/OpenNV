using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceEnableParent(FalloutFormKey Reference, bool Opposite, bool PopIn)
{
    internal static FalloutReferenceEnableParent? Read(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "XESP").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Length != 1 || fields[0].Data.Length != 8)
            throw new InvalidDataException($"Reference {record.FormKey} has invalid XESP extent/count.");
        var data = fields[0].Data.Span;
        var parent = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
        var flags = data[4]; // Three trailing bytes are unused, not flag bits.
        if ((flags & ~3u) != 0) throw new NotSupportedException($"Reference {record.FormKey} has unbound XESP flags {flags:x8}.");
        return parent is { } key ? new(key, (flags & 1) != 0, (flags & 2) != 0) : null;
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly FalloutFormKey _enginePlayer = records.RuntimeFormKey(0x14);
    internal bool IsEnabled(FalloutFormKey reference)
    {
        HashSet<FalloutFormKey>? visiting = null;
        var opposite = false;
        while (reference != _enginePlayer)
        {
            var instance = Get(reference);
            if (instance.Deleted || instance.Taken) return opposite;
            if (instance.EnableParent is not { } parent) return instance.Enabled != opposite;
            visiting ??= [];
            if (!visiting.Add(reference)) throw new InvalidDataException($"Enable-parent cycle reaches {reference}.");
            opposite ^= parent.Opposite;
            reference = parent.Reference;
        }
        return !opposite;
    }

    // Script commands enqueue work; GetDisabled observes the applied state until
    // the next world update. In particular, a fading disable remains enabled
    // (including collision) until its loaded fade node reaches zero opacity.
    internal bool SetEnabled(FalloutFormKey reference, bool enabled, bool fade = false)
    {
        if (records.RuntimeFormId(reference) == 0x14) return false;
        var instance = Get(reference);
        if (instance.Deleted) return false;
        _ = IsEnabled(reference); // Reject invalid source chains before mutation.
        if (instance.EnableParent is not null) return false;
        if (enabled)
        {
            if (!fade) instance.NoFade = true;
            var cancelled = instance.EnableRequest is { Enabled: false };
            instance.EnableRequest = instance.Enabled ? null : new(true, fade);
            return !instance.Enabled || cancelled;
        }
        if (!instance.Enabled) return false;
        if (instance.EnableRequest == new FalloutReferenceEnableRequest(false, fade)) return false;
        instance.EnableRequest = new(false, fade);
        return true;
    }

    internal void AdvanceEnableChanges(double seconds, FalloutReferenceFadeSettings settings,
        Func<FalloutFormKey, bool> hasFadeNode)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        settings.Validate();
        var pending = _instances.Values.Where(instance => instance.EnableRequest is not null).ToArray();
        void Apply(FalloutReferenceInstance instance, bool enabled, bool fade)
        {
            bool Affected(FalloutReferenceInstance value)
            {
                var visited = new HashSet<FalloutFormKey>();
                while (visited.Add(value.Reference))
                {
                    if (value == instance) return true;
                    if (value.EnableParent is not { } parent || records.RuntimeFormId(parent.Reference) == 0x14) return false;
                    value = Get(parent.Reference);
                }
                return false; // An unrelated invalid chain is not this command's target.
            }
            var affected = _instances.Values.ToArray().Where(Affected).ToArray();
            var before = affected.ToDictionary(value => value.Reference, value => IsEnabled(value.Reference));
            instance.Enabled = enabled;
            instance.EnableRequest = null;
            foreach (var value in affected)
            {
                if (before[value.Reference] == IsEnabled(value.Reference)) continue;
                var shouldFade = value == instance ? fade : value.EnableParent is { PopIn: false };
                value.Opacity = IsEnabled(value.Reference) && shouldFade && !value.NoFade && hasFadeNode(value.Reference) ? 0 : 1;
            }
        }
        foreach (var instance in pending.Where(instance => instance.EnableRequest is { Enabled: true }))
            Apply(instance, true, instance.EnableRequest!.Fade);
        foreach (var instance in pending.Where(instance => instance.EnableRequest is { Enabled: false, Fade: false }))
            Apply(instance, false, false);
        foreach (var instance in pending.Where(instance => instance.EnableRequest is { Enabled: false, Fade: true }))
            if (instance.Opacity == 0 || !hasFadeNode(instance.Reference)) Apply(instance, false, false);

        // Use Float32 storage and the engine's per-update opacity step ceiling.
        // A long frame cannot jump directly through a partially completed fade.
        var fadeIn = Math.Min((float)seconds / settings.InSeconds, .1f);
        var fadeOut = Math.Min((float)seconds / settings.OutSeconds, .1f);
        foreach (var instance in _instances.Values)
        {
            if (instance.Opacity == 1 && instance.EnableRequest is not { Enabled: false, Fade: true }) continue;
            if (!IsEnabled(instance.Reference)) continue;
            if (instance.EnableRequest is { Enabled: false, Fade: true })
                instance.Opacity = Math.Max(instance.Opacity - fadeOut, 0);
            else if (instance.Opacity < 1)
                instance.Opacity = Math.Min(instance.Opacity + fadeIn, 1);
        }
    }
}

internal sealed record FalloutReferenceEnableRequest(bool Enabled, bool Fade);

internal sealed record FalloutReferenceFadeSettings(float InSeconds, float OutSeconds)
{
    internal static FalloutReferenceFadeSettings Read(FalloutInstallationSettings settings) =>
        new(settings.Number("LOD", "fFadeInTime"), settings.Number("LOD", "fFadeOutTime"));

    internal void Validate()
    {
        if (!float.IsFinite(InSeconds) || !float.IsFinite(OutSeconds) || InSeconds <= 0 || OutSeconds <= 0)
            throw new InvalidDataException("Reference fade times must be positive finite source settings.");
    }
}
