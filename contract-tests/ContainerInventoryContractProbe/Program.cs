using System.Text.Json;
using OpenNV.Runtime.Gameplay.Containers;
using OpenNV.Runtime.Gameplay.Items;

const string containerFormId = "00000010";
const string authoredItemFormId = "00000020";
const string depositedItemFormId = "00000030";

var unknownEconomics = new ItemDefinition(
    authoredItemFormId,
    "TestAuthoredItem",
    "Test Authored Item",
    "MISC",
    null,
    null);
var enriched = unknownEconomics.Merge(new ItemDefinition(
    authoredItemFormId,
    "TestAuthoredItem",
    "Test Authored Item",
    "MISC",
    12,
    1.5f));
if (enriched.Value != 12 || enriched.Weight != 1.5f)
    throw new InvalidOperationException("Canonical item economics did not enrich unknown fields.");
try
{
    _ = enriched.Merge(new ItemDefinition(
        authoredItemFormId,
        "TestAuthoredItem",
        "Test Authored Item",
        "MISC",
        13,
        1.5f));
    throw new InvalidOperationException("Conflicting item economics were accepted.");
}
catch (InvalidOperationException exception) when (
    exception.Message.StartsWith("Item definition is ambiguous", StringComparison.Ordinal))
{
}

var definition = new ContainerInventoryDefinition(
    containerFormId,
    "TestContainer",
    "Test Container",
    [
        new ContainerInventoryDefinitionItem(
            new ItemDefinition(
                authoredItemFormId,
                "TestAuthoredItem",
                "Test Authored Item",
                "MISC",
                12,
                1.5f),
            2,
            true),
    ]);
var store = new ContainerInventoryStore();
store.Register(definition, legacyEmptied: false);
var taken = store.TakeOne(containerFormId, authoredItemFormId);
if (taken.Count != 1 || store.RemainingItemCount != 1)
    throw new InvalidOperationException("Authored container withdrawal failed.");

store.Put(
    containerFormId,
    new ContainerTransfer(
        new ItemDefinition(
            depositedItemFormId,
            "TestDepositedItem",
            "Test Deposited Item",
            "ALCH",
            25,
            0.25f),
        3));
var saved = JsonSerializer.SerializeToElement(
    store.Capture(),
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

var restored = new ContainerInventoryStore();
restored.Load(saved);
var snapshot = restored.Register(definition, legacyEmptied: false);
if (snapshot.Items.Count != 2 ||
    snapshot.Items.Single(item => item.ItemFormId == authoredItemFormId).RemainingCount != 1 ||
    snapshot.Items.Single(item => item.ItemFormId == depositedItemFormId).RemainingCount != 3 ||
    snapshot.Items.Single(item => item.ItemFormId == authoredItemFormId).Definition.Value != 12 ||
    snapshot.Items.Single(item => item.ItemFormId == depositedItemFormId).Definition.Weight != 0.25f)
    throw new InvalidOperationException("Deposited container stack did not survive cold restore.");

restored.TakeAll(containerFormId);
if (!restored.IsEmpty(containerFormId) || restored.RemainingItemCount != 0)
    throw new InvalidOperationException("Restored container take-all failed.");

Console.WriteLine(
    "OPENNV_CONTAINER_INVENTORY_CONTRACT_PROBE_PASS " +
    $"authored={authoredItemFormId} deposited={depositedItemFormId}");
