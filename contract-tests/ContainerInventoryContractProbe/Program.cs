using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Containers;
using OpenNV.Runtime.Gameplay.Crafting;
using OpenNV.Runtime.Gameplay.Items;

if (args is ["--owned-ammo", var dataRoot, var formId])
{
    RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
        activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var id = Convert.ToUInt32(formId, 16);
    var record = records.GetEffective(records.RuntimeFormKey(id));
    if (record.Signature != "AMMO") throw new InvalidDataException("Owned ammunition audit requires AMMO.");
    var fields = record.ReadSubrecords();
    var editorId = Encoding.UTF8.GetString(fields.Single(field => field.Signature == "EDID").Data.Span).TrimEnd('\0');
    var item = FalloutCampaignInventoryResolver.Resolve(records, [new(id, editorId, "AMMO", 1)], null).Items.Single();
    var data = fields.Single(field => field.Signature == "DATA").Data.Span;
    var extra = fields.Where(field => field.Signature == "DAT2").ToArray();
    if (data.Length != 13 || item.Value != BinaryPrimitives.ReadInt32LittleEndian(data[8..]) ||
        (extra.Length == 0 ? item.Weight is not null :
            item.Weight != BinaryPrimitives.ReadSingleLittleEndian(extra.Single().Data.Span[8..])))
        throw new InvalidOperationException("Owned ammunition economics diverged from source field presence or value.");
    Console.WriteLine($"OPENNV_OWNED_AMMO_ECONOMICS_PASS reference={record.FormKey} value={item.Value} weightKnown={item.Weight is not null} sourceReadonly=true gameplay=unverified");
    return;
}

const string containerFormId = "00000010";
const string authoredItemFormId = "00000020";
const string depositedItemFormId = "00000030";

CheckAmmunitionEconomics();

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
            authoredItemFormId,
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

using var craftingDocument = JsonDocument.Parse("""
    {
      "type": "crafting-station",
      "support": "unconditioned-zero-skill-recipes",
      "script": { "formId": "0013e3fb", "editorId": "CraftingCampfireRecipesScript" },
      "category": { "formId": "0013b2c0", "editorId": "CampfireRecipes", "displayName": "Campfire", "sourceKind": 254 },
      "recipes": [
        {
          "schema": "opennv-owned-crafting-recipe/v1",
          "formId": "0013b2c2",
          "editorId": "RecipeHealingPowder",
          "displayName": "Healing Powder",
          "categoryFormId": "0013b2c0",
          "subcategoryFormId": "001613cf",
          "requiredSkillLevel": 0,
          "ingredients": [
            {
              "itemFormId": "0013b2b9", "itemEditorId": "XanderRoot", "itemDisplayName": "Xander Root", "itemRecordType": "ALCH", "count": 1,
              "itemDefinition": { "schema": "opennv-owned-item-definition/v1", "formId": "0013b2b9", "editorId": "XanderRoot", "displayName": "Xander Root", "recordType": "ALCH", "source": { "recordFormId": "0013b2b9", "recordType": "ALCH", "economicsStatus": "unsupported-record-layout" } }
            },
            {
              "itemFormId": "0013b2ba", "itemEditorId": "BrocFlower", "itemDisplayName": "Broc Flower", "itemRecordType": "ALCH", "count": 1,
              "itemDefinition": { "schema": "opennv-owned-item-definition/v1", "formId": "0013b2ba", "editorId": "BrocFlower", "displayName": "Broc Flower", "recordType": "ALCH", "source": { "recordFormId": "0013b2ba", "recordType": "ALCH", "economicsStatus": "unsupported-record-layout" } }
            }
          ],
          "outputs": [
            {
              "itemFormId": "00136a1d", "itemEditorId": "NVHealingPowder", "itemDisplayName": "Healing Powder", "itemRecordType": "ALCH", "count": 1,
              "itemDefinition": { "schema": "opennv-owned-item-definition/v1", "formId": "00136a1d", "editorId": "NVHealingPowder", "displayName": "Healing Powder", "recordType": "ALCH", "source": { "recordFormId": "00136a1d", "recordType": "ALCH", "economicsStatus": "unsupported-record-layout" } }
            }
          ]
        }
      ]
    }
    """);
var crafting = CraftingStationContract.Read(craftingDocument.RootElement);
if (crafting.Category.EditorId != "CampfireRecipes" || crafting.Recipes.Count != 1 ||
    crafting.Recipes[0].Ingredients.Count != 2 || crafting.Recipes[0].Outputs.Count != 1 ||
    crafting.Recipes[0].Outputs[0].Definition.EditorId != "NVHealingPowder")
    throw new InvalidOperationException("Owned crafting station contract did not retain its recipe graph.");

