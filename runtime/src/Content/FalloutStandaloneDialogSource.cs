using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutStandaloneMenuResource(string Path, string Sha256);
internal sealed record FalloutStandaloneDialogControl(int Ordinal, uint Id, string Path, string Kind);
internal sealed record FalloutStandaloneDialogDeclaration(string Interface, string Contract, string Stack,
    IReadOnlyList<FalloutStandaloneMenuResource> Resources, uint StackingBits,
    IReadOnlyList<FalloutStandaloneDialogControl> Controls)
{
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Interface + "\0" + Contract + "\0" + Stack + "\0" +
        string.Join('\0', Resources.Select(row => row.Path + ":" + row.Sha256)) + "\0" + StackingBits + "\0" +
        string.Join('\0', Controls.Select(row => $"{row.Ordinal}:{row.Id}:{row.Path}:{row.Kind}")));
}
internal sealed record FalloutStandaloneDialogDocument(XElement Root,
    IReadOnlyList<FalloutStandaloneMenuResource> Resources);

// This catalogue is bound to the independently selected executable, not the
// campaign name. Source XML controls retain their callback order; duplicate
// indices overwrite the same returned field, just as the original callback.
internal static class FalloutStandaloneDialogSource
{
    internal const uint MenuId = 1009;
    internal const string Path = "menus/dialog/dialog_menu.xml";
    private const string Rules = "source-DialogMenu/v1;menu1009;constructor-lifecycle4-flags256-null-root-four-null-controls;" +
        "unsigned-control-index-at-most3;returned-order-last-field-store;root-store-before-stacking-query;" +
        "stacking-register-equal102-6006-103-6007;identity-store-before-push;ten-slot-first-empty;mode3-first-push;" +
        "dependent-native-scene-byte-independent;selected-menu-tile-null-source-constructor;four-required-after-root-bind;" +
        "speaker-reference-store-before-original-actor-children;" +
        "visible-zero-before-show;duration-source-locus4033-nonzero-else4034;null-root-quarter;" +
        "transition-remove-first-same-menu-before-tail-append;duration-maxss-input-zero;elapsed-Float32;" +
        "kind4-delta-divide-independent-scale;progress-maxss-zero-minss-one-ratio;remove-only-ordered-one;" +
        "control-word29-constructor-and252;menu-control-low-byte-plus-argument-shift8;" +
        "class-code-bits2..7;source-form-flag2-and-not-pressed2;bit0-store;" +
        "native-factory-current-thread-and-fresh-cold-owner;unowned-actor-TLS-event-input-remain-visible";
    internal static string Contract => FalloutAdvancementRuntimeReceipt.Hash(Rules);

    internal static FalloutStandaloneDialogDocument Read(RuntimeLiveContentSource source)
        => Read(path => source.TryRead(path, null, out var bytes, out _) ? bytes :
            throw new FileNotFoundException("Original Dialog XML/prefab is missing.", path));

