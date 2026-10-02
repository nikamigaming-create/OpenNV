using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyList<FalloutIniDeclaration> ReadIniDeclarations(string path) => ReadIniDeclarations(File.ReadAllBytes(path));
    internal static IReadOnlyList<FalloutIniDeclaration> ReadIniDeclarations(byte[] bytes)
    {
        var (code, image) = Load(bytes);
        return ReadIniDeclarations(code, image.CodeBase, image.Literal, image.Read, image.IsWritableExtent, image.IsFileExtent);
    }

    internal static IReadOnlyList<FalloutIniDeclaration> ReadIniDeclarations(ReadOnlySpan<byte> code, uint codeBase,
        Func<uint, string?> literal, Func<uint, int, byte[]> read, Func<uint, int, bool> writable, Func<uint, int, bool> readable)
        => new IniAssociations(code.ToArray(), codeBase, literal, read, writable, readable).Read();

    // Source-emitted x86 associations, not an executable address catalog. Both
    // constructor and inline forms must connect name/payload/receiver to the
    // same typed collection factory and virtual registration operation.
    private sealed class IniAssociations(byte[] code, uint codeBase, Func<uint, string?> literal,
        Func<uint, int, byte[]> read, Func<uint, int, bool> writable, Func<uint, int, bool> readable)
    {
        private readonly Dictionary<uint, FalloutIniCollection?> _types = [];
        private readonly Dictionary<uint, FalloutIniCollection?> _constructors = [];
        private readonly Dictionary<(uint Target, uint Slot), FalloutIniCollection> _factories = [];
        private readonly Dictionary<(FalloutIniCollection, string), FalloutIniDeclaration> _declarations = [];

        internal IReadOnlyList<FalloutIniDeclaration> Read()
        {
            for (var at = 0; at <= code.Length - 20; ++at)
            {
                var row = code.AsSpan(at);
                if (row.Length >= 20 && row[..3].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }))
                {
                    if (row.Length >= 23 && row[3] == 0x68 && row[8] == 0x68 && row[13] == 0xb9 && row[18] == 0xe8)
                        Constructor(at, 9, 14, 18, U32(row, 4));
                    else if (row[3] == 0x6a && row[5] == 0x68 && row[10] == 0xb9 && row[15] == 0xe8)
                        Constructor(at, 6, 11, 15, unchecked((uint)(sbyte)row[4]));
                    else if (row.Length >= 28 && row[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05 }) &&
                        row.Slice(10, 4).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x68 }) && row[18] == 0xb9 && row[23] == 0xe8)
                        Constructor(at, 14, 19, 23, U32(read(U32(row, 6), 4), 0));
                    else if (row.Length >= 24 && row[..5].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9 }) &&
                        row[5] is 0xe8 or 0xee && row.Slice(6, 4).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x68 }) && row[14] == 0xb9 && row[19] == 0xe8)
                        Constructor(at, 10, 15, 19, row[5] == 0xe8 ? 0x3f800000U : 0);
                }
                if (row.Length >= 25 && row[..2].SequenceEqual(new byte[] { 0xd9, 0x05 }) &&
                    row.Slice(6, 5).SequenceEqual(new byte[] { 0x51, 0xd9, 0x1c, 0x24, 0x68 }) && row[15] == 0xb9 && row[20] == 0xe8)
                    Constructor(at, 11, 16, 20, U32(read(U32(row, 2), 4), 0));
                else if (row.Length >= 21 && row[0] == 0xd9 && row[1] is 0xe8 or 0xee &&
                    row.Slice(2, 5).SequenceEqual(new byte[] { 0x51, 0xd9, 0x1c, 0x24, 0xb9 }) && row[11] == 0x68 && row[16] == 0xe8)
                    Constructor(at, 12, 7, 16, row[1] == 0xe8 ? 0x3f800000U : 0);
                if (row[..2].SequenceEqual(new byte[] { 0xc7, 0x05 }) && row.Slice(10, 2).SequenceEqual(new byte[] { 0xc7, 0x05 }) &&
                    U32(row, 12) == (ulong)U32(row, 2) + 8) Inline(at);
            }
            if (!_declarations.Keys.Any(key => key.Item1 == FalloutIniCollection.Main) ||
                !_declarations.Keys.Any(key => key.Item1 == FalloutIniCollection.Prefs))
                throw new NotSupportedException("Owned executable INI collections have no complete admitted association owner.");
            return Array.AsReadOnly(_declarations.Values.ToArray());
        }

        private void Constructor(int at, int nameAt, int receiverAt, int callAt, uint payload)
        {
            var row = code.AsSpan(at);
            if (!writable(U32(row, receiverAt), 12) || literal(U32(row, nameAt)) is not { Length: > 0 } name) return;
            var target = Target(at + callAt);
            if (!_constructors.TryGetValue(target, out var family))
            {
                family = ConstructorFamily(target);
                _constructors.Add(target, family);
            }
            if (family is { } collection) Add(name, collection, payload);
        }

        private FalloutIniCollection? ConstructorFamily(uint target)
        {
            var offset = Offset(target);
            ReadOnlySpan<byte> body = code.AsSpan(offset, Math.Min(192, code.Length - offset));
            var virtualAt = -1;
            FalloutIniCollection? family = null;
            for (var at = 0; at <= body.Length - 6; ++at)
            {
                if (body[at] != 0xc7 || body[at + 1] is not (0 or 1 or 2)) continue;
                if (DescriptorType(U32(body, at + 2)) is not { } candidate) continue;
                if (family is not null) throw new InvalidDataException("INI constructor has ambiguous typed descriptor writes.");
                family = candidate; virtualAt = at;
            }
            if (family is null) return null;
            body = Body(target, 192);
            if (virtualAt >= body.Length) throw Unbound("constructor extent");
            var receiverAt = body.IndexOf(new byte[] { 0x89, 0x4d });
            if (receiverAt < 0 || receiverAt + 3 > virtualAt) throw Unbound("constructor receiver");
            var receiver = body[receiverAt + 2];
            var forward = body.Slice(receiverAt + 3, virtualAt - receiverAt - 3);
            var namePush = forward.IndexOf(new byte[] { 0x8b, 0x4d, 0x08, 0x51 });
            if (namePush < 0) namePush = forward.IndexOf(new byte[] { 0x8b, 0x45, 0x08, 0x50 });
            if (namePush < 0 || !(forward.IndexOf(new byte[] { 0x8b, 0x45, 0x0c, 0x50 }) >= 0 ||
                forward.IndexOf(new byte[] { 0x0f, 0xb6, 0x45, 0x0c, 0x50 }) >= 0 ||
                forward.IndexOf(new byte[] { 0x51, 0xd9, 0x45, 0x0c, 0xd9, 0x1c, 0x24 }) >= 0))
                throw Unbound("constructor name/payload forwarding");
            var baseCall = Find(forward, [0x8b, 0x4d, receiver, 0xe8]);
            if (baseCall < 0) throw Unbound("constructor base receiver");
            BaseDescriptor(Target(Offset(target) + receiverAt + 3 + baseCall + 3));
            var register = body[(virtualAt + 6)..];
            // A source collection getter returns EAX; that local is used for
            // the virtual call while the original descriptor is pushed once.
            if (register.Length < 5 || register[0] != 0xe8) throw Unbound("constructor collection getter");
            var factory = Getter(Target(Offset(target) + virtualAt + 6));
            if (factory != family) throw Unbound("constructor collection identity");
            Register(register[5..], receiver, null);
            return family;
        }

        private void BaseDescriptor(uint target)
        {
            var body = Body(target, 96);
            if (!body.StartsWith(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d })) throw Unbound("base descriptor");
            var receiver = body[6];
            if (body.IndexOf(new byte[] { 0x8b, 0x4d, receiver, 0x8b, 0x55, 0x08, 0x89, 0x51, 0x08 }) < 0 ||
                !(body.IndexOf(new byte[] { 0x8b, 0x45, receiver, 0x8b, 0x4d, 0x0c, 0x89, 0x48, 0x04 }) >= 0 ||
                  body.IndexOf(new byte[] { 0x8b, 0x45, receiver, 0xd9, 0x45, 0x0c, 0xd9, 0x58, 0x04 }) >= 0 ||
                  body.IndexOf(new byte[] { 0x8b, 0x45, receiver, 0x8a, 0x4d, 0x0c, 0x88, 0x48, 0x04 }) >= 0))
                throw Unbound("base descriptor name/payload stores");
        }

        private void Inline(int at)
        {
            var row = code.AsSpan(at, Math.Min(160, code.Length - at));
            var receiver = U32(row, 2);
            if (!writable(receiver, 12) || literal(U32(row, 16)) is not { Length: > 0 } name) return;
            var derivedAt = -1; FalloutIniCollection? family = null;
            for (var index = 20; index <= row.Length - 20; ++index)
            {
                if (row[index] != 0xc7 || row[index + 1] != 0x05 || U32(row, index + 2) != receiver || row[index + 10] != 0xe8) continue;
                if (DescriptorType(U32(row, index + 6)) is not { } candidate) continue;
                derivedAt = index; family = candidate; break;
            }
            if (family is null) return;
            var payload = InlinePayload(at, row.Slice(20, derivedAt - 20), checked(receiver + 4), name);
            var singleton = row[(derivedAt + 15)..];
            var registerAt = derivedAt + 20;
            uint slot; byte resultRegister;
            if (singleton[0] == 0xa1) { slot = U32(singleton, 1); resultRegister = 0; }
            else if (singleton.Length >= 6 && singleton[0] == 0x8b && singleton[1] == 0x15)
            { slot = U32(singleton, 2); resultRegister = 2; ++registerAt; }
            else throw Unbound("inline collection singleton");
            if (Factory(Target(at + derivedAt + 10), slot) != family) throw Unbound("inline collection identity");
            Register(row[registerAt..], null, receiver, resultRegister);
            Add(name, family.Value, payload);
        }

        private uint InlinePayload(int initializer, ReadOnlySpan<byte> body, uint slot, string name)
        {
            uint? payload = null;
            for (var at = 0; at < body.Length; ++at)
            {
                uint? found = null;
                if (at <= body.Length - 10 && body[at] == 0xc7 && body[at + 1] == 0x05 && U32(body, at + 2) == slot)
                    found = U32(body, at + 6);
                else if (at <= body.Length - 7 && body[at] == 0xc6 && body[at + 1] == 0x05 && U32(body, at + 2) == slot)
                    found = body[at + 6];
                else if (at <= body.Length - 12 && body[at] == 0xd9 && body[at + 1] == 0x05 &&
                    body[at + 6] == 0xd9 && body[at + 7] == 0x1d && U32(body, at + 8) == slot)
                    found = U32(read(U32(body, at + 2), 4), 0);
                else if (at <= body.Length - 8 && body[at] == 0xd9 && body[at + 1] is 0xe8 or 0xee &&
                    body[at + 2] == 0xd9 && body[at + 3] == 0x1d && U32(body, at + 4) == slot)
                    found = body[at + 1] == 0xe8 ? 0x3f800000U : 0;
                else if (at == 0 && body.Length >= 9 && body[..2].SequenceEqual(new byte[] { 0x8b, 0x4d }) &&
                    body.Slice(3, 2).SequenceEqual(new byte[] { 0x89, 0x0d }) && U32(body, 5) == slot)
                    found = ReadPackedIniPayload(code.AsSpan(0, initializer), body[2]);
                if (found is null) continue;
                if (payload is not null) throw new InvalidDataException("Inline INI declaration has ambiguous payload stores.");
                payload = found;
            }
            return payload ?? throw Unbound("inline descriptor payload for " + name);
        }

        private static void Register(ReadOnlySpan<byte> body, byte? receiverLocal, uint? receiverAddress, byte resultRegister = 0)
        {
            if (body.Length < 3 || body[0] != 0x89 || body[1] != (0x45 | resultRegister << 3)) throw Unbound("registration result owner");
            var collection = body[2];
            var receiverAt = 3;
            if (receiverLocal is { } local)
            {
                if (body.Length < 7 || body[3] != 0x8b || body[5] != local || body[4] is not (0x45 or 0x55) ||
                    body[6] != (body[4] == 0x45 ? 0x50 : 0x52)) throw Unbound("registration descriptor local");
                receiverAt = 7;
            }
            else
            {
                if (body.Length < 8 || body[3] != 0x68 || U32(body, 4) != receiverAddress) throw Unbound("registration descriptor identity");
                receiverAt = 8;
            }
            var tail = body[receiverAt..];
            if (!(tail.StartsWith(new byte[] { 0x8b, 0x4d, collection, 0x8b, 0x11, 0x8b, 0x4d, collection, 0x8b, 0x42, 0x04, 0xff, 0xd0 }) ||
                tail.StartsWith(new byte[] { 0x8b, 0x45, collection, 0x8b, 0x10, 0x8b, 0x4d, collection, 0x8b, 0x42, 0x04, 0xff, 0xd0 })))
                throw Unbound("collection virtual add");
        }

        private FalloutIniCollection Getter(uint target)
        {
            var body = Body(target, 32);
            if (body.Length < 15 || !body[..4].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0xe8 }) ||
                body[8] != 0xa1 || body[13] != 0x5d || body[14] != 0xc3) throw Unbound("collection getter");
            return Factory(Target(Offset(target) + 3), U32(body, 9));
        }

        private FalloutIniCollection Factory(uint target, uint slot)
        {
            if (_factories.TryGetValue((target, slot), out var cached)) return cached;
            if (!writable(slot, 4)) throw Unbound("collection singleton storage");
            var body = Body(target, 192);
            var check = -1;
            for (var at = 0; at <= body.Length - 7; ++at)
                if (body[at] == 0x83 && body[at + 1] == 0x3d && U32(body, at + 2) == slot && body[at + 6] == 0) { check = at; break; }
            if (check < 0 || body.Length - check < 74 || body[check + 7] != 0x75) throw Unbound("collection factory singleton guard");
            var guard = check + 32;
            if (!body.Slice(guard, 2).SequenceEqual(new byte[] { 0x83, 0x7d }) || body[guard + 3] != 0 || body[guard + 4] != 0x74 ||
                body[guard + 6] != 0x8b || body[guard + 7] != 0x4d || body[guard + 8] != body[guard + 2] || body[guard + 9] != 0xe8)
                throw Unbound("collection factory allocated receiver");
            var family = CollectionConstructor(Target(Offset(target) + guard + 9));
            var result = body[guard + 14];
            if (result != 0x89 || body[guard + 15] != 0x45 ||
                body[guard + 16] != body[guard + 21] || body[guard + 19] != 0xc7 || body[guard + 20] != 0x45 || U32(body, guard + 22) != 0)
                throw Unbound("collection factory result branches");
            var store = Find(body, [0x89, 0x0d]);
            if (store < guard + 26 || U32(body, store + 2) != slot) throw Unbound("collection factory singleton store");
            _factories.Add((target, slot), family);
            return family;
        }

        private FalloutIniCollection CollectionConstructor(uint target)
        {
            var body = Body(target, 128);
            if (!body.StartsWith(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d })) throw Unbound("collection constructor receiver");
            var receiver = body[6];
            for (var at = 7; at <= body.Length - 9; ++at)
                if (body.Slice(at, 5).SequenceEqual(new byte[] { 0x8b, 0x45, receiver, 0xc7, 0x00 }) &&
                    CollectionType(U32(body, at + 5)) is { } family) return family;
            throw Unbound("collection constructor RTTI");
        }

        private FalloutIniCollection? DescriptorType(uint vtable)
        {
            if (_types.TryGetValue(vtable, out var cached)) return cached;
            var family = Family(TypeName(vtable), true);
            _types.Add(vtable, family);
            return family;
        }
        private FalloutIniCollection? CollectionType(uint vtable) => Family(TypeName(vtable), false);
        private static FalloutIniCollection? Family(string? name, bool descriptor) => name switch
        {
            ".?AV?$SettingT@VINISettingCollection@@@@" when descriptor => FalloutIniCollection.Main,
            ".?AV?$SettingT@VINIPrefSettingCollection@@@@" when descriptor => FalloutIniCollection.Prefs,
            ".?AV?$SettingT@VRendererSettingCollection@@@@" when descriptor => FalloutIniCollection.Renderer,
            ".?AVINISettingCollection@@" when !descriptor => FalloutIniCollection.Main,
            ".?AVINIPrefSettingCollection@@" when !descriptor => FalloutIniCollection.Prefs,
            ".?AVRendererSettingCollection@@" when !descriptor => FalloutIniCollection.Renderer,
            _ => null,
        };
        private string? TypeName(uint vtable)
        {
            // A random immediate store is not a virtual table. Only backed
            // MSVC locators become candidates; admitted owners still require
            // their complete constructor/factory/registration relationships.
            if (vtable < 4 || !readable(vtable - 4, 8)) return null;
            var locator = U32(read(checked(vtable - 4), 4), 0);
            if (!readable(locator, 20)) return null;
            var row = read(locator, 20);
            if (U32(row, 0) != 0 || U32(row, 4) != 0 || U32(row, 8) != 0) return null;
            var address = checked(U32(row, 12) + 8);
            if (!readable(address, 1)) return null;
            var text = new List<byte>();
            for (var at = 0U; at < 256; ++at)
            {
                var character = read(checked(address + at), 1)[0];
                if (character == 0) return Encoding.ASCII.GetString(text.ToArray());
                if (character is < 32 or > 126) return null;
                text.Add(character);
            }
            throw Unbound("collection RTTI string extent");
        }
        private void Add(string name, FalloutIniCollection collection, uint payload)
        {
            var declaration = new FalloutIniDeclaration(name, collection, payload);
            if (declaration.NumericDefault is { } number && !double.IsFinite(number) || declaration.Kind == 'b' && payload > 1)
                throw new InvalidDataException($"Owned INI setting has an invalid canonical payload: {name}.");
            var key = (collection, name.ToUpperInvariant());
            if (!_declarations.TryAdd(key, declaration)) throw new InvalidDataException($"Multiple source INI associations declare {name} in {collection}.");
        }
        private int Offset(uint target) => target >= codeBase && (ulong)target - codeBase < (ulong)code.Length
            ? checked((int)(target - codeBase)) : throw Unbound("code target");
        private ReadOnlySpan<byte> Body(uint target, int maximum)
        {
            var at = Offset(target); var body = code.AsSpan(at, Math.Min(maximum, code.Length - at));
            // These admitted owners have a frame return; never inspect the next
            // function as proof of a registration missing from this one.
            var plain = body.IndexOf(new byte[] { 0x5d, 0xc3 });
            var pop = body.IndexOf(new byte[] { 0x5d, 0xc2, 0x08, 0x00 });
            var end = plain < 0 ? pop < 0 ? -1 : pop + 4 : pop < 0 ? plain + 2 : Math.Min(plain + 2, pop + 4);
            if (end < 0) throw Unbound("function extent");
            return body[..end];
        }
        private uint Target(int call) => code[call] == 0xe8
            ? checked((uint)((long)codeBase + call + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(call + 1, 4))))
            : throw Unbound("relative call");
        private static int Find(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> pattern) => bytes.IndexOf(pattern);
        private static NotSupportedException Unbound(string owner) => new($"Owned INI association is unbound: {owner}.");
    }
}
