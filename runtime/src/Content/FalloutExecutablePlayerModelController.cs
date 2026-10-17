using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static void AuditPlayerModelControllerPrefix(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() !=
            World.Physics.FalloutPhysicalConstructorInputs.ReviewedEngineSha256)
            throw new NotSupportedException("Controller model audit has no reviewed selected source image.");
        var (code, image) = Load(bytes);
        var tables = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (tables is not [var player]) throw new InvalidDataException("Controller source audit lost its actual Player table.");
        var load = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c8, 4), 0));
        RequirePlayerModelControllerPrefix(code, image, player, load);
    }
    private static void RequirePlayerModelControllerPrefix(byte[] code, Image image, uint player, int load)
    {
        Need(load + 0x580, [0x8b, 7, 0x8b, 0xcf, 0xff, 0x90, 0xc4, 1, 0, 0], "actual following Player controller virtual");
        var helper = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c4, 4), 0));
        Need(helper + 0x43, [0x6a, 0, 0xe8], "reference Extra service false operand before process guard");
        var extra = Call(helper + 0x45);
        Need(extra + 0x13, [0x6a, 0x7e], "actual auxiliary Extra subtype");
        Need(extra + 0x20, [0x80, 0x7d, 8, 0], "independent false service branch");
        Need(extra + 0x2f, [0x85, 0xdb, 0x0f, 0x84], "absent auxiliary Extra skips removal children");
        Need(extra + 0x138, [0x8b, 0x47, 0x3c, 0x85, 0xc0, 0x74, 0x2d], "independent nullable ParentCELL service tail");
        Need(helper + 0x58, [0x8b, 0x42, 0x28, 0x85, 0xc0, 0x74, 0x10, 0x83, 0xf8, 1], "current process tier admission");
        Need(helper + 0x6f, [0x8b, 0x4f, 0x3c, 0x85, 0xc9, 0x74, 0x2d], "current nullable ParentCELL admission");
        Need(helper + 0xc4, [0x8b, 0x81, 0, 1, 0, 0, 0x83, 0xe8, 2, 0x0f, 0x84], "independent Actor physical state two refusal");
        Need(helper + 0xd3, [0x83, 0xe8, 2, 0x0f, 0x84], "independent Actor physical state four refusal");
        Need(helper + 0xdc, [0x83, 0xb9, 0xfc, 0, 0, 0, 2, 0x74, 0x0f, 0x6a, 0xff, 0xe8], "source mode two short circuit before physical predicate");
        RequireActorPhysicalRegistrationPredicate(code, Call(helper + 0xe7));
        var model = Call(helper + 0xfa); var prepare = Call(helper + 0x100);
        Need(model + 0x21, [0x3b, 0xbe, 0x4c, 2, 0, 0, 0x75], "same current-thread reference-model cache selection");
        Need(model + 0x40, [0x8b, 0x47, 0x5c, 0x85, 0xc0, 0x74, 6, 0x8b, 0x40, 0x14], "same current Reference model fallback");
        Need(prepare + 5, [0x85, 0xf6, 0x74], "null model skips collision-name preparation");
        Need(prepare + 9, [0x8b, 0x4e, 0x1c], "current scene collision object before physical child");
        Need(prepare + 0x40, [0x8b, 0x57, 8, 0x8b, 0x46, 8, 0x89, 0x42, 0x70], "actual rigid-body name receives the same scene name");
        var bodyClass = Word(code, prepare + 0x31);
        var declarations = new List<int>();
        for (var at = 0; at <= code.Length - 20; ++at)
            if (code[at] == 0x68 && code[at + 5] == 0x68 && code[at + 10] == 0xb9 &&
                Word(code, at + 11) == bodyClass && code[at + 15] == 0xe8 &&
                image.Literal(Word(code, at + 6)) == "bhkRigidBody") declarations.Add(at);
        if (declarations.Count != 1)
            throw new NotSupportedException("Controller name preparation lost its actual rigid-body type-chain declaration.");
        if (Call(prepare + 0x79) != prepare)
            throw new NotSupportedException("Controller model preparation changed its actual source child recursion.");
        Need(helper + 0x10e, [0x8d, 0x4f, 0x40, 0xe8], "independent secondary Extra read after model preparation");
        var secondary = Call(helper + 0x111);
        Need(secondary, [0x6a, 0x5f, 0xe8], "same source secondary Extra subtype");
        if (Call(secondary + 2) != Call(extra + 0x1b))
            throw new NotSupportedException("Controller helper changed the actual shared Extra-list getter.");
        Need(helper + 0x241, [0x8b, 0x80, 0x88, 2, 0, 0, 0xff, 0xd0, 0x8b, 0xf0, 0x85, 0xf6, 0x0f, 0x85], "nonnull controller skips construction");
        if (helper + 0x253 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(helper + 0x24f)) != helper + 0x430)
            throw new NotSupportedException("Controller reuse no longer skips descriptor, group, scale and constructor-only flag stores.");
        Need(helper + 0x4a8, [0x8b, 0x8e, 0x24, 4, 0, 0, 0x85, 0xc9, 0x74], "actual nullable first body before current model property");
        Need(helper + 0x4cf, [0x39, 0x45, 0xdc], "fresh direct reference model comparison");
        Need(helper + 0x4ed, [0x8b, 0x8e, 0x2c, 4, 0, 0, 0x85, 0xc9], "independent nullable second body model binding");
        var motion = Call(helper + 0x56c);
        Need(motion + 0x2d, [0xf6, 0x40, 0xc, 2], "actual model BSX physical enable flag");
        Need(motion + 0xb5, [0x80, 0x7c, 0x24, 0x10, 0], "actual recursive source-model motion operand");
        var flagsGetter = Call(motion + 0x21);
        Need(flagsGetter + 0xc, [0xff, 0x35], "actual current BSX name singleton");
        var flagsName = Word(code, flagsGetter + 0xe);
        var names = new List<string>();
        for (var at = 0; at <= code.Length - 21; ++at)
            if (code[at] == 0x68 && code[at + 5] == 0xe8 && Match(code, at + 10, [0x89, 6, 0x83, 0xc4, 4, 0x89, 0x35]) &&
                Word(code, at + 17) == flagsName)
                names.Add(image.Literal(Word(code, at + 1)) ?? throw new InvalidDataException("BSX selector has no original interned name."));
        if (names is not [FalloutPlayerModelLoadContinuationSource.ModelMotionFlagsName])
            throw new NotSupportedException("Controller model motion changed its actual named BSX flag field.");
        var ragdollCaller = Call(helper + 0x598); var ragdollConstructor = Call(ragdollCaller + 0x140);
        var ragdollTables = image.SourceClassTables(".?AVbhkRagdollController@@", 1).ToArray();
        Need(ragdollConstructor + 0x2c, [0xc7, 6], "independent actual Actor ragdoll-controller constructor");
        if (!ragdollTables.Contains(Word(code, ragdollConstructor + 0x2e)))
            throw new NotSupportedException("Following Actor child no longer constructs its own actual ragdoll controller.");
        Need(ragdollConstructor + 0x48f, [0xc7, 0x46, 0xc, 2, 0, 0, 0], "independent initial ragdoll mode two");
        Need(ragdollConstructor + 0x36b, [0x66, 0xc7, 0x46, 0x41, 0, 0], "independent controller readiness initially false");
        Need(ragdollConstructor + 0x165, [0x66, 0x89, 0xbe, 0xf4, 0, 0, 0], "first tracking animation bone initially minus one");
        Need(ragdollConstructor + 0x16c, [0x66, 0x89, 0xbe, 0xf6, 0, 0, 0], "first tracking physical bone initially minus one");
        Need(ragdollConstructor + 0x173, [0xc7, 0x86, 0xf8, 0, 0, 0, 0x92, 0x0a, 0x86, 0x3f], "first tracking initial angular limit");
        Need(ragdollConstructor + 0x212, [0x66, 0x89, 0xbe, 0x44, 1, 0, 0], "second tracking animation bone initially minus one");
        Need(ragdollConstructor + 0x219, [0x66, 0x89, 0xbe, 0x46, 1, 0, 0], "second tracking physical bone initially minus one");
        Need(ragdollConstructor + 0x220, [0xc7, 0x86, 0x48, 1, 0, 0, 0x92, 0x0a, 0x86, 0x3f], "second tracking initial angular limit");
        Need(ragdollCaller + 0x64, [0x8b, 0x8f, 0xa4, 0, 0, 0], "actual independent Actor rig field read");
        Need(ragdollCaller + 0x145, [0x89, 0x87, 0xa4, 0, 0, 0], "same returned controller publishes into Actor field");
        Need(ragdollCaller + 0x152, [0x80, 0x78, 0x42, 0], "caller consumes actual returned controller readiness");
        Need(ragdollConstructor + 0x4c7, [0xc7, 0x46, 0x2c, 0, 0, 0x80, 0x3f], "initial ragdoll quaternion identity");
        Need(ragdollConstructor + 0x4ce, [0xc7, 0x46, 0x30, 0, 0, 0x80, 0x3f], "independent initial ragdoll scale one");
        Need(ragdollCaller + 0x1c8, [0x0f, 0x10, 5], "original model-axis matrix before its independent copy");
        var originalAxes = image.Read(Word(code, ragdollCaller + 0x1cb), 36);
        if (!Enumerable.Range(0, 9).Select(index => Word(originalAxes, index * 4)).SequenceEqual(World.Physics.FalloutRagdollMatrixInverse.Identity))
            throw new NotSupportedException("Ragdoll model axes changed their original independent identity field.");
        var copyAxes = Call(ragdollCaller + 0x1ff);
        Need(copyAxes, [0x55, 0x8b, 0xec, 0x8b, 0x4d, 0xc, 0x8b, 0x45, 8, 0x0f, 0x10, 1,
            0x0f, 0x11, 0, 0x0f, 0x10, 0x41, 0x10, 0x8b, 0x49, 0x20, 0x0f, 0x11, 0x40, 0x10,
            0x89, 0x48, 0x20, 0x5d, 0xc2, 8, 0], "actual nine-element copy without a reference-transform getter");
        var invertAxes = Call(ragdollCaller + 0x214);
        Need(invertAxes + 0x137, [0xf3, 0x0f, 0x10, 5], "source matrix inverse determinant tolerance");
        if (Word(image.Read(Word(code, invertAxes + 0x13b), 4), 0) != World.Physics.FalloutRagdollMatrixInverse.DeterminantTolerance)
            throw new NotSupportedException("Ragdoll model inverse changed its actual determinant boundary.");
        Need(invertAxes + 0x14b, [0x0f, 0x2f, 0xc1, 0x72, 8], "singular determinant retains preceding cofactor stores");
        RequireNpcBodyPartTables(code, image, ragdollCaller);
        RequireActorAnimationNotes(code, image, player, ragdollCaller);
        RequireActorModelAssociation(code, image, player);
        RequirePlayerModelLoadTail(code, image, load);
        var root = Call(ragdollConstructor + 0x587);
        Need(root + 6, [0x8b, 0x73, 0x1c, 0x85, 0xf6, 0x75], "ragdoll selects the first current collision before children");
        if (Call(root + 0x38) != root)
            throw new NotSupportedException("Ragdoll root collision changed its source-ordered child recursion.");
        var templatePath = Call(ragdollCaller + 0xa3); var referencePath = Call(templatePath + 0x10);
        var component = Call(referencePath); var modelOverride = Call(component + 6);
        Need(modelOverride, [0x6a, FalloutPlayerModelLoadContinuationSource.ControllerModelOverrideExtra, 0xe8], "actual current model-component Extra subtype");
        Need(component + 0xf, [0x8b, 0x47, 0x1c], "current Base after absent model override");
        var templatePose = Call(ragdollConstructor + 0x5fb);
        Need(templatePose + 0x27, [0x8b, 0x75, 0xc], "template traversal receiver is independent from current target root");
        if (image.Literal(Word(code, templatePose + 0x4f)) != FalloutPlayerModelLoadContinuationSource.NonAccumulationNode)
            throw new NotSupportedException("Ragdoll temporary pose changed its original NonAccum name.");
        Need(templatePose + 0x89, [0x8b, 0x4e, 0x18], "NonAccum uses its template parent local pose");
        Need(templatePose + 0x173, [0x0f, 0x10, 0x41, 0x34], "actual template local rotation copy");
        Need(templatePose + 0x18c, [0xf3, 0x0f, 0x7e, 0x41, 0x58], "actual template local translation copy");
        if (Call(templatePose + 0x1d2) != templatePose)
            throw new NotSupportedException("Ragdoll temporary pose changed its source template child recursion.");
        var update = Call(ragdollConstructor + 0x61b);
        Need(ragdollConstructor + 0x603, [0xc7, 0x45, 0xdc, 0, 0, 0, 0], "ragdoll update time zero");
        Need(ragdollConstructor + 0x60d, [0xc7, 0x45, 0xe0, 0, 0, 0, 0], "ragdoll update controller-delivery and flags zero");
        Need(update + 6, [0x6a, 0, 0xff, 0x75, 8, 0x8b, 6, 0xff, 0x90, 0xa4, 0, 0, 0], "actual root downward update with zero inherited flags");
        if (Call(ragdollConstructor + 0x694) != update)
            throw new NotSupportedException("Ragdoll restored pose changed its actual second root update.");
        var temporaryFlags = Call(ragdollConstructor + 0x5ab);
        Need(temporaryFlags + 0x19, [0x83, 0xc8, 0x20], "actual recursive blend flag restore");
        Need(temporaryFlags + 0x1e, [0x25, 0xdf, 0xff, 0, 0], "actual recursive temporary blend flag clear");
        if (Call(temporaryFlags + 0x51) != temporaryFlags)
            throw new NotSupportedException("Ragdoll blend flag changed its actual current source recursion.");
        var nodes = image.SourceClassTables(".?AVNiNode@@", 47).ToArray();
        if (nodes is not [var nodeTable]) throw new NotSupportedException("Ragdoll world update lost its source NiNode virtual table.");
        var downward = SourceOffset(code, image.CodeBase, Word(image.Read(nodeTable + 0xa4, 4), 0));
        Need(downward + 0xc, [0x80, 0x7b, 4, 0, 0x74, 0x50], "zero delivery byte skips controller traversal");
        Need(downward + 0x62, [0x8b, 7, 0x8b, 0xcf, 0x53, 0xff, 0x90, 0xb8, 0, 0, 0], "world-data virtual after independent controller guard");
        var world = SourceOffset(code, image.CodeBase, Word(image.Read(nodeTable + 0xb8, 4), 0));
        Need(world + 9, [0x8b, 0x4e, 0x1c, 0x85, 0xc9], "current collision-object world publication precedes ordinary composition");
        Need(world + 0x22, [0x8b, 0x4e, 0x18, 0x85, 0xc9], "actual nullable parent world frame");
        Need(world + 0x2f, [0xc1, 0xe8, 9, 0xa8, 1], "current flag-nine parent-world copy branch");
        var blendTables = image.SourceClassTables(".?AVbhkBlendCollisionObject@@", 45).ToArray();
        if (blendTables is not [var blendTable]) throw new NotSupportedException("Model motion lost its actual blend collision virtual.");
        var blendMotion = SourceOffset(code, image.CodeBase, Word(image.Read(blendTable + 0xb0, 4), 0));
        Need(blendMotion + 0xab, [0xf3, 0x0f, 0x10, 0x14, 0xc5], "actual shared collision blend group factor read");
        var factors = image.Read(Word(code, blendMotion + 0xb0), 256);
        if (!Enumerable.Range(0, 64).All(index => Word(factors, index * 4) == World.Physics.FalloutPhysicalConstructorInputs.InitialCollisionBlendFactor(index)))
            throw new NotSupportedException("Model motion changed its original independent collision blend field initializers.");
        var key = Call(ragdollCaller + 0xd7);
        if (Call(key + 0x12) != referencePath || image.Literal(Word(code, key + 0x53)) != "%s%i")
            throw new NotSupportedException("Ragdoll descriptor key changed its actual model component or format.");
        Need(key + 0x3f, [0xd8, 0x0d], "source x87 multiply before Float32 key argument store");
        if (Word(image.Read(Word(code, key + 0x41), 4), 0) != 0x42c80000)
            throw new NotSupportedException("Ragdoll descriptor key changed its source scale multiplier.");
        Need(key + 0x45, [0xd9, 0x5d, 8, 0xd9, 0x45, 8, 0xdb, 0x5d, 0xfc], "independent actual Float32 store before integer conversion");
        var scale = Call(key + 0x3a);
        Need(scale + 4, [0xf3, 0x0f, 0x10, 0x41, 0x38, 0x8b, 0x49, 0x1c], "same current reference scale and Base before effective height");
        Need(scale + 0x23, [0xf3, 0x0f, 0x59, 0x81, 0x3c, 1, 0, 0], "current NPC height multiplier");
        var initialize = Call(ragdollConstructor + 0x628);
        Need(initialize + 0x761, [0xc6, 0x46, 0x42, 1], "readiness store follows the returned physical drive and twelve settings");
        Need(initialize + 0x310, [0x8b, 0x48, 8, 0x8b, 0x49, 0x18], "actual first collision target parent store");
        var collect = Call(initialize + 0x44d);
        Need(collect + 0x14, [0x8b, 0x7d, 0x1c, 0x85, 0xff], "actual collision-first physical subtree collection");
        if (Call(collect + 0x18d) != collect) throw new NotSupportedException("Ragdoll physical collection changed its source child recursion.");
        var order = Call(initialize + 0x460);
        Need(order + 0x1b, [0x8d, 0x7e, 0xff], "one physical root before constraint denominator comparison");
        Need(order + 0x1fa, [0x8b, 0x48, 0x14, 0x8b, 0x58, 0x18], "actual joint child and parent body fields");
        Need(order + 0x3df, [0x3b, 0xf9, 0x7e], "equal-depth bodies skip physical-order exchange");
        var descriptor = Call(initialize + 0x48f);
        Need(descriptor + 0x12d, [0x8b, 0x44, 0xb8, 0xfc], "physical reference pose consumes the ordered preceding joint");
        Need(descriptor + 0x157, [0x66, 0x89, 0x0c, 0x78], "physical skeleton stores its actual parent index");
        var drive = Call(initialize + 0x6a6);
        Need(drive + 0x32, [0xc7, 0x43, 0xc, 0, 0, 0, 0], "empty per-body drive control-index array");
        Need(drive + 0xe1, [0x3b, 0x58, 0x10], "physical body count selects the full skeleton parent copy");
        Need(drive + 0x2c0, [0x8b, 0x41, 0x2c, 0x8b, 0xe, 0x8b, 0x40, 4], "same physical skeleton parent-index array");
        var defaults = new List<uint>();
        foreach (var at in new[] { 0xa9, 0xb0, 0xb7 })
        {
            Need(drive + at, [0x0f, 0x28], "source constructor drive defaults");
            var packed = image.Read(Word(code, drive + at + 3), 16);
            defaults.AddRange(Enumerable.Range(0, 4).Select(index => Word(packed, index * 4)));
        }
        if (!defaults.SequenceEqual(FalloutRagdollDriveSource.Defaults))
            throw new NotSupportedException("Ragdoll drive changed its original constructor defaults.");
        var settingLoads = new[] { 0x6b6, 0x6cc, 0x6d9, 0x6e6, 0x6f3, 0x700, 0x70d, 0x71a, 0x727, 0x734, 0x741, 0x74e };
        for (var index = 0; index < settingLoads.Length; ++index)
        {
            Need(initialize + settingLoads[index], [0xf3, 0x0f, 0x10, 5], "current independent drive INI float read");
            var setting = Word(code, initialize + settingLoads[index] + 4);
            if (image.Literal(Word(image.Read(setting + 4, 4), 0)) != FalloutRagdollDriveSource.Settings[index])
                throw new NotSupportedException("Ragdoll drive changed its source setting identity or order.");
        }
        var history = Call(drive + 0x31b);
        Need(history + 0x2a, [0x8b, 0x81, 0xa0, 1, 0, 0], "previous drive state reads actual body linear velocity");
        Need(history + 0x4e, [0x8b, 0x81, 0xb0, 1, 0, 0], "previous drive state reads actual body angular velocity");
        Need(history + 0x72, [0x8b, 0x81, 0x30, 1, 0, 0], "previous drive state reads actual swept center of mass");
        Need(history + 0x96, [0x8b, 0x81, 0x50, 1, 0, 0], "previous drive state reads actual swept rotation");
        Need(initialize + 0x765, [0x8b, 0x40, 0xc], "prior shared animation skeleton before arm lookup");
        var armLookups = new[] { 0x775, 0x782, 0x798, 0x7ae, 0x7c4, 0x7da };
        for (var index = 0; index < armLookups.Length; ++index)
        {
            Need(initialize + armLookups[index], [0x68], "independent current arm bone name");
            if (image.Literal(Word(code, initialize + armLookups[index] + 1)) != World.Physics.FalloutRagdollArmInitialization.BoneNames[index])
                throw new NotSupportedException("Ragdoll prior-animation lookup changed its actual arm name order.");
        }
        Need(initialize + 0x889, [0x88, 0x8e, 0x1c, 2, 0, 0], "actual source arm availability byte after current target resets");
        var animation = Call(initialize + 0x891); var animationCollect = Call(animation + 0xae);
        if (Call(animation + 0x39) != root || Call(animationCollect + 0x385) != root || Call(animationCollect + 0x3f9) != animationCollect)
            throw new NotSupportedException("Ragdoll animation skeleton changed its first-collision child pruning or ordered recursion.");
        Need(animationCollect + 0x146, [0x8b, 0x4e, 0x18], "animation parent index uses the actual current scene parent");
        Need(animationCollect + 0x1a7, [0x8b, 0x40, 0x58, 0xf3, 0x0f, 0x10, 0x98, 0x98, 0, 0, 0], "nonroot local translation consumes current rig parent world scale");
        Need(animationCollect + 0xf5, [0xf3, 0x0f, 0x10, 5], "animation local translation source units");
        if (Word(image.Read(Word(code, animationCollect + 0xf9), 4), 0) != World.Physics.FalloutPhysicalConstructorInputs.SourceToPhysicalBits)
            throw new NotSupportedException("Ragdoll animation skeleton changed its independent source/physical units.");
        Need(animationCollect + 0x3b0, [0x3d], "current source child runtime class chain");
        var excludedClass = Word(code, animationCollect + 0x3b1);
        var excludedNames = new List<string>();
        for (var at = 0; at <= code.Length - 15; ++at)
            if (code[at] == 0x68 && code[at + 5] == 0x68 && code[at + 10] == 0xb9 && Word(code, at + 11) == excludedClass)
                excludedNames.Add(image.Literal(Word(code, at + 6)) ?? "");
        if (excludedNames is not ["WeaponObject"])
            throw new NotSupportedException("Ragdoll animation child pruning changed its actual excluded source class.");
        var referencePose = Call(animation + 0x488);
        Need(referencePose + 0x33, [0xc7, 4, 0x88, 2, 0, 0, 0], "animation pose publishes current bone dirty flags");
        Need(referencePose + 0x44, [0x66, 0xc7, 0x46, 0x28, 0, 1], "animation pose initializes local-valid model-dirty fields");
        Need(initialize + 0x8bf, [0xc6, 0x40, 4, 0], "shared animation root translation unlock before mapper construction");
        Need(initialize + 0x8d1, [0xc6, 0x40, 4, 0], "independent physical root translation unlock before mapping");
        Need(initialize + 0x97a, [0x66, 0xc7, 0x85, 0x3c, 0xff, 0xff, 0xff, 1, 1], "source mapping automatic construction bytes");
        Need(initialize + 0x9a6, [0xc6, 0x85, 0x3d, 0xff, 0xff, 0xff, 0], "source caller disables automatic chain mapping independently");
        var mapper = Call(initialize + 0x9b5);
        Need(mapper + 0x60, [0x3b, 0x47, 0x10], "automatic mapping chooses its actual smaller bone denominator");
        if (Call(mapper + 0x2f6) != Call(mapper + 0x35c))
            throw new NotSupportedException("Ragdoll mapper changed independent source and target local-to-world pose construction.");
        Need(mapper + 0x25c1, [0x80, 0xbd, 0xdc, 0xfd, 0xff, 0xff, 0], "current automatic simple-mapping flag");
        Need(mapper + 0x25fe, [0xbf], "default current mapping name comparator");
        var compareNames = SourceOffset(code, image.CodeBase, Word(code, mapper + 0x25ff));
        if (Call(compareNames + 0x1c) != Call(compareNames + 0x27))
            throw new NotSupportedException("Ragdoll mapper changed its independent byte-case name comparison.");
        Need(compareNames + 0x2f, [0x3a, 0xd8], "source folded name-byte equality");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Controller model source lost an actual direct child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Player controller model call changed its " + role + "."); }
    }
}