    internal static FalloutStandaloneDialogDocument Read(Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var resources = new List<FalloutStandaloneMenuResource>();
        XElement ReadMember(string path)
        {
            ValidateMemberPath(path);
            var bytes = read(path) ?? throw new InvalidDataException("Source menu reader omitted its actual bytes.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (resources.Any(row => row.Path == path && row.Sha256 != hash))
                throw new InvalidDataException("Original menu dependency changed inside its actual expansion.");
            resources.Add(new(path, hash));
            return FalloutMenuXml.Parse(bytes);
        }
        var expanded = FalloutMenuXml.Expand(ReadMember(Path), ReadMember);
        var root = expanded.Elements("menu").Single();
        return new(root, resources.ToArray());
    }

    internal static FalloutStandaloneDialogDeclaration Bind(FalloutStandaloneInterfaceSource source, string stack,
        FalloutStandaloneDialogDocument document)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (!FalloutSourceMainFamily.IsFallout3(source.Main.EngineSha256) || document.Root.Name != "menu" ||
            document.Root.Elements("class").Single().Value.Trim() != "entity_DialogMenu")
            throw new NotSupportedException("Dialog root does not select the actual admitted class declaration.");
        var stacking = Stacking(document.Root.Elements("stackingtype").Single());
        var controls = document.Root.DescendantsAndSelf()
            .Where(tile => (tile.Name.LocalName is "menu" or "rect" or "hotrect" or "text" or "image") &&
                !tile.Ancestors("template").Any())
            .Select((tile, ordinal) => (Tile: tile, Ordinal: ordinal))
            .Where(row => row.Tile.Element("id") is not null)
            .Select(row => new FalloutStandaloneDialogControl(row.Ordinal, ControlId(row.Tile.Element("id")!),
                TilePath(document.Root, row.Tile), row.Tile.Name.LocalName)).ToArray();
        var declaration = new FalloutStandaloneDialogDeclaration(source.Identity, Contract, stack,
            document.Resources.ToArray(), BitConverter.SingleToUInt32Bits(stacking), controls);
        Validate(source, stack, declaration); return declaration;
    }
    internal static void Validate(FalloutStandaloneInterfaceSource source, string stack, FalloutStandaloneDialogDeclaration declaration)
    {
        source.Validate();
        if (declaration is null || declaration.Interface != source.Identity || declaration.Contract != Contract || declaration.Stack != stack ||
            declaration.Resources is not { Count: > 0 } || declaration.Resources[0] is null || declaration.Resources[0].Path != Path ||
            declaration.Resources.Any(row => row is null || string.IsNullOrWhiteSpace(row.Path) || !FalloutAdvancementRuntimeReceipt.Digest(row.Sha256) ||
                row.Path != Path && !row.Path.StartsWith("menus/prefabs/", StringComparison.Ordinal) ||
                row.Path.Split('/').Any(part => part.Length == 0 || part is "." or "..")) ||
            declaration.Resources.GroupBy(row => row.Path).Any(group => group.Select(row => row.Sha256).Distinct().Count() != 1) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(declaration.StackingBits)) || declaration.Controls is null ||
            declaration.Controls.Any(row => row is null || row.Ordinal < 0 || string.IsNullOrWhiteSpace(row.Path) ||
                row.Kind is not ("menu" or "rect" or "hotrect" or "text" or "image")) ||
            !declaration.Controls.Select(row => row.Ordinal).SequenceEqual(declaration.Controls.Select(row => row.Ordinal).Distinct().Order()))
            throw new InvalidDataException("Dialog lost its exact original class, winning dependency or callback declaration.");
    }
    internal static float Stacking(XElement property)
    {
        if (property.HasElements) throw new NotSupportedException("Dynamic original menu stacking expression is unowned.");
        var literal = property.Value.Trim();
        return literal switch
        {
            "entity_click_past" => 101,
            "entity_no_click_past" => 102,
            "entity_mixed_menu" => 103,
            "entity_does_not_stack" => 6008,
            _ when float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) => value,
            _ => throw new NotSupportedException("Selected menu stacking token has no original entity producer."),
        };
    }
    internal static bool Registers(uint bits)
    {
        var value = BitConverter.UInt32BitsToSingle(bits);
        return !float.IsNaN(value) && value is 102 or 6006 or 103 or 6007;
    }
    private static uint ControlId(XElement property)
    {
        if (property.HasElements) throw new NotSupportedException("Dynamic source control id is unowned.");
        var value = property.Value.Trim();
        if (value == "entity_generic") return uint.MaxValue; // The original entity registers signed -1, transported as UInt32.
        if (value == "entity_noglow_branch") return 111; // Independently registered engine entity, outside the four returned fields.
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new NotSupportedException("Source tile id has no admitted unsigned callback operand.");
        return id;
    }
    private static void ValidateMemberPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path != Path && !path.StartsWith("menus/prefabs/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException("Original menu expansion supplied an invalid logical dependency.");
    }
    internal static string TilePath(XElement root, XElement tile)
    {
        var segments = new Stack<string>();
        for (var value = tile; value is not null; value = value.Parent)
        {
            var ordinal = value.ElementsBeforeSelf(value.Name).Count();
            segments.Push(value.Name.LocalName + ":" + ordinal);
            if (ReferenceEquals(value, root)) return string.Join('/', segments);
        }
        throw new InvalidDataException("Returned source tile is outside the actual menu root.");
    }
}
