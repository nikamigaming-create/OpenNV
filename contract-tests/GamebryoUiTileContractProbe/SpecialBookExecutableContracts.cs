using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class SpecialBookExecutableContracts
{
    internal static void Run()
    {
        foreach (var origin in new uint[] { 0x200000, 0x760000 })
        {
            var source = new Fixture(origin);
            var book = source.Read();
            Require(book.DefaultBudget == 47 && book.ModelScale == .125f && book.Depth == -47 && book.RotationRadians == .37f,
                "Optimized source pose or inline budget was replaced with another executable's declarations.");
            Require(book.LightIntensity == .6f && book.LightRadiusMultiple == 17 && book.ReferenceSlope == .625f &&
                book.FieldOfViewMultiplier == .27f && book.SinglePrecisionProjection, "Optimized source light/projection operands were replaced.");
            var angle = 83f * (float)(Math.PI / 180) * .27f;
            Require(BitConverter.SingleToInt32Bits(book.HorizontalSlope(83)) ==
                BitConverter.SingleToInt32Bits((float)Math.Tan(angle) * .625f), "Source Float32 multiplication order was lost before tangent.");
            for (var page = 1; page <= 9; page++) Require(book.Transition(page, 1) == source.Forward[page - 1], "Optimized forward table was lost.");
            for (var page = 0; page < 9; page++) Require(book.Transition(page, -1) == source.Backward[page], "Optimized reverse table was lost.");

            void AlterCode(Action<Fixture> change)
            {
                var changed = new Fixture(origin); change(changed); Reject(() => changed.Read());
            }
            AlterCode(changed => changed.Code[changed.DepthRead]--);
            AlterCode(changed => changed.Code[changed.AngleRead]--);
            AlterCode(changed => changed.Code[changed.BlueRead]--);
            AlterCode(changed => changed.Code[changed.AspectRead]--);
            AlterCode(changed => changed.Code[changed.FinalFrustumField]--);
            AlterCode(changed => changed.Code[changed.MatrixField] = 0x58);
            AlterCode(changed => changed.Code[changed.DefaultTypeId] = 1);
            AlterCode(changed => changed.Write(changed.ModelCall, 0));
            AlterCode(changed => changed.Write(changed.CameraCall, 0));
            AlterCode(changed => changed.Write(changed.DefaultOwner, origin + 0x900));
            AlterCode(changed => changed.Code.RemoveRange(changed.CameraStart, changed.Code.Count - changed.CameraStart));
            var mask = new Fixture(origin); mask.Memory[mask.ScaleMask] = new byte[16]; Reject(() => mask.Read());
            mask = new Fixture(origin); mask.Memory[mask.SignMask] = new byte[16]; Reject(() => mask.Read());
            var nonfinite = new Fixture(origin); nonfinite.Memory[nonfinite.Scale] = BitConverter.GetBytes(float.NaN); Reject(() => nonfinite.Read());
            var unequal = new Fixture(origin); unequal.Memory[unequal.Light] = BitConverter.GetBytes(.7f); Reject(() => unequal.Read());
            var missing = new Fixture(origin); missing.Memory.Remove(missing.Table); Reject(() => missing.Read());
            var duplicate = new Fixture(origin, duplicatePose: true); Reject(() => duplicate.Read());
        }
        Console.WriteLine("PASS relocated optimized SPECIAL transform/axis/light/frustum/factory associations, source Float32 order, all paired transitions and drift/ambiguity rejection.");
    }

    internal static void Owned(string[] arguments)
    {
        if (arguments.Length == 0) return;
        if (arguments.Length is < 3 or > 4 || arguments[0] != "--audit-special-book")
            throw new ArgumentException("SPECIAL book declaration audit: --audit-special-book <owned-root> <fallout-3|fallout-new-vegas> [launcher-mod-stack-list.json].");
        using var source = arguments.Length == 4
            ? (FalloutModStackSelection.ReadOptions(new Dictionary<string, string>
            {
                ["mod-stack"] = File.ReadAllText(arguments[3]),
            }) ?? throw new InvalidDataException("Selected source mod stack is absent.")).Resolve(arguments[1]).OpenSource()
            : RuntimeLiveContentSource.Open(arguments[1], arguments[2]);
        Require(source.Game == arguments[2], "Selected declaration audit does not match its owned game.");
        const string modelPath = "meshes/terminals/babybook02.nif";
        const string menuPath = "menus/chargen/specialbookmenu.xml";
        Require(source.TryRead(modelPath, null, out var modelBytes, out var modelIdentity), "Owned book model is absent.");
        Require(source.TryRead(menuPath, null, out var menuBytes, out var menuIdentity), "Owned book XML is absent.");
        var nif = FalloutNifFile.Read(modelBytes);
        var names = nif.Blocks.Where(block => block.TypeName == "NiControllerSequence")
            .Select(block => ((FalloutNifControllerSequence)nif.ReadObject(block.Index)).Name).ToArray();
        var book = FalloutExecutableStringTable.ReadSpecialBook(source.FalloutExecutablePath, names);
        Require(book.AnimatedModel.Replace('\\', '/').Equals(modelPath, StringComparison.OrdinalIgnoreCase),
            "Owned executable and winning book model disagree.");
        Require(book.ForwardSequences.Concat(book.BackwardSequences).All(names.Contains) && book.LastPage == 9,
            "Owned executable transition tables do not bind the winning source NIF.");
        var textures = book.Textures ?? throw new InvalidDataException("Owned book texture declarations are absent.");
        var textureSources = textures.Paths.Select(path =>
        {
            Require(source.TryRead(path, null, out var bytes, out var identity), "Owned declared book texture is absent: " + path);
            return new { path, identity, bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }).ToArray();
        Console.WriteLine("OPENNV_OWNED_SPECIAL_BOOK_DECLARATIONS_PASS " + JsonSerializer.Serialize(new
        {
            source.Game,
            source.StackId,
            executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.FalloutExecutablePath))),
            modelIdentity,
            modelSha256 = Convert.ToHexString(SHA256.HashData(modelBytes)),
            menuIdentity,
            menuSha256 = Convert.ToHexString(SHA256.HashData(menuBytes)),
            book.DefaultBudget,
            book.ModelScale,
            book.Depth,
            book.RotationRadians,
            book.ReferenceSlope,
            book.FieldOfViewMultiplier,
            book.RadiansMultiplier,
            book.SinglePrecisionProjection,
            book.LightIntensity,
            book.LightRadiusMultiple,
            book.ForwardSequences,
            book.BackwardSequences,
            textureSources,
            textureBranch = "PC keyboard/pointer;source input-mode switching remains separate",
            boundary = "selected-owned-declarations-and-winning-NIF;native-input-campaign-and-retail-parity-unverified",
        }));
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("An unbound optimized SPECIAL declaration was admitted.");
    }

    // Independent synthetic instruction emitter. Data pointers, frame slots,
    // values and relative callees vary independently of the retail corpus.
    private sealed class Fixture
    {
        internal readonly List<byte> Code = [];
        internal readonly Dictionary<uint, byte[]> Memory = [];
        private readonly Dictionary<uint, string> _strings = [];
        internal readonly string[] Forward = Enumerable.Range(1, 9).Select(page => "SyntheticForward" + page).ToArray();
        internal readonly string[] Backward = Enumerable.Range(0, 9).Select(page => "SyntheticBackward" + page).ToArray();
        internal uint Scale, ScaleMask, SignMask, Light, Table;
        internal int DepthRead, AngleRead, BlueRead, AspectRead, FinalFrustumField, MatrixField, DefaultTypeId,
            ModelCall, CameraCall, DefaultOwner, CameraStart;
        private uint _cursor;

        internal Fixture(uint origin, bool duplicatePose = false)
        {
            _cursor = origin + 0x1000; var owner = origin + 0x100;
            Scale = Scalar(.125f); ScaleMask = Data(Enumerable.Repeat(0x7fffffffU, 4).SelectMany(BitConverter.GetBytes).ToArray());
            SignMask = Data(Enumerable.Repeat(0x80000000U, 4).SelectMany(BitConverter.GetBytes).ToArray()); Light = Scalar(.6f);
            var calls = new List<(int Model, int Camera)>();
            void Factory(bool literal)
            {
                Bytes(0x55, 0x8b, 0xec); Push("Data/Menus/CharGen/SPECIALBookMenu.xml");
                Bytes(0x8b, 0x89, 0x9c, 0, 0, 0); Call(); Bytes(0x8b, 0xf8, 0x8b, 0xcf); Call();
                Bytes(0x8b, 0xf0, 0x85, 0xf6, 0x0f, 0x84); Dword(0);
                Bytes(0x8b, 0x16, 0x8b, 0xce, 0xff, 0x52, 0x34, 0x3d);
                if (literal) DefaultTypeId = Code.Count;
                Dword(FalloutSpecialBookPresentation.MenuId); Bytes(0x0f, 0x85); Dword(0);
                Bytes(0x89, 0x35); Dword(owner);
                if (literal) { Bytes(0xc7, 0x46, 0x58); Dword(47); }
                else Bytes(0x8b, 0x45, 8, 0x89, 0x46, 0x58);
                Bytes(0x8b, 0x0d); if (literal) DefaultOwner = Code.Count; Dword(owner); var modelCall = Call();
                Bytes(0x8b, 0x0d); Dword(owner); var cameraCall = Call(); Bytes(0xc3, 0xcc);
                if (literal) { ModelCall = modelCall; CameraCall = cameraCall; }
                calls.Add((modelCall, cameraCall));
            }
            Factory(false); Factory(true);
            var modelStart = Code.Count;
            Bytes(0x53, 0x8b, 0xdc, 0x83, 0xec, 8, 0x83, 0xe4, 0xf0, 0x83, 0xc4, 4,
                0x55, 0x8b, 0x6b, 4, 0x89, 0x6c, 0x24, 4, 0x8b, 0xec, 0x6a, 0xff);
            Bytes(0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0); Push("Meshes/Terminals/Babybook02.NIF"); Call();
            void Pose()
            {
                Bytes(0x8b, 0x0d); Dword(owner); Bytes(0x89, 0x41, 0x6c, 0xa1); Dword(owner); Bytes(0xff, 0x70, 0x6c); Call();
                Bytes(0xa1); Dword(owner); Vector(0x57, 6, 6);
                Load(0, Scale); Bytes(0x83, 0xc4, 4, 0x0f, 0x54, 5); Dword(ScaleMask);
                Local(0xd0, -47); Bytes(0x8b, 0x40, 0x6c); Local(0xc4, .37f); Store(0, 0, 0x64);
                Vector(0x57, 0, 0); Bytes(0xa1); Dword(owner); Vector(0x14, 0, 6); Bytes(0x8b, 0x48, 0x6c, 0x8b, 0x45);
                DepthRead = Code.Count; Bytes(0xd0, 0x66, 0x0f, 0xd6, 0x41, 0x58, 0x89, 0x41, 0x60, 0xa1);
                Dword(owner); Bytes(0x8b, 0x40, 0x6c, 0xd9, 0x45); AngleRead = Code.Count; Bytes(0xc4, 0xd9, 0xfb, 0xd9, 0x5d, 0xa4, 0xd9, 0x5d, 0xa0);
                Load(2, Scalar(1)); LoadLocal(3, 0xa4); LoadLocal(4, 0xa0); Arithmetic(0x5c, 2, 3);
                Vector(0x28, 0, 3); Vector(0x28, 1, 4); Arithmetic(0x59, 1, 6); Arithmetic(0x58, 0, 2);
                Vector(0x28, 5, 2); Arithmetic(0x59, 5, 6); Store(0, 0, 0x34); MatrixField = Code.Count - 1;
                Vector(0x28, 2, 5); Vector(0x28, 0, 5); Arithmetic(0x58, 2, 1); Arithmetic(0x5c, 0, 1);
                Vector(0x28, 1, 5); Arithmetic(0x58, 1, 3); Store(2, 0, 0x38); Store(0, 0, 0x3c); Store(0, 0, 0x40);
                Vector(0x28, 0, 4); Arithmetic(0x58, 0, 5); Store(1, 0, 0x44); Arithmetic(0x5c, 5, 4);
                Store(2, 0, 0x4c); Store(1, 0, 0x54); Store(0, 0, 0x48); Store(5, 0, 0x50);
            }
            Pose(); if (duplicatePose) Pose(); Push(Forward[0]);
            Bytes(0xf3, 0x0f, 0x10, 0x42, 0x0c, 0xf3, 0x0f, 0x59, 5); Dword(Scalar(17));
            Bytes(0x8b, 0x40, 0x0c, 0x0f, 0x29, 0x45, 0x90, 0xf3, 0x0f, 0x11, 4, 0x24, 0xff, 0xd0);
            Load(0, Light); Vector(0x14, 0, 0); Bytes(0x66, 0x0f, 0xd6, 0x86); Dword(0xd4);
            Bytes(0x0f, 0x28, 0x45, 0x90, 0x89, 0x46, 0x60); Local(0xb0, .6f); Bytes(0x8b, 0x45);
            BlueRead = Code.Count; Bytes(0xb0, 0x89, 0x86); Dword(0xdc); Vector(0x14, 0, 1); Bytes(0x66, 0x0f, 0xd6, 0x86); Dword(0xe0);
            Push("LookInside_Btn:0");
            CameraStart = Code.Count; Bytes(0x55, 0x8b, 0xec, 0x6a, 0xff);
            Load(0, Scalar(83)); Multiply(0, Scalar((float)(Math.PI / 180))); Store(1, 5, 0x9c); Multiply(0, Scalar(.27f));
            Vector(0x5a, 0, 0); Call(); Load(1, Scalar(.625f)); Bytes(0x8d, 0x45, 0x8c); Vector(0x57, 3, 3);
            Bytes(0xc6, 0x45, 0x88, 0, 0xf2, 0x0f, 0x5a, 0xd8, 0x50, 0x8b, 0xce); Vector(0x28, 2, 3);
            Bytes(0x0f, 0x57, 0x15); Dword(SignMask); Vector(0x28, 0, 2);
            MultiplyLocal(2, 0x9c); AspectRead = Code.Count - 1;
            Arithmetic(0x59, 0, 1); Arithmetic(0x59, 2, 1); Store(0, 5, 0x8c); Vector(0x28, 0, 3);
            MultiplyLocal(3, 0x9c); Arithmetic(0x59, 0, 1); Arithmetic(0x59, 3, 1);
            Store(0, 5, 0x90); Store(2, 5, 0x98); Store(3, 5, 0x94); FinalFrustumField = Code.Count - 1; Call();
            Push("Surgery3DCamera");
            foreach (var call in calls) { Write(call.Model, unchecked((uint)(modelStart - call.Model - 4))); Write(call.Camera, unchecked((uint)(CameraStart - call.Camera - 4))); }
            Table = origin + 0x8000;
            var names = Backward.Concat(Forward).ToArray();
            for (var index = 0; index < names.Length; index++) Memory.Add(Table + (uint)index * 4, BitConverter.GetBytes(Literal(names[index])));
            Memory.Add(Table + (uint)names.Length * 4, new byte[4]);
            Bytes(0x8b, 0x14, 0x8d); Dword(Table); Bytes(0x8b, 0x0c, 0x85); Dword(Table + 32); Bytes(0xc3, 0xcc);
        }

        internal FalloutSpecialBookPresentation Read() => FalloutExecutableStringTable.ReadSpecialBookDeclarations(Code.ToArray(),
            address => _strings.GetValueOrDefault(address),
            (address, count) => Memory.TryGetValue(address, out var bytes) && bytes.Length == count ? bytes :
                throw new InvalidDataException("Synthetic owned declaration extent is absent."), Forward.Concat(Backward).ToArray());
        internal void Write(int at, uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            for (var index = 0; index < bytes.Length; index++) Code[at + index] = bytes[index];
        }
        private void Bytes(params byte[] bytes) => Code.AddRange(bytes);
        private void Dword(uint value) => Code.AddRange(BitConverter.GetBytes(value));
        private int Call() { Bytes(0xe8); var at = Code.Count; Dword(0); return at; }
        private uint Data(byte[] value) { var at = _cursor; _cursor += 32; Memory.Add(at, value); return at; }
        private uint Scalar(float value) => Data(BitConverter.GetBytes(value));
        private uint Literal(string value) { var at = _cursor; _cursor += 32; _strings.Add(at, value); return at; }
        private void Push(string value) { Bytes(0x68); Dword(Literal(value)); }
        private void Local(byte slot, float value) { Bytes(0xc7, 0x45, slot); Dword(unchecked((uint)BitConverter.SingleToInt32Bits(value))); }
        private void Load(byte register, uint address) { Bytes(0xf3, 0x0f, 0x10, (byte)(5 + register * 8)); Dword(address); }
        private void LoadLocal(byte register, byte slot) => Bytes(0xf3, 0x0f, 0x10, (byte)(0x45 + register * 8), slot);
        private void Store(byte register, byte receiver, byte field) => Bytes(0xf3, 0x0f, 0x11, (byte)(0x40 + register * 8 + receiver), field);
        private void Multiply(byte register, uint address) { Bytes(0xf3, 0x0f, 0x59, (byte)(5 + register * 8)); Dword(address); }
        private void MultiplyLocal(byte register, byte slot) => Bytes(0xf3, 0x0f, 0x59, (byte)(0x45 + register * 8), slot);
        private void Vector(byte operation, byte target, byte operand) => Bytes(0x0f, operation, (byte)(0xc0 + target * 8 + operand));
        private void Arithmetic(byte operation, byte target, byte operand) => Bytes(0xf3, 0x0f, operation, (byte)(0xc0 + target * 8 + operand));
    }
}
