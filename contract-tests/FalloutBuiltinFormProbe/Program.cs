using OpenNV.Runtime.Content;

foreach (var builtin in new uint[] { 1, 2, 6, 5, 3, 0x10, 0x12, 0x3b, 0x34, 0x32, 0x15, 0x33, 0x23, 0x24 })
{
    Require(FalloutNewVegasBuiltinForms.IsInternalStatic("STAT", builtin), "bootstrap static identity");
    Require(!FalloutNewVegasBuiltinForms.IsInternalStatic("STAT", 0x01000000 | builtin), "same local ID in another load slot");
    Require(!FalloutNewVegasBuiltinForms.IsInternalStatic("ACTI", builtin), "non-static object type");
}
foreach (var ordinary in new uint[] { 0, 4, 0x14, 0x17, 0x1f, 0x20, 0x31, 0x35, 0x1000, 0xff000034 })
    Require(!FalloutNewVegasBuiltinForms.IsInternalStatic("STAT", ordinary), "ordinary static identity");
Console.WriteLine("OPENNV_BUILTIN_STATIC_PREDICATE_OK exactRuntimeIdentity=true independentOfArt=true");

static void Require(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException("Builtin static contract failed: " + label);
}
