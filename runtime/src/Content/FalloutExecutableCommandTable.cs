using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeScriptCommandDeclaration(string Name, string Alias,
    bool RequiresReference, int Required, IReadOnlyList<byte> Parameters, bool VanillaArguments, uint Flags);

internal sealed record FalloutNativeScriptCommandCatalogue(string SourceSha256,
    IReadOnlyDictionary<ushort, FalloutNativeScriptCommandDeclaration> Commands);

internal static partial class FalloutExecutableStringTable
{
    // The public Win32 CommandInfo/ParamInfo ABI describes these source rows.
    // Reading a command declaration grants no authority to execute its handler.
    internal static FalloutNativeScriptCommandCatalogue ReadScriptCommandDeclarations(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (code, image) = Load(bytes);
        return new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), image.ScriptCommandDeclarations(code));
    }

    private sealed partial class Image
    {
        internal IReadOnlyDictionary<ushort, FalloutNativeScriptCommandDeclaration> ScriptCommandDeclarations(byte[] code)
        {
            var rows = new Dictionary<ushort, (FalloutNativeScriptCommandDeclaration Declaration, uint Parse)>();
            foreach (var section in headers.SectionHeaders.Where(section =>
                (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 40; at += sizeof(uint))
                {
                    var row = data.Slice(at, 40);
                    var opcode = U32(row, 8);
                    if (opcode is < 0x1000 or >= 0x2000) continue;
                    var name = Literal(U32(row, 0));
                    if (!CommandName(name)) continue;
                    var alias = Literal(U32(row, 4));
                    if (alias is null || alias.Length != 0 && !CommandName(alias) || Literal(U32(row, 12)) is null) continue;
                    var parent = BinaryPrimitives.ReadUInt16LittleEndian(row[16..]);
                    if (parent > 1) continue;
                    var execute = U32(row, 24);
                    var parse = U32(row, 28);
                    var evaluate = U32(row, 32);
                    bool CodePointer(uint pointer) => pointer >= CodeBase && pointer - CodeBase < code.Length;
                    if (!CodePointer(execute) || !CodePointer(parse) || evaluate != 0 && !CodePointer(evaluate)) continue;
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(row[18..]);
                    var parameters = U32(row, 20);
                    if (count != 0 && !IsFileExtent(parameters, checked(count * 12)))
                        throw new InvalidDataException("Original command parameters are not completely backed by source bytes.");
                    var types = new byte[count];
                    var required = 0;
                    var optionalSeen = false;
                    var parameterBytes = count == 0 ? [] : Read(parameters, checked(count * 12));
                    for (var index = 0; index < count; index++)
                    {
                        var parameter = parameterBytes.AsSpan(index * 12, 12);
                        var type = U32(parameter, 4);
                        var optional = U32(parameter, 8);
                        if (Literal(U32(parameter, 0)) is null)
                            throw new InvalidDataException("Original command parameter has no complete type declaration.");
                        if (type > byte.MaxValue || optional > 1)
                            throw new NotSupportedException("Original command parameter requires an unowned type or optional-flag width.");
                        if (optional == 0)
                        {
                            if (optionalSeen) throw new NotSupportedException("Original command has an unowned non-prefix required argument.");
                            required++;
                        }
                        else optionalSeen = true;
                        types[index] = (byte)type;
                    }
                    var declaration = new FalloutNativeScriptCommandDeclaration(name!, alias, parent != 0,
                        required, Array.AsReadOnly(types), false, U32(row, 36));
                    if (!rows.TryAdd((ushort)opcode, (declaration, parse)))
                        throw new InvalidDataException("Original script command opcode has ambiguous source declarations.");
                }
            }
            // Public standard-parser API declarations must agree on the actual
            // selected image's parser. No most-common-pointer inference occurs.
            uint Parser(string name)
            {
                var owners = rows.Values.Where(row => row.Declaration.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                return owners.Length == 1 ? owners[0].Parse :
                    throw new InvalidDataException("Original command table has no unique standard parser declaration: " + name);
            }
            var standardParser = Parser("GetDistance");
            if (Parser("GetActorValue") != standardParser || Parser("GetDisabled") != standardParser)
                throw new NotSupportedException("Original standard command parser declarations disagree.");
            return rows.ToDictionary(row => row.Key, row => row.Value.Declaration with
            {
                VanillaArguments = row.Value.Parse == standardParser,
            });
        }

        private static bool CommandName(string? name) => !string.IsNullOrEmpty(name) &&
            (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
            name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
