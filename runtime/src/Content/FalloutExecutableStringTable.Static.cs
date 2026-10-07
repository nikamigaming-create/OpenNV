using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal enum SettingCollection { Game, Main, Prefs, Renderer, Blend, Registry }
    internal sealed record StaticSetting(string Name, SettingCollection Collection, uint Payload, uint Descriptor = 0);

    internal static IReadOnlyList<StaticSetting> ReadStaticSettings(byte[] bytes) => ReadStaticSettings(Load(bytes).Image);

    private static IReadOnlyDictionary<string, string> StaticStrings(Image image)
        => ReadStaticSettings(image).Where(row => row.Collection == SettingCollection.Game && row.Name[0] == 's')
            .ToDictionary(row => row.Name, row => image.Literal(row.Payload) ??
                throw new NotSupportedException($"Owned static string setting has no admitted literal: {row.Name}."), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<StaticSetting> StaticNumbers(Image image, char kind, bool gameSettingsOnly)
        => ReadStaticSettings(image).Where(row => row.Name[0] == kind && (!gameSettingsOnly || row.Collection == SettingCollection.Game));

    private static IReadOnlyList<StaticSetting> ReadStaticSettings(Image image)
    {
        var result = new List<StaticSetting>();
        var names = new HashSet<(SettingCollection, string)>();
        var types = new Dictionary<uint, SettingCollection?>();
        foreach (var (address, bytes) in image.StaticDescriptorSections())
        {
            for (var at = 0; at <= bytes.Length - 12; at += 4)
            {
                var name = image.Literal(U32(bytes, at + 8));
                if (name is null || !Regex.IsMatch(name, @"^[sfibur][A-Za-z0-9_][A-Za-z0-9_ ]*(?::[A-Za-z0-9_ ]+)?$", RegexOptions.CultureInvariant)) continue;
                var vtable = U32(bytes, at);
                if (!types.TryGetValue(vtable, out var collection)) types.Add(vtable, collection = image.StaticSettingCollection(vtable));
                if (collection is not { } owner) continue;
                var payload = U32(bytes, at + 4);
                if (name[0] == 'f' && !float.IsFinite(BitConverter.Int32BitsToSingle(unchecked((int)payload))) || name[0] == 'b' && payload > 1)
                    throw new InvalidDataException($"Owned static setting has an invalid canonical payload: {name}.");
                if (!names.Add((owner, name.ToUpperInvariant()))) throw new InvalidDataException($"Multiple owned static settings declare {name} in {owner}.");
                result.Add(new(name, owner, payload, checked(address + (uint)at)));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private sealed partial class Image
    {
        internal IEnumerable<(uint Address, byte[] Data)> StaticDescriptorSections()
        {
            foreach (var section in headers.SectionHeaders.Where(section =>
                         (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0 &&
                         (section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0))
            {
                var count = Math.Min(section.VirtualSize, section.SizeOfRawData);
                if (count < 0 || section.PointerToRawData < 0 || section.PointerToRawData > bytes.Length - count)
                    throw new InvalidDataException("Owned static setting section exceeds its file extent.");
                yield return (checked(Base + (uint)section.VirtualAddress), bytes.AsSpan(section.PointerToRawData, count).ToArray());
            }
        }

        internal SettingCollection? StaticSettingCollection(uint vtable)
        {
            if (vtable < 4 || !IsReadOnlyExtent(vtable - 4, 12) ||
                !IsExecutableExtent(U32(Read(vtable, 4), 0)) || !IsExecutableExtent(U32(Read(checked(vtable + 4), 4), 0))) return null;
            var locator = U32(Read(vtable - 4, 4), 0);
            if (!IsReadOnlyExtent(locator, 20)) return null;
            var row = Read(locator, 20);
            if (U32(row, 0) != 0 || U32(row, 4) != 0 || U32(row, 8) != 0) return null;
            var descriptor = U32(row, 12);
            if (descriptor > uint.MaxValue - 8 || !IsFileExtent(descriptor, 9)) return null;
            // MSVC type descriptors contain mutable type_info storage. Their
            // names may be in .data; setting names and values remain literals.
            var text = new List<byte>();
            for (var at = 0U; at < 256; ++at)
            {
                var address = (ulong)descriptor + 8 + at;
                if (address > uint.MaxValue || !IsFileExtent((uint)address, 1)) return null;
                var character = Read((uint)address, 1)[0];
                if (character == 0) return Encoding.ASCII.GetString(text.ToArray()) switch
                {
                    ".?AV?$SettingT@VGameSettingCollection@@@@" => SettingCollection.Game,
                    ".?AV?$SettingT@VINISettingCollection@@@@" => SettingCollection.Main,
                    ".?AV?$SettingT@VINIPrefSettingCollection@@@@" => SettingCollection.Prefs,
                    ".?AV?$SettingT@VRendererSettingCollection@@@@" => SettingCollection.Renderer,
                    ".?AV?$SettingT@VBlendSettingCollection@@@@" => SettingCollection.Blend,
                    ".?AV?$SettingT@VRegSettingCollection@@@@" => SettingCollection.Registry,
                    _ => null,
                };
                if (character is < 32 or > 126) return null;
                text.Add(character);
            }
            return null;
        }

        private bool IsReadOnlyExtent(uint address, int count) => IsFileExtent(address, count) && headers.SectionHeaders.Any(section =>
            (section.SectionCharacteristics & (SectionCharacteristics.MemRead | SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute)) == SectionCharacteristics.MemRead &&
            address >= Base && address - Base >= section.VirtualAddress && (ulong)(address - Base) + (uint)count <= (ulong)section.VirtualAddress + (uint)section.SizeOfRawData);

        private bool IsExecutableExtent(uint address) => IsFileExtent(address, 1) && headers.SectionHeaders.Any(section =>
            (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0 && address >= Base &&
            address - Base >= section.VirtualAddress && (ulong)(address - Base) < (ulong)section.VirtualAddress + (uint)section.SizeOfRawData);
    }
}
