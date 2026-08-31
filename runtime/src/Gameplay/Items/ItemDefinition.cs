using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Gameplay.Items;

/// <summary>
/// Canonical source-derived identity and economics shared by runtime inventory owners.
/// A missing value or weight means unknown, never zero.
/// </summary>
internal sealed record ItemDefinition
{
    internal ItemDefinition(
        string formId,
        string editorId,
        string? displayName,
        string recordType,
        int? value,
        float? weight)
    {
        FormId = FalloutFormId.Normalize(formId);
        if (string.IsNullOrWhiteSpace(editorId) || string.IsNullOrWhiteSpace(recordType) ||
            value is < 0 || weight is { } resolvedWeight &&
            (!float.IsFinite(resolvedWeight) || resolvedWeight < 0.0f))
            throw new InvalidOperationException($"Item definition is invalid: {FormId}.");
        EditorId = editorId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        RecordType = recordType;
        Value = value;
        Weight = weight;
    }

    internal string FormId { get; }
    internal string EditorId { get; }
    internal string? DisplayName { get; }
    internal string RecordType { get; }
    internal int? Value { get; }
    internal float? Weight { get; }

    internal ItemDefinition Merge(ItemDefinition other)
    {
        if (!FormId.Equals(other.FormId, StringComparison.OrdinalIgnoreCase) ||
            !EditorId.Equals(other.EditorId, StringComparison.OrdinalIgnoreCase) ||
            !RecordType.Equals(other.RecordType, StringComparison.Ordinal) ||
            DisplayName is not null && other.DisplayName is not null &&
            !DisplayName.Equals(other.DisplayName, StringComparison.Ordinal) ||
            Value is not null && other.Value is not null && Value != other.Value ||
            Weight is not null && other.Weight is not null && Weight != other.Weight)
            throw new InvalidOperationException($"Item definition is ambiguous: {FormId}.");
        return new ItemDefinition(
            FormId,
            EditorId,
            DisplayName ?? other.DisplayName,
            RecordType,
            Value ?? other.Value,
            Weight ?? other.Weight);
    }
}
