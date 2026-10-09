using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSleepWaitSource(string EngineSha256, string RuntimeSha256, string MenuSha256,
    string ContractSha256, bool HasHardcoreConsumer, bool CarryAtDayBoundary, int MaximumMenuHours)
{
    internal const string MenuPath = "menus/sleep_wait_menu.xml";
    internal const uint MenuId = 0x3f4;
    internal const int RestControl = 15;
    internal const uint NoRestRecordFlag = 0x0008_0000;
    internal const byte InteriorCellDataFlag = 0x01;
    private const string Contract = "source-slider-zero-based-plus1;menu-float32-frame-countdown-ge1-reset0;one-hour-no-catchup;" +
        "independent-menu-counting-byte-before-action-and-slider-target-clears;source-visibility-not-phase;" +
        "independent-signed-player-hours-and-sleep-flag;script-hours-once-per-actual-player-update;" +
        "hardcore-prelude-before-world-and-calendar;source-healing-and-hardcore-sleep-debt;" +
        "completed-sleep-restores-nonhardcore-health-ap-limbs;menu-next-update-close-gate;cancel-no-completion;" +
        "source-modifier-ordinals-and-current-minute-baselines;attempted-operation-prefix-no-cold-replay";
    internal string Identity => Hash(JsonSerializer.Serialize(this));

    internal static FalloutSleepWaitSource Read(FalloutPluginStack records, FalloutAdvancementRuntimeReceipt runtime)
    {
        runtime.Validate();
        var declaration = runtime.EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => (Hardcore: true, CarryAtBoundary: true),
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => (Hardcore: false, CarryAtBoundary: false),
            _ => throw new NotSupportedException("Selected executable has no reviewed sleep/wait consumer contract."),
        };
        var source = records.OwnedSource ?? throw new NotSupportedException("Sleep/wait has no selected owned source.");
        if (!source.TryRead(MenuPath, null, out var bytes, out _))
            throw new FileNotFoundException("Selected sleep/wait source menu is absent.", MenuPath);
        var menu = FalloutMenuXml.Parse(bytes).Elements("menu").Single();
        var type = menu.Element("class")?.Value.Trim();
        if (type != "entity_SleepWaitMenu" && type != "1012" && !string.Equals(type, "0x3f4", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Sleep/wait XML has another source menu class.");
        var slider = menu.Descendants().Single(tile => (string?)tile.Attribute("name") == "SWM_Scrollbar");
        static int Integer(System.Xml.Linq.XElement owner, string property)
        {
            var value = owner.Element(property) ?? throw new InvalidDataException("Sleep/wait slider has no " + property);
            if (value.HasElements || !int.TryParse(value.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                throw new NotSupportedException("Sleep/wait slider has an unowned dynamic " + property);
            return integer;
        }
        var count = Integer(slider, "_number_of_items");
        var visible = Integer(slider, "_number_of_visible_items");
        if (visible != 1 || Integer(slider, "_step_size") != 1 || count < visible)
            throw new NotSupportedException("Sleep/wait slider requires another source allocation consumer.");
        return new(runtime.EngineSha256, runtime.SourceSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Hash(Contract), declaration.Hardcore, declaration.CarryAtBoundary, count);
    }
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(EngineSha256) || !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(MenuSha256) || ContractSha256 != Hash(Contract) || MaximumMenuHours < 1)
            throw new InvalidDataException("Sleep/wait source declaration is invalid.");
        var expected = EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => (true, true),
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => (false, false),
            _ => throw new NotSupportedException("Sleep/wait source engine is not admitted."),
        };
        if ((HasHardcoreConsumer, CarryAtDayBoundary) != expected)
            throw new InvalidDataException("Sleep/wait scalar consumers differ from the selected source.");
    }
    internal void RequireCurrent(FalloutSleepWaitSource current)
    {
        Validate(); current.Validate();
        if (this != current) throw new InvalidDataException("Saved sleep/wait belongs to another selected source or menu.");
    }
    internal static string CurrentContractSha256 => Hash(Contract);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
