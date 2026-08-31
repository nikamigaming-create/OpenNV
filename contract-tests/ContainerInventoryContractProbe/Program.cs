using System.Text.Json;
using OpenNV.Runtime.Gameplay.Containers;

const string containerFormId = "00000010";
const string authoredItemFormId = "00000020";
const string depositedItemFormId = "00000030";

var definition = new ContainerInventoryDefinition(
    containerFormId,
    "TestContainer",
    "Test Container",
    [
        new ContainerInventoryDefinitionItem(
            authoredItemFormId,
            "TestAuthoredItem",
            "Test Authored Item",
            "MISC",
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
        depositedItemFormId,
        "TestDepositedItem",
        "Test Deposited Item",
        "ALCH",
        3));
var saved = JsonSerializer.SerializeToElement(
    store.Capture(),
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

var restored = new ContainerInventoryStore();
restored.Load(saved);
var snapshot = restored.Register(definition, legacyEmptied: false);
if (snapshot.Items.Count != 2 ||
    snapshot.Items.Single(item => item.ItemFormId == authoredItemFormId).RemainingCount != 1 ||
    snapshot.Items.Single(item => item.ItemFormId == depositedItemFormId).RemainingCount != 3)
    throw new InvalidOperationException("Deposited container stack did not survive cold restore.");

restored.TakeAll(containerFormId);
if (!restored.IsEmpty(containerFormId) || restored.RemainingItemCount != 0)
    throw new InvalidOperationException("Restored container take-all failed.");

Console.WriteLine(
    "OPENNV_CONTAINER_INVENTORY_CONTRACT_PROBE_PASS " +
    $"authored={authoredItemFormId} deposited={depositedItemFormId}");
