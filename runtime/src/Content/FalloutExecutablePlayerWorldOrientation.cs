using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private const int WorldCameraVirtualSlotCount = 55;
    internal static FalloutPlayerWorldOrientationSource ReadPlayerWorldOrientation(string path, FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("World/reference orientation changed its selected owned executable.");
        var source = FalloutPlayerWorldOrientationSource.Reviewed(controls); var (code, image) = Load(bytes);
        var graph = Table(".?AVBSSceneGraph@@", 65); var specialized = Table(".?AVSceneGraph@@", 65);
        var player = Table(".?AVPlayerCharacter@@", 191);
        var graphCtor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == graph &&
            Match(code, at + 0xd, [0xc7, 0x86, 0xac, 0, 0, 0, 0, 0, 0, 0]), 0x420, "base world constructor");
        if (!Match(code, graphCtor + 0x1d2, [0x8b, 0xc8, 0xe8]))
            throw new NotSupportedException("World constructor lost its actual linked camera constructor.");
        var cameraCtor = Call(graphCtor + 0x1d4);
        if (cameraCtor < 0 || cameraCtor > code.Length - 0x160 ||
            !Match(code, cameraCtor, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x0c, 0x56, 0x57, 0x8b, 0xf9, 0xe8]) ||
            !Match(code, cameraCtor + 0x17, [0xc7, 7]))
            throw new NotSupportedException("World camera lost its exact constructor-selected primary table store.");
        var camera = Word(code, cameraCtor + 0x19);
        // The reached World constructor selects this primary NiCamera table.
        // Its complete table has 55 executable slots; following data is not a
        // virtual method. Verify RTTI against that actual store, rather than
        // imposing another class's slot extent or global RTTI uniqueness.
        if (image.SourceClassTables(".?AVNiCamera@@", WorldCameraVirtualSlotCount).Count(table => table == camera) != 1)
            throw new NotSupportedException("World camera constructor-selected table has no exact source NiCamera association.");
        var derivedCtor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == specialized &&
            code[at - 5] == 0xe8 && Call(at - 5) == graphCtor - 0x4b &&
            Match(code, at + 6, [0x8b, 0xc6, 0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 0x0c, 0]), 16, "specialized world constructor");
        var setters = new List<int>();
        for (var at = 0xb; at <= code.Length - 0x4b; ++at)
            if (Match(code, at - 0xb, [0x6a, 0, 0x6a, 0, 0x68]) && image.Literal(Word(code, at - 6)) == "World" &&
                code[at] == 0xe8 && Call(at) == derivedCtor - 0x18 &&
                Match(code, at + 0x11, [0x8b, 0x15]) && Match(code, at + 0x1e, [0x3b, 0xd1, 0x74]) &&
                Match(code, at + 0x39, [0x89, 0x0d])) setters.Add(at);
        if (setters.Count != 1) throw new NotSupportedException("World root has no unique source ordinary construction/publication.");
        var global = Word(code, setters[0] + 0x13);
        if (global != Word(code, setters[0] + 0x3b) || Word(image.ReadLoaderData(global, 4), 0) != 0)
            throw new NotSupportedException("World root lost its actual loader-null global publication field.");
        var settings = ReadStaticSettings(image);
        var fov = Setting(FalloutPlayerWorldOrientationSource.ConstructorFovName, FalloutPlayerWorldOrientationSource.ConstructorFovDefaultBits);
        var near = Setting(FalloutWorldProjectionInputs.NearName, 0x40a00000);
        var minimum = Setting(FalloutWorldProjectionInputs.MinimumName, 0x44d48000);
        var maximum = Setting(FalloutWorldProjectionInputs.MaximumName, 0x46200000);
        var fraction = Setting(FalloutWorldProjectionInputs.FractionName, 0x3f800000);
        var forced = graphCtor + 0x31d;
        if (!Match(code, forced - 0x11, [0x6a, 0, 0x6a, 0, 0x6a, 1, 0x51, 0x8b, 0xce]) ||
            !Match(code, forced, [0xf3, 0x0f, 0x10, 5]) || Word(code, forced + 4) != fov ||
            !Match(code, forced + 8, [0xf3, 0x0f, 0x11, 4, 0x24, 0xe8]))
            throw new NotSupportedException("World ordinary constructor changed its forced effective FOV arguments or store order.");
        var setter = Call(forced + 0xd);
        RequireWorldProjectionSetter(code, image, setter, fov, near, camera);
        RequireOrdinaryWorldProjectionConstruction(code, image, graphCtor, camera, minimum, maximum, fraction, near);
        var playerCtor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x681, [0xc7, 0x87, 0x40, 6, 0, 0, 0, 0, 0, 0]), 0xa2f, "Player orientation constructor");
        if (!Match(code, playerCtor + 0x57a, [0xf3, 0x0f, 0x10, 5]) || Word(code, playerCtor + 0x57e) != fov ||
            !Match(code, playerCtor + 0x582, [0x6a, 0, 0x51, 0xf3, 0x0f, 0x11, 0x87, 0xbc, 5, 0, 0, 0x8b, 0x0d]) ||
            Word(code, playerCtor + 0x58f) != global || Call(playerCtor + 0x59d) != setter)
            throw new NotSupportedException("Player retained FOV lost its own constructor store or actual current World child.");
        var character = Call(playerCtor - 5); var actor = Call(character + 0xc);
        var mobile = Call(actor + 0x2b); var inherited = Call(mobile + 0xa);
        if (!Match(code, inherited + 0x55, [0x0f, 0x57, 0xc0]) ||
            !Match(code, inherited + 0x63, [0x0f, 0x11, 0x46, 0x1c]) ||
            !Match(image.Read(Word(image.Read(player + 0x100, 4), 0), 3), 0, [0xb0, 1, 0xc3]))
            throw new NotSupportedException("Player inherited reference lost distinct positive-zero raw rotations or actual Character predicate.");
        source.Validate(); return source;

        uint Table(string name, int count)
        {
            var matches = image.SourceClassTables(name, count).ToArray();
            return matches.Length == 1 ? matches[0] : throw new NotSupportedException("World orientation source class is absent or ambiguous: " + name);
        }
        uint Setting(string name, uint initial)
        {
            var matches = settings.Where(row => row.Name == name && row.Collection == SettingCollection.Main && row.Payload == initial).ToArray();
            return matches.Length == 1 && image.IsWritableExtent(matches[0].Descriptor + 4, 4) ? matches[0].Descriptor + 4 :
                throw new NotSupportedException("World orientation source has no exact typed Main clip/FOV descriptor: " + name);
        }
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("World orientation lost an actual linked constructor child.");
        int Unique(Func<int, bool> predicate, int tail, string name)
        {
            var matches = new List<int>();
            for (var at = 0x60; at <= code.Length - tail; at++) if (predicate(at)) matches.Add(at);
            return matches.Count == 1 ? matches[0] : throw new NotSupportedException("World orientation has no unique source " + name + ".");
        }
    }

    private static void RequireOrdinaryWorldProjectionConstruction(byte[] code, Image image, int root,
        uint cameraTable, uint minimum, uint maximum, uint fraction, uint near)
    {
        int Child(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("World ordinary construction lost an actual linked logical child.");
        if (!Match(code, root + 0x43, [0x6a, 0, 0x8b, 0xcf, 0xe8]) ||
            !Match(code, root + 0x108, [0x6a, 0, 0x8b, 0xc8, 0xe8]) || Child(root + 0x10c) != Child(root - 5) ||
            !Match(code, root + 0x1d2, [0x8b, 0xc8, 0xe8]))
            throw new NotSupportedException("World constructor changed its actual null-camera culling or same-source scene node allocation.");
        var camera = Child(root + 0x1d4);
        if (!Match(code, camera + 0x17, [0xc7, 7]) || Word(code, camera + 0x19) != cameraTable ||
            !Match(code, camera + 0x46, [0xc7, 0x87, 0xec, 0, 0, 0, 0, 0, 0x80, 0x3f]) ||
            !Match(code, camera + 0x50, [0xc7, 0x87, 0xf0, 0, 0, 0, 0, 0, 0, 0x40]) ||
            !Match(code, camera + 0x89, [0xc7, 0x87, 0xf8, 0, 0, 0, 0xcd, 0xcc, 0xcc, 0x3d]) ||
            !Match(code, camera + 0x93, [0xc7, 0x87, 0xfc, 0, 0, 0, 0, 0x40, 0x1c, 0x46]) ||
            code[camera + 0xe1] != 0xe8 || !Match(code, camera + 0xe6, [0x8b, 0xcf, 0xe8]))
            throw new NotSupportedException("World camera lost its actual generic constructor frustum, near limits or transform-before-matrix publication.");
        if (!Match(code, root + 0x271, [0x0f, 0x28, 5]) ||
            !image.Read(Word(code, root + 0x274), 16).AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0x80, 0x3f, 0, 0, 0x80, 0x3f, 0, 0, 0, 0 }) ||
            !Match(code, root + 0x2b6, [0xc7, 0x80, 0xf8, 0, 0, 0, 0, 0, 0x80, 0x3f]) ||
            !Match(code, root + 0x2d3, [0xf3, 0x0f, 0x10, 0x15]) || Word(code, root + 0x2d7) != maximum ||
            !Match(code, root + 0x2db, [0x0f, 0x57, 0xdb, 0xf3, 0x0f, 0x10, 5]) || Word(code, root + 0x2e2) != fraction ||
            !Match(code, root + 0x2e6, [0xf3, 0x0f, 0x5c, 0x15]) || Word(code, root + 0x2ea) != minimum ||
            !Match(code, root + 0x2ee, [0xf3, 0x0f, 0x5c, 0xc3, 0xf3, 0x0f, 0x59, 0xd0, 0xf3, 0x0f, 0x58, 0x15]) ||
            Word(code, root + 0x2fa) != minimum || !Match(code, root + 0x2fe, [0xf3, 0x0f, 0x5e, 0x15]) || Word(code, root + 0x302) != near ||
            !Match(code, root + 0x315, [0xf3, 0x0f, 0x11, 0x90, 0xfc, 0, 0, 0]) ||
            !Match(code, root + 0x368, [0xff, 0x90, 0xdc, 0, 0, 0]) || code[root + 0x396] != 0xe8 ||
            !Match(code, root + 0x39b, [0x81, 0x4e, 0x30, 0, 8, 0, 0]) ||
            !Match(code, root + 0x3e7, [0xc7, 0x40, 0x0c, 0, 0, 0x80, 0x3f]) ||
            !Match(code, root + 0x3f0, [0x81, 0x4e, 0x30, 0, 0x20, 0, 0]) || !Match(code, root + 0x411, [0xc2, 0x0c, 0]))
            throw new NotSupportedException("World ordinary camera changed its exact viewport, Float32 clip setup, post-FOV attachment/update or root bound return.");
    }

    private static void RequireWorldProjectionSetter(byte[] code, Image image, int setter, uint fov, uint near, uint camera)
    {
        if (!Match(code, setter, [0x83, 0xec, 0x38, 0x8b, 0x44, 0x24, 0x44, 0xf3, 0x0f, 0x10, 0x4c, 0x24, 0x3c]) ||
            !Match(code, setter + 0x17, [0x85, 0xc0, 0x75, 0x48, 0x8b, 0xbe, 0xac, 0, 0, 0, 0x38, 0x44, 0x24, 0x48, 0x75, 0x34]) ||
            !Match(code, setter + 0x27, [0xf3, 0x0f, 0x10, 0x86, 0xbc, 0, 0, 0, 0x0f, 0x2e, 0xc1, 0x9f, 0xf6, 0xc4, 0x44, 0x7a, 0x23]) ||
            Word(code, setter + 0x47) != fov || !Match(code, setter + 0x5b, [0xf3, 0x0f, 0x11, 0x8e, 0xbc, 0, 0, 0]) ||
            Word(image.Read(Word(code, setter + 0x6e), 4), 0) != 0x3c8efa35 ||
            Word(image.Read(Word(code, setter + 0x78), 4), 0) != 0x3f000000 ||
            !Match(code, setter + 0x87, [0x8b, 6, 0x8b, 0xce, 0x8b, 0x80, 0, 1, 0, 0, 0xff, 0xd0]))
            throw new NotSupportedException("World setter changed ordered equality, force, camera selection, FOV store or source angle stores.");
        if (code[setter + 0x82] != 0xe8 ||
            !Match(code, checked(setter + 0x87 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(setter + 0x83))),
                [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x41, 0x18, 0x8b, 0xc1, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("World setter lost its temporary frustum constructor.");
        if (!Match(code, setter + 0x93, [0xf3, 0x0f, 0x10, 0x0d]) || Word(code, setter + 0x97) != near ||
            !Match(code, setter + 0x281, [0xf3, 0x0f, 0x10, 0x0d]) || Word(image.Read(Word(code, setter + 0x285), 4), 0) != 0x3f400000 ||
            !Match(code, setter + 0x2fe, [0x8d, 0x44, 0x24, 0x24]) || !Match(code, setter + 0x30a, [0x50, 0x8b, 0xcf]) || code[setter + 0x31a] != 0xe8)
            throw new NotSupportedException("World setter changed its exact near/aspect source fields or actual camera update child.");
        var update = checked(setter + 0x31f + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(setter + 0x31b)));
        if (!Match(code, update, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x6a, 0, 0xff, 0x75, 8, 0x8b, 6, 0xff, 0x90, 0xa4, 0, 0, 0]))
            throw new NotSupportedException("Camera update lost its actual false controller-update context.");
        var data = SourceOffset(code, image.CodeBase, Word(image.Read(camera + 0xb8, 4), 0));
        if (!Match(code, data, [0x55, 0x8b, 0xec, 0x56, 0xff, 0x75, 8, 0x8b, 0xf1, 0xe8]) ||
            !Match(code, data + 0xe, [0x8b, 0xce, 0xe8]) || !Match(code, data + 0x15, [0x5e, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("Camera source-world update changed transform-before-matrix child order.");
    }
}
