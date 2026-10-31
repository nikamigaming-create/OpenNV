using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal enum FalloutIdleGroupKeyKind { TimedAction, GroupMarker, NonmatchingGroupMarker, TransitionMetadata }

// Group compilation is immutable source preparation, independent of playback.
// A marker belonging to another group is not a universal ignored text key.
internal sealed class FalloutIdleGroupTextKeys
{
    private readonly IReadOnlyList<IReadOnlyList<FalloutIdleGroupKeyKind>> _kinds;
    internal string Group { get; }
    internal FalloutIdleTransition.Metadata Blend { get; }
    internal bool HasSynchronization { get; }

    private FalloutIdleGroupTextKeys(string group, IReadOnlyList<IReadOnlyList<FalloutIdleGroupKeyKind>> kinds,
        FalloutIdleTransition.Metadata blend, bool synchronization)
    { Group = group; _kinds = kinds; Blend = blend; HasSynchronization = synchronization; }

    internal FalloutIdleGroupKeyKind Kind(int key, int segment) => _kinds[key][segment];

    internal static FalloutIdleGroupTextKeys Read(string sequenceName, IReadOnlyList<FalloutNifTextKey> keys)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);
        ArgumentNullException.ThrowIfNull(keys);
        var separator = sequenceName.IndexOf('_');
        var group = separator < 0 ? sequenceName : sequenceName[..separator];
        var specialIdle = group.All(value => value <= 127) && group.Equals("SpecialIdle", StringComparison.OrdinalIgnoreCase);
        byte blend = 0, blendIn = 0, blendOut = 0;
        var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var synchronization = false;
        var expected = 0;
        var kinds = new List<IReadOnlyList<FalloutIdleGroupKeyKind>>(keys.Count);
        foreach (var key in keys)
        {
            var segments = new List<FalloutIdleGroupKeyKind>();
            foreach (var text in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
            {
                var kind = FalloutIdleGroupKeyKind.TimedAction;
                if (text.Any(value => value > 127))
                {
                    segments.Add(kind);
                    continue;
                }
                if (text.StartsWith("m:", StringComparison.OrdinalIgnoreCase)) synchronization = true;
                var colon = text.IndexOf(':');
                var field = colon < 0 ? "" : text[..colon];
                if (field.Equals("Blend", StringComparison.OrdinalIgnoreCase) ||
                    field.Equals("BlendIn", StringComparison.OrdinalIgnoreCase) || field.Equals("BlendOut", StringComparison.OrdinalIgnoreCase))
                {
                    if (!metadata.Add(field) || !byte.TryParse(text.AsSpan(colon + 1).Trim(),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
                        throw new NotSupportedException("Idle blend metadata requires one finite byte declaration per field.");
                    if (field.Equals("Blend", StringComparison.OrdinalIgnoreCase)) blend = count;
                    else if (field.Equals("BlendIn", StringComparison.OrdinalIgnoreCase)) blendIn = count;
                    else blendOut = count;
                    kind = FalloutIdleGroupKeyKind.TransitionMetadata;
                }
                else if (specialIdle && expected < 4)
                {
                    if (FalloutIdleGroupTiming.TryConsume(text, ref expected, out _))
                        kind = FalloutIdleGroupKeyKind.GroupMarker;
                    else if (expected is 1 or 2 && text.Equals("Attach", StringComparison.OrdinalIgnoreCase))
                    {
                        // Attach is an authored marker in other group classes.
                        // Here the valid intermediate SpecialIdle slot does not
                        // match it and compiles no timing or runtime action.
                        kind = FalloutIdleGroupKeyKind.NonmatchingGroupMarker;
                    }
                }
                segments.Add(kind);
            }
            kinds.Add(segments.AsReadOnly());
        }
        return new(group, kinds.AsReadOnly(), new(blend, blendIn, blendOut), synchronization);
    }
}
