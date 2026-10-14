using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock EarlyControllerQualificationLock = new();
    private static string? _qualifiedEarlyController;
    internal static FalloutEarlyControllerSource ReadEarlyController(string path,
        FalloutPackageCommandFieldsSource fields, FalloutPlayerThreadContextSource thread)
    {
        var source = FalloutEarlyControllerSource.Reviewed(fields, thread); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Early controller changed its selected original source image.");
        lock (EarlyControllerQualificationLock)
        {
            if (_qualifiedEarlyController == source.Identity) return source;
            Qualify();
            _qualifiedEarlyController = source.Identity;
            return source;
        }

        void Qualify()
        {
            _ = ReadPackageCommandFields(path, fields.Controls);
            var (code, image) = Load(original);
            var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
                throw new NotSupportedException("Early controller lost its actual selected Player class.");
            var constructor = Enumerable.Range(0, code.Length - 0xa63 + 1).Where(at => Match(code, at, [0xc7, 7]) &&
                Word(code, at + 2) == player && Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0])).ToArray() is [var start] ? start :
                throw new NotSupportedException("Early controller lost its unique actual Player constructor.");
            var helper = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c4, 4), 0));
            if (Call(constructor + 0x4a0) != helper)
                throw new NotSupportedException("Early helper no longer enters from the actual Player constructor.");
            Need(constructor + 0x49a, [0x89, 0x87, 0xcc, 1, 0, 0], "Player constructor pointer field before early helper");
            Need(helper + 0x4a, [0x8b, 0x57, 0x60, 0x89, 0x55, 0xdc, 0x85, 0xd2, 0x0f, 0x84], "actual current process guard");
            Need(helper + 0x241, [0x8b, 0x80, 0x88, 2, 0, 0, 0xff, 0xd0], "same Common controller getter before independent factory");
            var descriptor = Call(helper + 0x25b); var body = Call(helper + 0x33e);
            var initialize = Call(body + 0x401); var standard = Call(initialize + 0xb3);
            Need(helper + 0x289, [0xc7, 0x85, 0x78, 0xff, 0xff, 0xff, 0, 0, 0, 0], "explicit requested-null world store");
            Need(helper + 0x387, [0x8c, 2, 0, 0, 0x57, 0x68, 0xe8, 3, 0, 0], "Common publication before actual Actor property");
            if (Call(helper + 0x300) != Call(helper + 0x39a))
                throw new NotSupportedException("Early scale consumers changed their common current reference/Base getter.");
            var scale = Call(helper + 0x39a);
            Need(scale + 4, [0xf3, 0x0f, 0x10, 0x41, 0x38, 0x8b, 0x49, 0x1c], "reference scale before current nullable Base read");
            Need(scale + 0x11, [0x85, 0xc9, 0x74, 0x2f, 0x0f, 0xb6, 0x41, 4, 0x83, 0xe8, 0x2a], "null Base and independent NPC/CREA dispatch");
            Need(scale + 0x23, [0xf3, 0x0f, 0x59, 0x81, 0x3c, 1, 0, 0], "current CREA height multiplier");
            Need(scale + 0x37, [0xf3, 0x0f, 0x59, 0x81, 0xc4, 1, 0, 0], "current NPC height multiplier");
            Need(helper + 0x3a5, [0x8b, 0x8e, 0x94, 2, 0, 0, 0x8b, 0xc1, 0x0d, 0, 0, 0x10, 0,
            0x81, 0xe1, 0xff, 0xff, 0xef, 0xff, 0x80, 0x3d], "conditional source helper flag read");
            var helperFlag = Word(code, helper + 0x3ba);
            if (!image.IsWritableExtent(helperFlag, 1) || image.ReadLoaderData(helperFlag, 1)[0] != FalloutEarlyControllerSource.InitialHelperFlag)
                throw new NotSupportedException("Early collision helper flag lost its independent loader-zero declaration.");
            Need(helper + 0x3bf, [0x0f, 0x45, 0xc8, 0x8b, 0x45, 0xdc, 0x81, 0xc9, 0, 0, 0x20, 0], "unconditional source helper flag store");

            // These are declarations and the exact entered constructor reads. They
            // do not admit a later script/console/load writer into current fields.
            var center = Word(code, standard + 0x170);
            Need(standard + 0x16c, [0xf3, 0x0f, 0x7e, 0x0d], "standard center source XY read");
            Need(standard + 0x244, [0xa1], "separate standard center source Z read");
            if (Word(code, standard + 0x1e1) != center || Word(code, standard + 0x211) != center ||
                Word(code, standard + 0x245) != center + 8 || Word(code, constructor + 0x288) != center ||
                !image.IsWritableExtent(center, 12) || image.ReadLoaderData(center, 12).Any(value => value != 0))
                throw new NotSupportedException("Standard center lost its actual shared source loader vector.");
            var character = Call(constructor - 5); var actor = Call(character + 12);
            var mobile = Call(actor + 0x2b); var reference = Call(mobile + 0xa);
            Need(reference + 0x58, [0xc7, 0x46, 0x5c, 0, 0, 0, 0], "actual model-record constructor null writer");
            Need(reference + 0x52, [0x8b, 0x46, 0x1c], "capture preexisting Base field before vector clear");
            Need(reference + 0x77, [0x89, 0x46, 0x1c], "restore captured Base field without assuming an allocator value");
            Need(reference + 0x7d, [0xc7, 0x46, 0x38, 0, 0, 0x80, 0x3f], "independent reference scale positive-one constructor");
            Need(reference + 0x16e, [0xc7, 0x46, 0x5c, 0, 0, 0, 0], "final reference model-record null after independent cleanup");
            Need(reference + 0x175, [0xc7, 0x46, 0x1c, 0, 0, 0, 0], "final reference Base-null store after intermediate preservation");
            Need(reference + 0x17e, [0xc7, 0x46, 0x3c, 0, 0, 0, 0], "final independent ParentCELL-null store");
            Need(reference + 0x195, [0xc3], "returned complete inherited reference constructor field tail");
            Need(SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x2e4, 4), 0)), [0xd9, 0xe8, 0xc3],
                "actual Player shape scalar virtual positive one");
            Need(SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x100, 4), 0)), [0xb0, 1, 0xc3],
                "actual Actor-true selected Player primitive predicate");
            Need(SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x21c, 4), 0)), [0x32, 0xc0, 0xc3],
                "selected Player false base-collision predicate independent of current Base identity");
            RequireDescriptor(); RequireManager(); RequireThreadAndModel(); RequireContact();
            RequirePlayerControllerContext(code, image);
            return;

            void RequireDescriptor()
            {
                foreach (var (at, offset, value) in new (int, byte, uint)[] {
                         (0x62, 0x20, FalloutEarlyControllerSource.PositiveOneBits), (0x69, 0x24, 0),
                         (0x70, 0x28, FalloutPhysicalConstructorInputs.ContactToleranceBits),
                         (0xa8, 0x4c, FalloutPhysicalConstructorInputs.KeepDistanceBits),
                         (0xc4, 0x5c, FalloutEarlyControllerSource.MaximumBoundBits),
                         (0xd2, 0x64, FalloutEarlyControllerSource.MaximumSlopeBits),
                         (0xd9, 0x68, FalloutEarlyControllerSource.PositiveOneBits), (0xe0, 0x6c, FalloutEarlyControllerSource.UserPlanes),
                         (0x21b, 0x50, 0), (0x222, 0x48, 0), (0x230, 0x20, FalloutEarlyControllerSource.PositiveOneBits),
                         (0x245, 0x54, FalloutEarlyControllerSource.ContactIterations), (0x24c, 0x58, FalloutEarlyControllerSource.StrengthBits) })
                {
                    Need(descriptor + at, [0xc7, 0x46, offset], "creation-info field writer");
                    if (Word(code, descriptor + at + 3) != value) throw new NotSupportedException("Early creation-info source value changed.");
                }
                Need(descriptor + 0x1a4, [0xc1, 0xe0, 0x10, 0x83, 0xc8, 0x1e], "allocated source upper group and lower layer thirty");
                Need(descriptor + 0x112, [0xa1], "independent shared collision-group counter source read");
                var counter = Word(code, descriptor + 0x113);
                if (Word(image.ReadLoaderData(counter, 4), 0) != FalloutCharacterCollisionGroups.InitialCounter)
                    throw new NotSupportedException("Shared collision-group counter lost its actual source loader declaration.");
                Need(body + 0x40, [0xe8], "real inherited controller constructor");
                Need(body + 0x27e, [0xc7, 0x86, 0x94, 2, 0, 0, 0, 0, 0, 0], "actual helper flag constructor clear");
            }
            void RequireManager()
            {
                var manager = Call(descriptor + 0x291);
                Need(manager + 0xf, [0x0f, 0x57, 0xc0, 0x0f, 0x11, 0x41, 8], "real manager eleven-null-slot constructor");
                Need(manager + 0x18, [0x0f, 0x11, 0x41, 0x18, 0x66, 0x0f, 0xd6, 0x41, 0x28,
                0xc7, 0x41, 0x30, 0, 0, 0, 0], "complete manager slot initialization extent");
                var registration = Call(descriptor + 0x2d8);
                foreach (var (at, kind) in new (int, byte)[] { (0x2d1, 0), (0x325, 5), (0x379, 1), (0x3cd, 2), (0x404, 3), (0x44a, 4), (0x4ad, 6) })
                {
                    Need(descriptor + at, [0x6a, kind, 0x57], "source-ordered real state registration");
                    var call = at is 0x404 or 0x44a ? at + 0x16 : at + 7;
                    if (Call(descriptor + call) != registration)
                        throw new NotSupportedException("Shared character state registration changed its actual retained manager.");
                }
                var state0 = Call(descriptor + 0x2c4); var state2 = Call(descriptor + 0x3c0);
                Need(state0 + 0xd, [0x88, 0x41, 8], "state zero independent enabled byte");
                Need(state2 + 0x15, [0xc7, 0x41, 8, 0, 0, 0xfa, 0x44], "state two independent scalar constructor");
                var context = Call(body + 0x87);
                Need(context + 0xf, [0x8b, 0x45, 0xc, 0x89, 0x41, 0x10, 0x89, 0x41, 0x14], "context current and previous from the same actual state operand");
                Need(context + 0x2a, [0xc6, 0x41, 0x18, 1], "context enabled constructor byte");
                if (Word(code, context + 0x31) != FalloutEarlyControllerSource.ContextScalarBBits ||
                    Word(code, context + 0x38) != FalloutEarlyControllerSource.ContextScalarABits ||
                    Word(code, context + 0x3f) != FalloutEarlyControllerSource.PositiveOneBits)
                    throw new NotSupportedException("Actual state context changed its independent constructor scalars.");
            }
            void RequireThreadAndModel()
            {
                Need(initialize + 0x52, [0x8a, 0x86, 0xa4, 2, 0, 0, 0x84, 0xc0, 0x75], "actual initialized-byte predicate before prior kind");
                Need(initialize + 0x67, [0x8b, 0xb6, 0x80, 2, 0, 0, 0x89, 0x75, 0xe0], "capture actual prior physics kind");
                Need(initialize + 0x7c, [0xc7, 0x80, 0x80, 2, 0, 0, 0x10, 0, 0, 0], "source physics kind sixteen store");
                Need(initialize + 0x331, [0x89, 0xb7, 0x80, 2, 0, 0], "successful child restores actual prior kind");
                var initializer = Call(initialize + 0x5c);
                if (initializer != Call(initialize + 0x74) || initializer != Call(initialize + 0x32c))
                    throw new NotSupportedException("Physics initialization changed its actual source-thread initializer.");
                var model = Call(helper + 0xfa);
                Need(model + 0x13, [0x80, 0xbe, 0xa4, 2, 0, 0, 0, 0x75], "same-thread model initialized predicate");
                Need(model + 0x21, [0x3b, 0xbe, 0x4c, 2, 0, 0, 0x75], "actual model cached-reference selection");
                Need(model + 0x37, [0x8b, 0x86, 0x48, 2, 0, 0], "actual independent cached model field");
                Need(model + 0x40, [0x8b, 0x47, 0x5c, 0x85, 0xc0, 0x74, 6, 0x8b, 0x40, 0x14], "actual nullable reference model fallback");
                if (Call(model + 0x1c) != initializer || Call(model + 0x32) != initializer)
                    throw new NotSupportedException("Early model getter changed its current-thread initialization owner.");
                var index = Word(code, initialize + 0x48);
                if (Word(code, model + 2) != index)
                    throw new NotSupportedException("Controller/model scopes borrowed different actual source TLS indices.");
                var loaded = image.PlayerWorkingContextLoaderCells(index);
                if (loaded.Kind != thread.InitialKind || loaded.Initialized != thread.InitialInitialized ||
                    loaded.Model != thread.InitialModel || loaded.Reference != thread.InitialReference)
                    throw new NotSupportedException("Physics source current-thread loader fields changed.");
            }
            void RequireContact()
            {
                Need(body + 0xb0, [0xf3, 0x0f, 0x10, 5], "actual shared contact slope read");
                var slope = Word(code, body + 0xb4);
                if (!image.IsWritableExtent(slope, 4) || Word(image.ReadLoaderData(slope, 4), 0) != FalloutEarlyControllerSource.InitialSlopeBits ||
                    Word(image.Read(Word(code, body + 0xbc), 4), 0) != FalloutEarlyControllerSource.DegreesToRadiansBits)
                    throw new NotSupportedException("Contact angle lost its actual loaded scalar or separate degrees conversion.");
                var contact = Call(body + 0xd6);
                Need(contact + 0x37, [0xf2, 0x0f, 0x5a, 0xc0], "cosine return separately stored to Float32");
                Need(contact + 0xba, [0xf2, 0x0f, 0x5a, 0xc0, 0xf3, 0x0f, 0x5e, 0xc8,
                0xf3, 0x0f, 0x59, 0x4e, 0x20, 0xf3, 0x0f, 0x11, 0x4e, 0x64], "independent sine Float32 then separate division and multiplication");
                if (Call(contact + 0x32) == Call(contact + 0xae))
                    throw new NotSupportedException("Contact sine/cosine producers lost their distinct actual source children.");
            }
            int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
                checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Early controller lost an actual selected source child.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Early controller changed its " + role + "."); }
        }
    }
}
