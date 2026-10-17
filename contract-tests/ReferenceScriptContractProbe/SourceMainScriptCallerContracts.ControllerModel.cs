using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedControllerModel(string dataRoot)
    {
        using var content = RuntimeLiveContentSource.Open(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        FalloutExecutableStringTable.AuditPlayerModelControllerPrefix(content.FalloutExecutablePath);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var bodyPartTables = FalloutNpcBodyPartTables.FromLoadedRecords(records);
        var playerBase = records.RuntimeFormKey(7);
        var playerTable = bodyPartTables.Read(playerBase, playerBase);
        var ordinaryTable = bodyPartTables.Read(new("authored-other.esm", 1), playerBase);
        Require(playerTable.FormKey == records.RuntimeFormKey(0x1c) && ordinaryTable.FormKey == records.RuntimeFormKey(0x1d),
            "Owned NPC virtual lost the separate Player and default body-part records.");
        var playerBodyParts = FalloutBodyPartData.Read(playerTable);
        Require(playerBodyParts.ModelPath == "Characters\\_Male\\skeleton.NIF" &&
            FalloutRagdollDeathAssetPath.Select(playerBodyParts.ModelPath) == "Characters\\_Male\\Death.psa",
            "Owned ragdoll path substituted the NPC model for the actual Player body-part component.");
        Require(FalloutRagdollDeathAssetPath.Select("authored.nif") is null &&
            FalloutRagdollDeathAssetPath.Select("authored/shape.nif") is null &&
            FalloutRagdollDeathAssetPath.Select("authored\\other/shape.nif") == "authored\\Death.psa",
            "Ragdoll path selection normalized or invented a literal source backslash.");
        Reject(() => FalloutRagdollDeathAssetPath.Select(new string('a', 260)));
        Require(FalloutRagdollDeathAssetPath.Select(new string('é', 240) + "\\s.nif") == new string('é', 240) + "\\Death.psa",
            "Ragdoll bounded path copy counted UTF-8 bytes instead of the actual source Windows text encoding.");
        var enumeratedParts = playerBodyParts.EnumerateRagdollParts();
        Require(enumeratedParts.Select(part => part.Type).SequenceEqual(new byte[] { 10, 7, 1 }) &&
            enumeratedParts.Where(part => (part.Flags & 4) != 0).All(part => !string.IsNullOrWhiteSpace(part.IkStartNode)) &&
            enumeratedParts.All(part => (part.Flags & 0x20) == 0),
            "Owned Player body parts lost source list-head ordering, BPNI or their independent tracking flags.");
        Require(playerBodyParts.Parts.Any(part => part.HealthPercent == 255) &&
            enumeratedParts.Where(part => part.Type is 7 or 10).All(part => part.HealthPercent == 150),
            "Owned Player limb-health percentages were truncated to an invented one-hundred limit.");
        var appearance = FalloutPlayerSourceAppearance.Select(
            FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7)), content, false, records);
        var prepared = FalloutNpcPreparedGeometry.Read(appearance, content);
        var scene = prepared.SourceModel.Instantiate().ConstructLogicalScene();
        try
        {
            using var fixture = new PlayerReturnTailFixture();
            fixture.EnterLoad((load, guard) => scene.PrepareControllerModel(load, guard));
            var saved = scene.Capture(); var call = saved.ControllerModelPreparation!;
            Require(call.Returned && call.Failure is null && call.Visited == scene.Objects.Count && call.Stores.Count > 0,
                "Owned controller model omitted actual collision-name stores or source objects.");
            var definition = FalloutRagdollSourceSkeleton.Read(scene.SourceModel, scene);
            Console.WriteLine("OPENNV_OWNED_RAGDOLL_INPUT_DECLARATIONS " + JsonSerializer.Serialize(new
            {
                declarationsOnly = true,
                shapes = definition.Bones.Select(bone =>
                {
                    var body = prepared.SourceModel.Skeleton.FindAttachedBody(bone.Body)!;
                    var shape = prepared.SourceModel.Skeleton.Source.ReadObject(body.Shape);
                    return new
                    {
                        bone.Name, bone.Body, type = shape.Block.TypeName,
                        radius = shape switch { FalloutNifCapsuleShape capsule => (float?)capsule.Radius, FalloutNifBoxShape box => box.Radius, FalloutNifSphereShape sphere => sphere.Radius, _ => null },
                        endpointRadius = (shape as FalloutNifCapsuleShape)?.FirstRadius,
                        first = (shape as FalloutNifCapsuleShape)?.First,
                        second = (shape as FalloutNifCapsuleShape)?.Second
                    };
                }),
                joints = prepared.SourceModel.Skeleton.Constraints.Select(joint => new
                { joint.Header.Block.Index, joint.Header.Block.TypeName, joint.Header.WrappedType, joint.Header.EntityA, joint.Header.EntityB }),
                hinges = prepared.SourceModel.Skeleton.Constraints.Where(joint => joint.Declaration is FalloutNifHingeConstraint).Select(joint =>
                {
                    var hinge = (FalloutNifHingeConstraint)joint.Declaration;
                    return new { body = joint.Header.EntityA, parent = joint.Header.EntityB, joint.Header.WrappedType, hinge.Minimum, hinge.Maximum, hinge.AxisA };
                })
            }));
            var physicalOrder = FalloutRagdollPhysicalOrder.Build(definition.Bones.Select(bone => bone.Body).ToArray(),
                definition.Joints.Select(joint => new FalloutRagdollPhysicalLink(joint.Constraint, joint.FirstBody, joint.SecondBody)).ToArray());
            Require(physicalOrder.Bodies.Count == definition.Bones.Count && physicalOrder.Parents[0] == -1 &&
                physicalOrder.Parents.Skip(1).All(index => index >= 0), "Owned ragdoll physical joints did not form an ordered current tree.");
            var reordered = FalloutRagdollPhysicalOrder.Build([20, 40, 10, 30], [new(1, 20, 10), new(2, 30, 20), new(3, 40, 10)]);
            Require(reordered.Bodies.SequenceEqual(new[] { 10, 40, 20, 30 }) && reordered.Parents.SequenceEqual(new[] { -1, 0, 0, 2 }) &&
                reordered.Constraints.SequenceEqual(new[] { 3, 1, 2 }), "Physical skeleton changed the source equal-depth exchange order or joint joins.");
            Reject(() => FalloutRagdollPhysicalOrder.Build([1, 2, 3], [new(1, 2, 1), new(2, 2, 3)]));
            Reject(() => FalloutRagdollPhysicalOrder.Build([1, 2, 3, 4], [new(1, 2, 3), new(2, 3, 2), new(3, 4, 1)]));
            var cache = new FalloutRagdollDescriptorCache(); var factories = 0;
            var identity = new FalloutCharacterControllerProxyTransform(new(FalloutPhysicalVector3.FromValues(1, 0, 0),
                FalloutPhysicalVector3.FromValues(0, 1, 0), FalloutPhysicalVector3.FromValues(0, 0, 1)), default);
            FalloutRagdollSkeletonDescriptor ConstructDescriptor()
            { ++factories; return new(Guid.NewGuid(), "authored-key", new('a', 64), [new(1, "authored-bone", -1, identity, 0x3f800000)], []); }
            var firstDescriptor = cache.Acquire("authored-key", new('a', 64), ConstructDescriptor);
            var sharedDescriptor = cache.Acquire("authored-key", new('a', 64), ConstructDescriptor);
            Require(!firstDescriptor.Hit && sharedDescriptor.Hit && factories == 1 && ReferenceEquals(firstDescriptor.Descriptor, sharedDescriptor.Descriptor),
                "Current ragdoll descriptor did not retain its actual shared source object.");
            Reject(() => cache.Acquire("authored-key", new('b', 64), ConstructDescriptor));
            var rootPose = FalloutRagdollAnimationPose.Read(new(new(14, 21, -7), [1, 0, 0, 0, 1, 0, 0, 0, 1], 2), 1);
            var childPose = FalloutRagdollAnimationPose.Read(new(new(14, 21, -7), [1, 0, 0, 0, -1, 0, 0, 0, -1], 2), 3);
            Require(rootPose.Rotation == new FalloutNifQuaternion(1, 0, 0, 0) && rootPose.Scale == 0x40000000 &&
                childPose.Rotation == new FalloutNifQuaternion(0, 1, 0, 0) && childPose.Scale == rootPose.Scale &&
                childPose.Translation.XValue == FalloutPhysicalFloat32.Multiply(rootPose.Translation.XValue, 3),
                "Ragdoll animation changed root/child units, largest-diagonal rotation or independent local scale.");
            fixture.EnterLoad((load, guard) =>
            {
                guard(load);
                var prior = cache.ReadExistingAnimation(firstDescriptor.Descriptor);
                Require(prior is null, "Ragdoll physical cache invented an animation descriptor before its source construction.");
                var absentArms = FalloutRagdollArmInitialization.Read(load, prior); absentArms.Validate();
                Require(absentArms.Availability == 1 && absentArms.BoneIndices.All(index => index is null),
                    "Source prior-animation absence did not preserve its actually unwritten arm fields.");
                Reject(() => absentArms.RequireBoneIndex(0));
                var footDefaults = FalloutFootIkSetup.Construct();
                Require(footDefaults is { Hip: -1, Knee: -1, Ankle: -1, Skeleton: null } && absentArms.BoneIndices.All(index => index is null),
                    "Written foot setup sentinels were confused with the source arm fields that have no writer.");
                Require(FalloutFootIkMath.Normalize(FalloutPhysicalVector3.FromValues(0, 3, 4)) == FalloutPhysicalVector3.FromValues(0, .6f, .8f) &&
                    FalloutFootIkMath.Normalize(default) == default,
                    "Foot calibration changed current-axis Float32 normalization or the actual zero branch.");
                Require(FalloutFootIkMath.FootEnd(FalloutPhysicalVector3.FromValues(.5f, 0, 0), default) == FalloutPhysicalVector3.FromValues(.5f, 0, 0) &&
                    FalloutFootIkMath.FootEnd(FalloutPhysicalVector3.FromValues(.49f, 0, 0), default) == default &&
                    FalloutFootIkMath.FootEnd(FalloutPhysicalVector3.FromValues(0, 3, 4), default) == FalloutPhysicalVector3.FromValues(0, 3, 4),
                    "Foot-end calibration changed its equal threshold or normalized an authored offset.");
                Require(MathF.Abs(FalloutFootIkMath.Value(FalloutFootIkMath.MaximumKneeCosine(0)) + .17364818f) < .000001f &&
                    MathF.Abs(FalloutFootIkMath.Value(FalloutFootIkMath.MinimumKneeCosine(0)) - .34202015f) < .000001f,
                    "Foot calibration lost its independent source angle offsets and cosine directions.");
                Reject(() => (absentArms with { BoneIndices = [-1, -1, -1, -1, -1, -1] }).Validate());
                var namedArmDescriptor = new FalloutRagdollAnimationDescriptor(Guid.NewGuid(), "authored-key", new('a', 64),
                    FalloutRagdollArmInitialization.BoneNames.Select((name, index) => new FalloutRagdollAnimationBone(
                        name.ToLowerInvariant(), index == 0 ? -1 : 0, rootPose, true)).ToArray());
                var boundArms = FalloutRagdollArmInitialization.Read(load, namedArmDescriptor); boundArms.Validate();
                Require(FalloutRagdollDeathBones.Read(namedArmDescriptor).Select(bone => bone.Index).SequenceEqual(new[] { -1, -1, 3 }),
                    "Death initializer substituted arm indices for its independent three default names.");
                var deathDescriptor = namedArmDescriptor with { Bones = FalloutRagdollDeathBones.Names.Select((name, index) =>
                    new FalloutRagdollAnimationBone(name.ToLowerInvariant(), index == 0 ? -1 : 0, rootPose, true)).ToArray() };
                Require(FalloutRagdollDeathBones.Read(deathDescriptor).Select(bone => bone.Index).SequenceEqual(new[] { 0, 1, 2 }),
                    "Death initializer lost actual folded name order or index-zero admission.");
                Require(boundArms.Availability == 1 && boundArms.BoneIndices.SequenceEqual(new int?[] { 0, 1, 2, 3, 4, 5 }),
                    "Ragdoll current shared animation names did not bind the six independent folded arm indices.");
                var missingArm = namedArmDescriptor with { Bones = namedArmDescriptor.Bones.Select((bone, index) =>
                    index == 5 ? bone with { Name = "authored-unrelated-bone" } : bone).ToArray() };
                var missingArms = FalloutRagdollArmInitialization.Read(load, missingArm); missingArms.Validate();
                Require(missingArms.Availability == 0 && missingArms.RequireBoneIndex(5) == -1,
                    "Ragdoll missing named arm failed to preserve the source minus-one index and availability clear.");
                Reject(() => FalloutRagdollArmInitialization.Read(load, namedArmDescriptor with
                    { Bones = namedArmDescriptor.Bones.Select((bone, index) => index == 0 ? bone with { Name = "Bip01 İ UpperArm" } : bone).ToArray() }));
                var axes = FalloutRagdollMatrixInverse.Evaluate(FalloutRagdollMatrixInverse.Identity); axes.Validate();
                Require(axes.Inverted && axes.Result.SequenceEqual(FalloutRagdollMatrixInverse.Identity),
                    "Ragdoll model axes lost the actual identity inverse.");
                var shear = FalloutRagdollMatrixInverse.Evaluate(new uint[] { 0x40000000, 0, 0, 0x3f800000, 0x40400000, 0, 0, 0, 0x40800000 });
                shear.Validate();
                Require(shear.Inverted && shear.Result.Select(BitConverter.UInt32BitsToSingle).SequenceEqual(
                    new float[] { .5f, 0, 0, -1f / 6, 1f / 3, 0, 0, 0, .25f }),
                    "General inverse changed source Float32 scaling or the direction of a sheared matrix.");
                var singular = FalloutRagdollMatrixInverse.Evaluate(new uint[] { 0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, 0 });
                singular.Validate();
                Require(!singular.Inverted && singular.Result.SequenceEqual(singular.Cofactors) && singular.Result[8] == 0x3f800000,
                    "Singular model axes invented a replacement matrix instead of retaining actual cofactor stores.");
                Reject(() => (singular with { Result = FalloutRagdollMatrixInverse.Identity }).Validate());
                var boundary = FalloutRagdollMatrixInverse.Evaluate(new uint[]
                    { 0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, FalloutRagdollMatrixInverse.DeterminantTolerance });
                Require(!boundary.Inverted, "Matrix inverse changed the equal determinant-tolerance branch.");
                var initialController = new FalloutLivingRagdollControllerSnapshot(Guid.NewGuid(), load, Guid.NewGuid(), 2,
                    new(default, new(1, 0, 0, 0), 0x3f800000), false, FalloutRagdollTrackingConstruction.Construct(),
                    FalloutRagdollTrackingConstruction.Construct(), null, null, null, false, null, false, Ik: FalloutRagdollIkFields.Construct(),
                    FinalFields: FalloutRagdollFinalFields.Construct());
                var emptyModel = new FalloutLivingModelSnapshot("authored-model", new('a', 64), []);
                initialController.Validate(emptyModel);
                Reject(() => (initialController with { Ready = true }).Validate(emptyModel));
                Reject(() => (initialController with { Returned = true }).Validate(emptyModel));
                Reject(() => (initialController with { Mode = 0 }).Validate(emptyModel));
                Reject(() => (initialController with { Ik = initialController.Ik! with { FootEnabled = true } }).Validate(emptyModel));
                Reject(() => (initialController with { Ik = initialController.Ik! with { RayHeightStores = 1 } }).Validate(emptyModel));
                var noteField = new OpenNV.Runtime.World.Cells.FalloutActorRagdollControllerField(load.ReferenceProcess, load.ReferenceConstructor, load.Actor);
                var noteReceiver = new OpenNV.Runtime.World.Cells.FalloutActorAnimationNotes(load.ReferenceProcess, load.ReferenceConstructor, load.Actor);
                Require(noteReceiver.Capture() is { Capacity: 0, Size: 0, EffectiveSize: 0, GrowBy: 1 },
                    "Animation-note receiver invented constructor array membership.");
                Require(!noteReceiver.RegisterRagdoll(noteField), "Animation-note registration changed its source false return.");
                var firstRegistration = noteReceiver.Capture();
                Require(!noteReceiver.RegisterRagdoll(noteField) && noteReceiver.Capture().Registrations.Single() == firstRegistration.Registrations.Single(),
                    "Repeated same-type/same-callback registration replaced the existing actual pair.");
                noteReceiver.Dispatch(new(1, 0, 0, 1)); // Current Actor field is independently null.
                Reject(() => (firstRegistration with { Size = 0 }).Validate());
                Reject(() => noteReceiver.RegisterRagdoll(new(load.ReferenceProcess, Guid.NewGuid(), load.Actor)));
                var noteSource = new FalloutAnimationNoteSourceState();
                Require(noteSource.Capture() is { Size: 0, GrowBy: 1 } && !noteSource.Register(1, noteReceiver),
                    "Current manager note source invented membership or changed its original false return.");
                var notePair = noteSource.Capture().Receivers.Single();
                Require(!noteSource.Register(1, noteReceiver) && noteSource.Capture().Receivers.Single() == notePair,
                    "Current manager duplicated its same actual receiver pair.");
                var otherReceiver = new OpenNV.Runtime.World.Cells.FalloutActorAnimationNotes(load.ReferenceProcess, load.ReferenceConstructor, load.Actor);
                noteSource.Register(1, otherReceiver);
                Require(noteSource.Capture().Receivers.Count == 2 && noteSource.Capture().Receivers[1].Receiver == otherReceiver.Capture().Identity,
                    "Current manager confused receiver type with receiver object identity.");
                noteSource.Dispatch(new(1, 0, 0, 1));
                noteSource.Retire();
                try { noteSource.Register(1, noteReceiver); throw new InvalidDataException("Retired note source accepted another receiver."); }
                catch (ObjectDisposedException) { }
                var instance = new FalloutLivingRagdollSkeletonSnapshot(load, Guid.NewGuid(), firstDescriptor.Descriptor, false, default, [17], []);
                var parameters = FalloutRagdollDriveSource.Settings.Select((name, index) => new FalloutRagdollDriveParameter(name,
                    FalloutRagdollDriveSource.Defaults[index], BitConverter.SingleToUInt32Bits(index + 1), "authored-current-Main", 1)).ToArray();
                var drive = new FalloutLivingRagdollDriveSnapshot(load, Guid.NewGuid(), instance.Instance, [-1], parameters,
                    [new(1, 17, FalloutPhysicalVector3.FromValues(3, 4, 5), identity.Rotation, default, default)], null, true, null, false);
                drive.Validate(instance);
                Reject(() => (drive with { PreviousBodies = [drive.PreviousBodies[0] with { NativeBody = 23 }] }).Validate(instance));
                Reject(() => (drive with { ParameterIndices = [0] }).Validate(instance));
                Reject(() => (drive with { Parameters = parameters.Reverse().ToArray() }).Validate(instance));
                var prefix = parameters.Select((value, index) => index < 3 ? value : value with { Value = value.Default, Origin = null, Stores = 0 }).ToArray();
                (drive with { Parameters = prefix, Returned = false, Failure = "authored-current-setting-read-failed" }).Validate(instance);
                Reject(() => (drive with { Parameters = prefix }).Validate(instance));
                var animationDescriptor = new FalloutRagdollAnimationDescriptor(Guid.NewGuid(), "authored-key", new('a', 64), [new("authored-bone", -1, rootPose, true)]);
                var animation = new FalloutLivingRagdollAnimationSnapshot(load, Guid.NewGuid(), [default], animationDescriptor, false, [rootPose], [2], 0x100, true, null);
                animation.Validate(instance);
                Reject(() => (animation with { BoneFlags = [0] }).Validate(instance));
                Reject(() => (animation with { LocalPoses = [childPose] }).Validate(instance));
                var cachedAnimation = cache.AcquireAnimation("authored-key", new('a', 64), () => animationDescriptor);
                var maps = cache.AcquireMappings(firstDescriptor.Descriptor, cachedAnimation.Descriptor, () => guard(load));
                maps.Mappings.Validate(firstDescriptor.Descriptor, animationDescriptor);
                Require(!maps.Hit && maps.Mappings.AnimationToPhysical.Simple is [{ Source: 0, Target: 0 }] &&
                    maps.Mappings.PhysicalToAnimation.Simple is [{ Source: 0, Target: 0 }] &&
                    maps.Mappings.AnimationToPhysical.Unmapped.Count == 0 && !firstDescriptor.Descriptor.RootTranslationLocked &&
                    !animationDescriptor.RootTranslationLocked, "Ragdoll mapping omitted actual root stores or joined another bone.");
                var sharedMaps = cache.AcquireMappings(firstDescriptor.Descriptor, animationDescriptor, () => guard(load));
                Require(sharedMaps.Hit && ReferenceEquals(maps.Mappings, sharedMaps.Mappings) && firstDescriptor.Descriptor.RootTranslationStores == 1,
                    "Shared mapper construction replayed its root stores or allocated another cache object.");
                Reject(() => firstDescriptor.Descriptor.UnlockRootTranslation());
                Reject(() => animationDescriptor.UnlockRootTranslation());
                var quarterTurn = new FalloutRagdollAnimationPose(FalloutPhysicalVector3.FromValues(10, 20, 30),
                    new(.70710677f, 0, 0, .70710677f), 0x40000000);
                var offset = new FalloutRagdollAnimationPose(FalloutPhysicalVector3.FromValues(3, 0, 0), new(1, 0, 0, 0), 0x3f800000);
                var composed = FalloutRagdollPoseMath.Compose(quarterTurn, offset);
                Require(MathF.Abs(composed.Translation.XValue - 10) < .00001f && MathF.Abs(composed.Translation.YValue - 26) < .00001f &&
                    MathF.Abs(composed.Translation.ZValue - 30) < .00001f && composed.Scale == 0x40000000,
                    "Ragdoll mapped reference pose lost parent rotation, scale or physical translation.");
                var relative = FalloutRagdollPoseMath.Relative(quarterTurn, composed);
                Require(MathF.Abs(relative.Translation.XValue - 3) < .00001f && MathF.Abs(relative.Translation.YValue) < .00001f && relative.Scale == 0x3f800000,
                    "Ragdoll mapper stored the inverse reference transform in the wrong direction.");
            });
            Console.WriteLine("OPENNV_OWNED_RAGDOLL_WORLD_INPUTS " + JsonSerializer.Serialize(new
            {
                root = scene.Root.Source.Block.TypeName,
                classes = scene.Objects.GroupBy(node => node.Source.Block.TypeName).Select(group => new { type = group.Key, count = group.Count() }),
                collisions = prepared.SourceModel.Skeleton.Scene.Where(row => row.Collision is not null).Select(row => new
                { node = row.Declaration.Block.Index, type = row.Collision!.Block.TypeName, row.Collision.Flags, row.Collision.Body })
            }));
            var embedded = prepared.SourceModel.Skeleton.Scene.Select(row => row.Declaration).OfType<FalloutNifNode>()
                .SelectMany(node => FalloutNifNodeControllerChain.Read(prepared.SourceModel.Skeleton.Source, node))
                .GroupBy(controller => (controller.Block.TypeName, FalloutNifNodeControllerChain.Time(controller).Flags, Detail: controller switch
                {
                    FalloutNifTransformController value => $"interpolator={value.Interpolator}",
                    FalloutNifFloatExtraDataController value => $"interpolator={value.Interpolator}",
                    FalloutNifVisibilityController value => $"interpolator={value.Interpolator}",
                    FalloutNifBlendController value => $"keys={value.Keys}",
                    _ => ""
                }))
                .Select(group => $"{group.Key.TypeName}:0x{group.Key.Flags:x4}:{group.Key.Detail}:{group.Count()}");
            Console.WriteLine("OPENNV_OWNED_RAGDOLL_EMBEDDED_CONTROLLERS " + string.Join(",", embedded));
            Require(definition.Bones.Count == call.Stores.Count && definition.Joints.Count > 0 &&
                definition.RootBody == definition.Bones[0].Body && definition.Bones[0].ParentBody is null,
                "Owned ragdoll hierarchy omitted its first collision, body source or current joint denominator.");
            foreach (var store in call.Stores)
                Require(scene.ReadControllerBodyName(store.Body.Asset, store.Body.Block) == store.Name,
                    "Actual body-name consumer did not read the reached model store.");
            foreach (var partNode in scene.Objects.Where(node => node.Key.Asset != 0))
                _ = scene.ReadCurrentCollision(partNode);
            fixture.EnterLoad((load, guard) => Reject(() => scene.PrepareControllerModel(load, guard)));
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(saved))!;
                var cold = prepared.SourceModel.Instantiate().ConstructLogicalScene(saved);
                try
                {
                    Require(JsonSerializer.Serialize(cold.Capture().ControllerModelPreparation) == JsonSerializer.Serialize(saved.ControllerModelPreparation),
                        "Cold controller model replayed or dropped its actual traversal and name stores.");
                    var coldDefinition = FalloutRagdollSourceSkeleton.Read(cold.SourceModel, cold);
                    Require(JsonSerializer.Serialize(coldDefinition with { Model = definition.Model }) == JsonSerializer.Serialize(definition),
                        "Cold ragdoll source hierarchy changed its body/joint/parent graph.");
                    foreach (var store in call.Stores)
                        Require(cold.ReadControllerBodyName(store.Body.Asset, store.Body.Block) == store.Name,
                            "Cold model did not retain its actual physical body-name fields.");
                    saved = cold.Capture();
                }
                finally { cold.Retire(); }
            }
            Reject(() => prepared.SourceModel.Instantiate().ConstructLogicalScene(saved with
            { ControllerModelPreparation = call with { Stores = call.Stores.Skip(1).ToArray() } }));
            Reject(() => prepared.SourceModel.Instantiate().ConstructLogicalScene(saved with
            { ControllerModelPreparation = call with { Stores = call.Stores.Select((store, index) => index == 0 ? store with { Name = "foreign" } : store).ToArray() } }));
            var target = scene.Objects.Single(node => node.Key == definition.Bones[0].Scene);
            scene.PublishLocalPose(target.Key, target.Local with { Translation = new(7, 8, 9), Scale = 2 });
            var before = FalloutNifSceneTransform.From(target.Local);
            fixture.EnterLoad((load, guard) =>
            {
                var modelNode = new OpenNV.Runtime.World.Cells.FalloutReferenceModelDataNode(
                    new(fixture.Scene.Process, scene.Factory, 1), scene, () => guard(load), () => { });
                var retained = modelNode.LogicalScene;
                scene.ApplyRagdollTemplatePose(load, prepared.SourceModel.Skeleton, guard);
                var temporary = scene.Capture();
                Require(temporary.RagdollTemplatePose is { Applied: true, Restored: false, Failure: null } pose &&
                    pose.Stores.Count > 0 && target.Local.Scale == 2 &&
                    JsonSerializer.Serialize(FalloutNifSceneTransform.From(target.Local)) != JsonSerializer.Serialize(before),
                    "Ragdoll template did not copy its source pose or overwrote current scale.");
                Reject(() => FalloutActorLogicalScene.RequireComplete(temporary));
                Reject(() => _ = modelNode.LogicalScene);
                Reject(() => scene.ApplyRagdollTemplatePose(load, prepared.SourceModel.Skeleton, guard));
                var diagnostic = prepared.SourceModel.Instantiate().ConstructLogicalScene(
                    JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(temporary))!);
                try
                {
                    Require(diagnostic.SaveBlocker == "source-ragdoll-template-pose-remains-entered" &&
                        JsonSerializer.Serialize(diagnostic.Capture().RagdollTemplatePose) == JsonSerializer.Serialize(temporary.RagdollTemplatePose),
                        "Cold diagnostic replayed or admitted an entered ragdoll temporary pose.");
                }
                finally { diagnostic.Retire(); }
                var poseBeforeWorld = JsonSerializer.Serialize(scene.Capture().Objects.Select(node => node.Local));
                retained.UpdateRagdollTemplateWorld(load, guard, _ => null);
                foreach (var current in scene.Objects)
                {
                    Reject(() => scene.ReadCurrentCollision(current));
                    _ = scene.ReadRagdollTemplateCollision(load, current, guard);
                }
                Require(scene.Capture().RagdollWorldUpdates is [{ Returned: true, BoundsUpdated: true } world] &&
                    world.WorldStores.Count == scene.Objects.Count && world.BodyStores.Count == 0 &&
                    JsonSerializer.Serialize(scene.Capture().Objects.Select(node => node.Local)) == poseBeforeWorld,
                    "Ragdoll zero-controller-delivery update changed local poses or omitted current worlds/bounds.");
                Reject(() => scene.UpdateRagdollTemplateWorld(load, guard, _ => null));
                scene.RestoreRagdollTemplatePose(load, guard);
                Require(JsonSerializer.Serialize(FalloutNifSceneTransform.From(target.Local)) == JsonSerializer.Serialize(before) &&
                    scene.Capture().RagdollTemplatePose is { Restored: true },
                    "Ragdoll temporary pose did not restore the actual saved local rotation/translation.");
                scene.UpdateRagdollTemplateWorld(load, guard, _ => null);
                Require(scene.Capture().RagdollWorldUpdates is [{ Returned: true, RestoredPose: false }, { Returned: true, RestoredPose: true }],
                    "Ragdoll restore lost the actual second zero-controller-delivery world update.");
                var complete = scene.Capture();
                var cold = prepared.SourceModel.Instantiate().ConstructLogicalScene(
                    JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(complete))!);
                try
                {
                    Require(JsonSerializer.Serialize(cold.Capture().RagdollWorldUpdates) == JsonSerializer.Serialize(complete.RagdollWorldUpdates),
                        "Cold ragdoll world fields replayed or dropped actual update history.");
                }
                finally { cold.Retire(); }
            });
            Console.WriteLine($"OPENNV_OWNED_CONTROLLER_MODEL_PASS objects={call.Visited} rigidBodyNames={call.Stores.Count} " +
                $"motionEnabled={scene.ReadControllerModelMotionEnabled()} rigJoints={definition.Joints.Count} " +
                "cold3NoReplay=true changedStoreRejected=true actualBodyConsumer=true temporaryPoseRestored=true " +
                "enteredPoseColdRefused=true zeroControllerWorldUpdates=2 loaderInvocation=AUTHORED gameplay=UNEXECUTED");
        }
        finally { scene.Retire(); }
    }
}
