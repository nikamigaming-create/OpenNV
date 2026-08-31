using System.Text.Json;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Gameplay.Containers;

internal sealed class ContainerInventoryStore
{
    private readonly Dictionary<string, ContainerState> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedContainerState> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    internal int RegisteredContainers => _active.Count;
    internal int RemainingItemCount =>
        _active.Values.Sum(state => state.Items.Values.Sum(item => item.RemainingCount)) +
        _loaded.Values.Sum(state => state.Items.Sum(item => item.RemainingCount));

    internal ContainerInventorySnapshot Register(
        ContainerInstance container,
        bool legacyEmptied) => Register(
            new ContainerInventoryDefinition(
                container.ReferenceFormId,
                container.EditorId,
                container.DisplayName,
                container.Items.Select(item => new ContainerInventoryDefinitionItem(
                    item.ItemFormId,
                    item.EditorId,
                    item.DisplayName,
                    item.RecordType,
                    item.Count,
                    item.Resolved)).ToArray()),
            legacyEmptied);

    internal ContainerInventorySnapshot Register(
        ContainerInventoryDefinition container,
        bool legacyEmptied)
    {
        var referenceFormId = FalloutFormId.Normalize(container.ReferenceFormId);
        if (_active.TryGetValue(referenceFormId, out var existing))
            return existing.Snapshot();

        var authored = BuildAuthoredState(container, referenceFormId);
        if (_loaded.Remove(referenceFormId, out var loaded))
            authored.Apply(loaded);
        else if (legacyEmptied)
            authored.TakeAll();
        _active.Add(referenceFormId, authored);
        return authored.Snapshot();
    }

    internal ContainerTransfer TakeOne(string referenceFormId, string itemFormId)
    {
        var state = RequiredState(referenceFormId);
        var normalizedItem = FalloutFormId.Normalize(itemFormId);
        if (!state.Items.TryGetValue(normalizedItem, out var item) || item.RemainingCount <= 0)
            throw new InvalidOperationException(
                $"Container item is unavailable: {state.ReferenceFormId}/{normalizedItem}");
        item.RemainingCount--;
        return new ContainerTransfer(
            item.ItemFormId,
            item.EditorId,
            item.DisplayName,
            item.RecordType,
            1);
    }

    internal IReadOnlyList<ContainerTransfer> TakeAll(string referenceFormId)
    {
        var state = RequiredState(referenceFormId);
        return state.TakeAll();
    }

