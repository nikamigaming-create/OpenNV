using System.Text.Json;
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

    internal static ItemDefinition ReadCompiled(JsonElement source)
    {
        var definition = new ItemDefinition(
            source.GetProperty("itemFormId").GetString()!,
            source.GetProperty("itemEditorId").GetString()!,
            source.TryGetProperty("itemDisplayName", out var displayName)
                ? displayName.GetString()
                : null,
            source.GetProperty("itemRecordType").GetString()!,
            source.TryGetProperty("itemValue", out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null,
            source.TryGetProperty("itemWeight", out var weight) && weight.ValueKind == JsonValueKind.Number
                ? weight.GetSingle()
                : null);
        var contract = source.GetProperty("itemDefinition");
        var contractSource = contract.GetProperty("source");
        var economicsStatus = contractSource.GetProperty("economicsStatus").GetString();
        if (contract.GetProperty("schema").GetString() != "opennv-owned-item-definition/v1" ||
            !definition.FormId.Equals(contract.GetProperty("formId").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !definition.EditorId.Equals(contract.GetProperty("editorId").GetString(), StringComparison.Ordinal) ||
            !string.Equals(definition.DisplayName ?? "", contract.GetProperty("displayName").GetString(), StringComparison.Ordinal) ||
            !definition.RecordType.Equals(contract.GetProperty("recordType").GetString(), StringComparison.Ordinal) ||
            !definition.FormId.Equals(contractSource.GetProperty("recordFormId").GetString(), StringComparison.OrdinalIgnoreCase) ||
            !definition.RecordType.Equals(contractSource.GetProperty("recordType").GetString(), StringComparison.Ordinal) ||
            economicsStatus is not "source-bound" and not "unsupported-record-layout" ||
            economicsStatus == "source-bound" && (definition.Value is null || definition.Weight is null) ||
            economicsStatus == "unsupported-record-layout" && (definition.Value is not null || definition.Weight is not null))
            throw new InvalidOperationException(
                $"Compiled item definition provenance is invalid: {definition.FormId}.");
        return definition;
    }

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