Console.WriteLine(
    "OPENNV_CONTAINER_INVENTORY_CONTRACT_PROBE_PASS " +
    $"authored={authoredItemFormId} deposited={depositedItemFormId} " +
    $"crafting={crafting.Recipes[0].EditorId}");

static void CheckAmmunitionEconomics()
{
    var directory = Directory.CreateTempSubdirectory("opennv-ammunition-economics-");
    try
    {
        var records = new List<byte[]> { Record("TES4", 0, Field("HEDR", new byte[12])) };
        Add(0x100, Data());
        Add(0x101, Data(flags: 0));
        Add(0x102, Data(), Field("DAT2", Extra(12, .25f)));
        Add(0x103, Data(), Field("DAT2", Extra(20, .5f)));
        Add(0x104, Data(), Field("DAT2", Extra(12, 0)));
        Add(0x110, Data(), Field("DAT2", Extra(12, 1)), Field("DAT2", Extra(12, 1)));
        Add(0x111, Data(), Field("DAT2", []));
        Add(0x112, Data(), Field("DAT2", Extra(16, 1)));
        Add(0x113, Data(), Field("DAT2", Extra(12, float.NaN)));
        Add(0x114, Data(), Field("DAT2", Extra(20, -1)));
        Add(0x115, Data(), Field("DAT2", Extra(12, float.PositiveInfinity)));
        Add(0x116, Data(value: -1));
        Add(0x117, Data()[..12]);
        Add(0x118, Data(), Field("DATA", Data()));
        File.WriteAllBytes(Path.Combine(directory.FullName, "AmmoEconomics.esm"), Join(records.ToArray()));
        using var stack = FalloutPluginStack.Load(directory.FullName, ["AmmoEconomics.esm"]);
        foreach (var (id, weight) in new (uint Id, float? Weight)[]
            { (0x100, null), (0x101, null), (0x102, .25f), (0x103, .5f), (0x104, 0) })
        {
            var item = Resolve(id);
            if (item.Value != 7 || item.Weight != weight || item.Count != 3)
                throw new InvalidOperationException("Ammunition economics lost source value, weight presence or count.");
        }
        for (uint id = 0x110; id <= 0x118; id++)
        {
            var refused = false;
            try { _ = Resolve(id); }
            catch (InvalidDataException) { refused = true; }
            if (!refused) throw new InvalidOperationException("Malformed ammunition economics were accepted.");
        }
        var unknown = Resolve(0x100);
        var ammoDefinition = new ItemDefinition("00000100", unknown.EditorId, "Test Ammunition", "AMMO", unknown.Value, unknown.Weight);
        var container = new ContainerInventoryDefinition("00000099", "AmmoContainer", "Ammo Container",
            [new("00000100", ammoDefinition, 3, true)]);
        var ammoStore = new ContainerInventoryStore();
        ammoStore.Register(container, legacyEmptied: false);
        var ammoSaved = JsonSerializer.SerializeToElement(ammoStore.Capture(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var cold = new ContainerInventoryStore();
        cold.Load(ammoSaved);
        var restoredAmmo = cold.Register(container, legacyEmptied: false).Items.Single();
        if (restoredAmmo.RemainingCount != 3 || restoredAmmo.Definition.Value != 7 || restoredAmmo.Definition.Weight is not null)
            throw new InvalidOperationException("Cold ammunition storage invented a weight or lost source value/count.");

        FalloutCampaignItem Resolve(uint id) => FalloutCampaignInventoryResolver.Resolve(stack,
            [new(id, $"Ammo{id:x}", "AMMO", 3)], null).Items.Single();
        void Add(uint id, byte[] data, params byte[][] extra) => records.Add(Record("AMMO", id,
            new[] { Field("EDID", Encoding.ASCII.GetBytes($"Ammo{id:x}\0")), Field("DATA", data) }.Concat(extra).ToArray()));
    }
    finally { directory.Delete(true); }

    static byte[] Data(int value = 7, byte flags = 2)
    {
        var bytes = new byte[13];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, 1);
        bytes[4] = flags;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), value);
        bytes[12] = 12;
        return bytes;
    }
    static byte[] Extra(int size, float weight)
    {
        var bytes = new byte[size];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), weight);
        return bytes;
    }
    static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(bytes, 6);
        return bytes;
    }
    static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields);
        var bytes = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24);
        return bytes;
    }
}