    internal void Put(string referenceFormId, ContainerTransfer transfer)
    {
        if (transfer.Count <= 0 || string.IsNullOrWhiteSpace(transfer.EditorId) ||
            string.IsNullOrWhiteSpace(transfer.DisplayName) ||
            string.IsNullOrWhiteSpace(transfer.RecordType))
            throw new InvalidOperationException("Container deposit item identity is invalid.");
        var state = RequiredState(referenceFormId);
        var itemFormId = FalloutFormId.Normalize(transfer.ItemFormId);
        if (state.Items.TryGetValue(itemFormId, out var current))
        {
            if (!current.EditorId.Equals(transfer.EditorId, StringComparison.OrdinalIgnoreCase) ||
                !current.DisplayName.Equals(transfer.DisplayName, StringComparison.Ordinal) ||
                !current.RecordType.Equals(transfer.RecordType, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Container deposit item identity is ambiguous: {state.ReferenceFormId}/{itemFormId}");
            current.RemainingCount = checked(current.RemainingCount + transfer.Count);
            return;
        }
        state.Items.Add(
            itemFormId,
            new MutableContainerItem(
                itemFormId,
                transfer.EditorId,
                transfer.DisplayName,
                transfer.RecordType,
                transfer.Count));
    }

    internal ContainerInventorySnapshot Snapshot(string referenceFormId) =>
        RequiredState(referenceFormId).Snapshot();

    internal bool IsEmpty(string referenceFormId) =>
        RequiredState(referenceFormId).IsEmpty;

    internal IReadOnlyList<SavedContainerState> Capture()
    {
        var rows = _loaded.Values
            .Concat(_active.Values.Select(state => state.Save()))
            .OrderBy(state => state.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (rows.Select(row => row.ReferenceFormId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length)
            throw new InvalidOperationException("Container save state contains duplicate references.");
        return rows;
    }

    internal void Load(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Array || _loaded.Count > 0 || _active.Count > 0)
            throw new InvalidOperationException("Container save state has an invalid shape or lifecycle.");
        foreach (var row in source.EnumerateArray())
        {
            var referenceFormId = FalloutFormId.Normalize(
                RequiredString(row, "referenceFormId"));
            var editorId = RequiredString(row, "editorId");
            var displayName = RequiredString(row, "displayName");
            var items = row.GetProperty("items").EnumerateArray()
                .Select(item => new SavedContainerItem(
                    FalloutFormId.Normalize(RequiredString(item, "itemFormId")),
                    RequiredString(item, "editorId"),
                    RequiredString(item, "displayName"),
                    RequiredString(item, "recordType"),
                    item.GetProperty("remainingCount").GetInt32()))
                .OrderBy(item => item.ItemFormId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (items.Any(item => item.RemainingCount < 0) ||
                items.Select(item => item.ItemFormId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
                throw new InvalidOperationException(
                    $"Saved container inventory is invalid: {referenceFormId}");
            if (!_loaded.TryAdd(
                    referenceFormId,
                    new SavedContainerState(referenceFormId, editorId, displayName, items)))
                throw new InvalidOperationException(
                    $"Saved container inventory is duplicated: {referenceFormId}");
        }
    }

    internal void ValidateEmptiedReferences(IReadOnlySet<string> emptiedReferences)
    {
        foreach (var state in _loaded.Values)
        {
            var markedEmpty = emptiedReferences.Contains(state.ReferenceFormId);
            if (markedEmpty != state.IsEmpty)
                throw new InvalidOperationException(
                    $"Saved container empty marker differs from remaining contents: " +
                    state.ReferenceFormId);
        }
    }

    private ContainerState RequiredState(string referenceFormId)
    {
        var normalized = FalloutFormId.Normalize(referenceFormId);
        return _active.GetValueOrDefault(normalized) ??
            throw new InvalidOperationException($"Container is not registered: {normalized}");
    }

    private static ContainerState BuildAuthoredState(
        ContainerInventoryDefinition container,
        string referenceFormId)
    {
        if (string.IsNullOrWhiteSpace(container.EditorId))
            throw new InvalidOperationException(
                $"Compiled container has no editor identity: {referenceFormId}");
        if (string.IsNullOrWhiteSpace(container.DisplayName))
            throw new InvalidOperationException(
                $"Compiled container has no owned display name: {referenceFormId}");
        var items = new Dictionary<string, MutableContainerItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in container.Items)
        {
            if (!entry.Resolved)
                throw new InvalidOperationException(
                    $"Container has unresolved owned contents: {referenceFormId}");
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
                throw new InvalidOperationException(
                    $"Container item has no owned display name: " +
                    $"{referenceFormId}/{entry.ItemFormId}");
            if (entry.Count <= 0)
                throw new InvalidOperationException(
                    $"Container item count is invalid: {referenceFormId}/{entry.ItemFormId}");
            var itemFormId = FalloutFormId.Normalize(entry.ItemFormId);
            if (items.TryGetValue(itemFormId, out var current))
            {
                if (!current.EditorId.Equals(entry.EditorId, StringComparison.OrdinalIgnoreCase) ||
                    !current.DisplayName.Equals(entry.DisplayName, StringComparison.Ordinal) ||
                    !current.RecordType.Equals(entry.RecordType, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Container item identity is ambiguous: {referenceFormId}/{itemFormId}");
                current.RemainingCount = checked(current.RemainingCount + entry.Count);
            }
            else
            {
                items.Add(
                    itemFormId,
                    new MutableContainerItem(
                        itemFormId,
                        entry.EditorId,
                        entry.DisplayName,
                        entry.RecordType,
                        entry.Count));
            }
        }
        return new ContainerState(
            referenceFormId,
            container.EditorId,
            container.DisplayName,
            items);
    }

    private static string RequiredString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidOperationException(
                $"Container save state has no {propertyName}.");
        return property.GetString()!;
    }

    private sealed class ContainerState(
        string referenceFormId,
        string editorId,
        string displayName,
        Dictionary<string, MutableContainerItem> items)
    {
        internal string ReferenceFormId { get; } = referenceFormId;
        internal string EditorId { get; } = editorId;
        internal string DisplayName { get; } = displayName;
        internal Dictionary<string, MutableContainerItem> Items { get; } = items;
        internal bool IsEmpty => Items.Values.All(item => item.RemainingCount == 0);

        internal void Apply(SavedContainerState saved)
        {
            if (!EditorId.Equals(saved.EditorId, StringComparison.OrdinalIgnoreCase) ||
                !DisplayName.Equals(saved.DisplayName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Saved container identity differs from compiled contents: {ReferenceFormId}");
            var savedByFormId = saved.Items.ToDictionary(
                item => item.ItemFormId,
                StringComparer.OrdinalIgnoreCase);
            if (Items.Keys.Any(itemFormId => !savedByFormId.ContainsKey(itemFormId)))
                throw new InvalidOperationException(
                    $"Saved container omits compiled contents: {ReferenceFormId}");
            foreach (var savedItem in saved.Items)
            {
                if (Items.TryGetValue(savedItem.ItemFormId, out var authored))
                {
                    if (!authored.EditorId.Equals(
                            savedItem.EditorId,
                            StringComparison.OrdinalIgnoreCase) ||
                        !authored.DisplayName.Equals(savedItem.DisplayName, StringComparison.Ordinal) ||
                        !authored.RecordType.Equals(savedItem.RecordType, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Saved container item differs from compiled contents: " +
                            $"{ReferenceFormId}/{savedItem.ItemFormId}");
                    authored.RemainingCount = savedItem.RemainingCount;
                }
                else
                {
                    Items.Add(
                        savedItem.ItemFormId,
                        new MutableContainerItem(
                            savedItem.ItemFormId,
                            savedItem.EditorId,
                            savedItem.DisplayName,
                            savedItem.RecordType,
                            savedItem.RemainingCount));
                }
            }
        }

        internal IReadOnlyList<ContainerTransfer> TakeAll()
        {
            var transfers = Items.Values
                .Where(item => item.RemainingCount > 0)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ContainerTransfer(
                    item.ItemFormId,
                    item.EditorId,
                    item.DisplayName,
                    item.RecordType,
                    item.RemainingCount))
                .ToArray();
            foreach (var item in Items.Values)
                item.RemainingCount = 0;
            return transfers;
        }

        internal ContainerInventorySnapshot Snapshot() => new(
            ReferenceFormId,
            EditorId,
            DisplayName,
            Items.Values
                .Where(item => item.RemainingCount > 0)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ItemFormId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ContainerInventoryItem(
                    item.ItemFormId,
                    item.EditorId,
                    item.DisplayName,
                    item.RecordType,
                    item.RemainingCount))
                .ToArray());

        internal SavedContainerState Save() => new(
            ReferenceFormId,
            EditorId,
            DisplayName,
            Items.Values
                .OrderBy(item => item.ItemFormId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new SavedContainerItem(
                    item.ItemFormId,
                    item.EditorId,
                    item.DisplayName,
                    item.RecordType,
                    item.RemainingCount))
                .ToArray());
    }

    private sealed class MutableContainerItem(
        string itemFormId,
        string editorId,
        string displayName,
        string recordType,
        int remainingCount)
    {
        internal string ItemFormId { get; } = itemFormId;
        internal string EditorId { get; } = editorId;
        internal string DisplayName { get; } = displayName;
        internal string RecordType { get; } = recordType;
        internal int RemainingCount { get; set; } = remainingCount;
    }
}

internal sealed record ContainerInventorySnapshot(
    string ReferenceFormId,
    string EditorId,
    string DisplayName,
    IReadOnlyList<ContainerInventoryItem> Items)
{
    internal bool IsEmpty => Items.Count == 0;
}

internal sealed record ContainerInventoryItem(
    string ItemFormId,
    string EditorId,
    string DisplayName,
    string RecordType,
    int RemainingCount);

internal sealed record ContainerTransfer(
    string ItemFormId,
    string EditorId,
    string DisplayName,
    string RecordType,
    int Count);

internal sealed record SavedContainerState(
    string ReferenceFormId,
    string EditorId,
    string DisplayName,
    IReadOnlyList<SavedContainerItem> Items)
{
    internal bool IsEmpty => Items.All(item => item.RemainingCount == 0);
}

internal sealed record SavedContainerItem(
    string ItemFormId,
    string EditorId,
    string DisplayName,
    string RecordType,
    int RemainingCount);

internal sealed record ContainerInventoryDefinition(
    string ReferenceFormId,
    string EditorId,
    string DisplayName,
    IReadOnlyList<ContainerInventoryDefinitionItem> Items);

internal sealed record ContainerInventoryDefinitionItem(
    string ItemFormId,
    string EditorId,
    string DisplayName,
    string RecordType,
    int Count,
    bool Resolved);
