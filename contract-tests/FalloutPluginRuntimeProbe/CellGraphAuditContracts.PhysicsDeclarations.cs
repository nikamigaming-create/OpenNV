using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void NifPhysicsDeclarations()
    {
        foreach (var version in new uint[] { 21, 26, 32, 34 })
        {
            foreach (var (type, wrapped) in new[]
            {
                ("bhkRagdollConstraint", 7U), ("bhkLimitedHingeConstraint", 2U),
                ("bhkMalleableConstraint", 7U), ("bhkMalleableConstraint", 2U), ("bhkHingeConstraint", 1U),
            })
            {
                var bytes = FalloutNifPhysicsFixture.Constraint(type, version, wrappedType: wrapped);
                var hash = SHA256.HashData(bytes);
                var observed = new List<FalloutNifReadRange>();
                var source = FalloutNifFile.Read(bytes, observed.Add);
                // Exercise the actual full-file generic dispatch first. The old
                // owner refused here, before either dedicated joint reader ran.
                var decoded = source.Blocks.Select(block => source.ReadObject(block.Index)).ToArray();
                Require(decoded.Length == 3 && decoded[0] is FalloutNifRigidBody && decoded[1] is FalloutNifRigidBody &&
                    ReferenceEquals(decoded[2], source.ReadObject(2)), "Generic constraint declarations lost complete body decoding or cache ownership.");
                var row = new CellGraphAudit.ResourceRow { Path = "meshes/first-party-joint.nif", Kind = "model" };
                CellGraphAudit.SourcePhysicsDeclarations.Inspect(source, decoded[2], row);
                var links = row.DependencyDeclarations.OfType<CellGraphAudit.ControllerLinkDeclaration>().ToArray();
                Require(row.Failures.Count == 0 && links.Length == 2 &&
                    links.Select(link => link.TargetBlock).SequenceEqual([0, 1]) &&
                    links.All(link => link.DecodeDisposition == "target-block-decoded" && link.NativeAdmission == "unverified" &&
                        link.TypedDisposition == "typed-source-target-admitted; native binding unverified"),
                    "Constraint metadata lost exact source body edges or awarded native admission.");
                var header = source.ReadConstraintHeader(2);
                Require(header.EntityA == 0 && header.EntityB == 1 && header.Priority == 1 && header.WrappedType == wrapped,
                    "Generic source decoder changed the original joint identity.");
                uint[] padding;
                if (decoded[2] is FalloutNifRagdollConstraint joint)
                {
                    padding = joint.VectorPadding;
                    Require(ReferenceEquals(joint, source.ReadRagdollConstraint(2)) && joint.Motor is null &&
                        joint.TwistMinimum == -.3f && joint.TwistMaximum == .6f && joint.Friction == 12 &&
                        joint.Strength == (type == "bhkMalleableConstraint" ? .8f : 1) &&
                        joint.Cone == (wrapped == 7 ? .9f : 0), "Generic joint decoding altered limits, motor absence or the native-reader cache.");
                    if (wrapped == 2 && type == "bhkLimitedHingeConstraint")
                    {
                        var hinge = source.ReadHingeConstraint(2);
                        Require(hinge.AxisA == joint.TwistA && hinge.AxisB == joint.TwistB &&
                            hinge.Minimum == -.3f && hinge.Maximum == .6f && hinge.VectorPadding.SequenceEqual(padding),
                            "Limited hinge projection changed the source frame or padding identity.");
                    }
                }
                else
                {
                    var hinge = (FalloutNifHingeConstraint)decoded[2]; padding = hinge.VectorPadding;
                    Require(ReferenceEquals(hinge, source.ReadHingeConstraint(2)) && hinge.Minimum is null && hinge.Maximum is null,
                        "Basic hinge declaration invented a limit or a second source object.");
                }
                Require(padding.SequenceEqual(new uint[] { 0, uint.MaxValue, 0x81234567, uint.MaxValue, 0x80, 0, uint.MaxValue, 0x12345678 }) &&
                    observed.Count(range => range.Owner.Contains("block 2", StringComparison.Ordinal) && range.Field.EndsWith("padding", StringComparison.Ordinal)) == 8 &&
                    SHA256.HashData(bytes).SequenceEqual(hash), "Joint padding was interpreted as finite spatial data or source bytes changed.");
                foreach (var mode in new[] { "same-entity", "missing-entity", "null-entity", "priority", "entity-count", "wrong-entity-type",
                    "nan-vector", "truncated", "trailing" })
                    RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, wrappedType: wrapped, mode: mode)).ReadObject(2));
                if (wrapped != 1)
                    RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, wrappedType: wrapped, mode: "infinite-limit")).ReadObject(2));
                else
                    RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, wrappedType: wrapped, mode: "bad-frame")).ReadObject(2));
                if (type == "bhkMalleableConstraint")
                    foreach (var mode in new[] { "nested-entity", "nested-count", "nested-priority" })
                        RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, wrappedType: wrapped, mode: mode)).ReadObject(2));
            }
            foreach (var type in new[] { "bhkRagdollConstraint", "bhkLimitedHingeConstraint" })
            foreach (var motorType in new byte[] { 1, 2, 3 })
            {
                var bytes = FalloutNifPhysicsFixture.Constraint(type, version, motorType);
                var source = FalloutNifFile.Read(bytes);
                var joint = (FalloutNifRagdollConstraint)source.ReadObject(2);
                Require(joint.Motor is { Enabled: false, MinimumForce: -1000, MaximumForce: 2500 } &&
                    joint.Motor.Type == motorType && ReferenceEquals(source.ReadRagdollConstraint(2), joint),
                    "A complete disabled motor lost its declaration or enabled an invented native drive.");
                Require(motorType switch
                {
                    1 => joint.Motor is FalloutNifPositionConstraintMotor { Tau: .8f, Damping: .5f,
                        ProportionalRecoveryVelocity: 1, ConstantRecoveryVelocity: 2 },
                    2 => joint.Motor is FalloutNifVelocityConstraintMotor { Tau: .2f, TargetVelocity: -4, UseVelocityTarget: true },
                    3 => joint.Motor is FalloutNifSpringConstraintMotor { SpringConstant: 3, SpringDamping: .7f },
                    _ => false,
                }, "Typed motor payload did not retain its distinct source fields.");
                var motorRow = new CellGraphAudit.ResourceRow();
                CellGraphAudit.SourcePhysicsDeclarations.Inspect(source, joint, motorRow);
                var declaration = JsonSerializer.SerializeToElement(motorRow.DependencyDeclarations[0],
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                Require(declaration.GetProperty("motor").GetProperty("type").GetByte() == motorType &&
                    !declaration.GetProperty("motor").GetProperty("enabled").GetBoolean() &&
                    declaration.GetProperty("motor").EnumerateObject().Count() == (motorType == 1 ? 8 : motorType == 2 ? 7 : 6) &&
                    declaration.GetProperty("nativeAdmission").GetString() == "unverified" &&
                    declaration.GetProperty("motorDispatch").GetString() == "not performed",
                    "Motor report erased variant fields or implied native execution.");
                var enabled = FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, motorType, enabled: true));
                Require(((FalloutNifRagdollConstraint)enabled.ReadObject(2)).Motor is { Enabled: true },
                    "Enabled motor declaration was dropped instead of retained independently.");
                RequirePhysicsRefusal(() => enabled.ReadRagdollConstraint(2));
                if (type == "bhkLimitedHingeConstraint") RequirePhysicsRefusal(() => enabled.ReadHingeConstraint(2));
                foreach (var mode in new[] { "nonfinite-motor", "bad-motor-bool", "truncated", "trailing" })
                    RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint(type, version, motorType, mode: mode)).ReadObject(2));
            }
            RequirePhysicsRefusal(() => FalloutNifFile.Read(FalloutNifPhysicsFixture.Constraint("bhkRagdollConstraint", version, 4)).ReadObject(2));
            foreach (var mode in new[] { "blend-before", "blend-after", "blend-middle", "unowned-float-suffix" })
            {
                var bytes = FalloutNifPhysicsFixture.ControllerChain(mode, version);
                var source = FalloutNifFile.Read(bytes); var node = source.ReadNode(0);
                var chain = FalloutNifNodeControllerChain.Read(source, node);
                var expected = mode is "blend-before" or "blend-after" ? 2 : 3;
                Require(chain.Select(value => value.Block.Index).SequenceEqual(Enumerable.Range(1, expected)),
                    "Complete source controller traversal dropped or reordered a blend suffix.");
                foreach (var blend in chain.OfType<FalloutNifBlendController>())
                {
                    FalloutNifNodeControllerChain.RequireDormantBlend(node, blend);
                    Require(ReferenceEquals(source.ReadObject(blend.Block.Index), blend) && blend.Keys == 0,
                        "Blend declaration bypassed generic decoder/cache ownership.");
                }
                if (mode == "unowned-float-suffix") Require(chain[^1] is FalloutNifFloatExtraDataController,
                    "A source suffix with an unowned native channel vanished from the declaration list.");
                var foreign = FalloutNifFile.Read(bytes);
                RequirePhysicsRefusal(() => FalloutNifNodeControllerChain.Read(source, foreign.ReadNode(0)));
                var row = new CellGraphAudit.ResourceRow();
                foreach (var blend in chain.OfType<FalloutNifBlendController>())
                    CellGraphAudit.SourcePhysicsDeclarations.Inspect(source, blend, row);
                Require(row.Failures.Count == 0 && row.DependencyDeclarations.OfType<CellGraphAudit.ControllerLinkDeclaration>()
                    .Where(link => link.Field == "Time.NextController").Select(link => link.TargetBlock)
                    .SequenceEqual(chain.OfType<FalloutNifBlendController>().Select(blend => blend.Time.NextController)),
                    "Blend audit metadata dropped or substituted a real source continuation.");
                var foreignBlend = foreign.Blocks.Where(block => block.TypeName == "bhkBlendController").Select(block => foreign.ReadObject(block.Index)).First();
                RequirePhysicsRefusal(() => CellGraphAudit.SourcePhysicsDeclarations.Inspect(source, foreignBlend, new CellGraphAudit.ResourceRow()));
            }
            foreach (var mode in new[] { "cycle", "foreign-target", "node-suffix", "invalid-next", "missing-next", "nonfinite", "truncated", "trailing" })
            {
                var source = FalloutNifFile.Read(FalloutNifPhysicsFixture.ControllerChain(mode, version));
                RequirePhysicsRefusal(() => FalloutNifNodeControllerChain.Read(source, source.ReadNode(0)));
                Require(source.ReadObject(source.Blocks.Count - 1) is FalloutNifNode,
                    "A malformed controller list prevented an unrelated healthy declaration from decoding.");
            }
            foreach (var mode in new[] { "unknown-keys", "active-blend", "wrong-flags" })
            {
                var source = FalloutNifFile.Read(FalloutNifPhysicsFixture.ControllerChain(mode, version));
                var node = source.ReadNode(0); var chain = FalloutNifNodeControllerChain.Read(source, node);
                Require(chain.Count == 3, "A source declaration drift was hidden before native admission.");
                RequirePhysicsRefusal(() => FalloutNifNodeControllerChain.RequireDormantBlend(node, (FalloutNifBlendController)chain[0]));
            }
        }
        Console.WriteLine("OPENNV_NIF_PHYSICS_DECLARATION_CONTRACT_PASS genericDecoder=true constraints=5 variants=4 " +
            "motorDescriptors=3 enabledNativeRefused=true padding=true sourceChainOrder=true suffix=true " +
            "cycles=true foreignOwner=true malformed=true nativeExecution=not-performed");
    }

    private static void RequirePhysicsRefusal(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed, foreign or unowned physics declaration was accepted.");
    }
}
