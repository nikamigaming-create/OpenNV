namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginExpressionSource
{
    private static readonly string[][] Names =
    [
        ["CreateExpressionEvaluator"], ["DestroyExpressionEvaluator"], ["ExtractArgsEval"], ["GetNumArgs"], ["GetNthArg"],
        ["ScriptTokenGetType"], ["ScriptTokenGetFloat"], ["ScriptTokenGetBool"], ["ScriptTokenGetFormID"],
        ["ScriptTokenGetTESForm"], ["ScriptTokenGetString"], ["ScriptTokenGetArray", "ScriptTokenGetArrayID"],
        ["ScriptTokenGetActorValue"], ["ScriptTokenGetScriptVar", "ScriptTokenGetScriptLocal"], ["ScriptTokenGetPair"], ["ScriptTokenGetSlice"],
        ["ScriptTokenGetAnimGroup", "ScriptTokenGetAnimationGroup"], ["SetExpectedReturnType"],
        ["AssignCommandResultFromElement"], ["ScriptTokenGetElement"], ["ScriptTokenCanConvertTo"], ["ExtractArgsVA", "ExtractArgsV"]
    ];
    private static readonly ushort[] Parameters = [8, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2];
    private static readonly string[] Returns = ["pointer32", "void", "bool8", "uint8", "pointer32", "uint8", "float64", "bool8",
        "uint32", "pointer32", "pointer32", "", "uint32", "pointer32", "pointer32", "pointer32", "uint32", "void", "void", "void", "bool8", "bool8"];

    internal static NativeNvseExpressionAbi Read(NativePluginImageDeclaration module, string pdbPath)
    {
        if (module.CodeViews.Count != 1) throw new NotSupportedException("Original expression client has no unique source CodeView identity.");
        var pdb = new NativePluginPdbTypes(pdbPath, module.CodeViews[0]);
        var structures = pdb.Structures("ExpressionEvaluatorUtils");
        if (structures.Count == 0) throw new NotSupportedException("Matched original PDB has no complete expression utility declaration.");
        NativeNvseExpressionAbi? agreed = null;
        // Separate compilation units can retain equivalent complete TPI types.
        // Validate every declaration; never select an arbitrary matching name.
        foreach (var structure in structures)
        {
            var current = ReadStructure(module, pdb, structure);
            if (agreed is not null && current != agreed)
                throw new NotSupportedException("Complete original expression utility declarations disagree on their callable ABI.");
            agreed = current;
        }
        return agreed! with { DeclarationOwner = agreed!.DeclarationOwner + ":complete-declarations=" + structures.Count };
    }
    private static NativeNvseExpressionAbi ReadStructure(NativePluginImageDeclaration module,
        NativePluginPdbTypes pdb, NativePluginPdbStructure structure)
    {
        if (structure.Extent is < 88 or > 4096 || structure.Extent % 4 != 0 || structure.Members.Count * 4 != structure.Extent)
            throw new NotSupportedException("Original expression client extent includes unowned packing, inheritance or fields.");
        var members = structure.Members.OrderBy(member => member.Offset).ToArray();
        for (var index = 0; index < members.Length; ++index)
            if (members[index].Offset != index * 4) throw new InvalidDataException("Original expression utility fields have a gap/overlap.");
        for (var index = 0; index < Names.Length; ++index)
        {
            var member = members[index]; var function = pdb.FunctionPointer(member.Type);
            if (!Names[index].Contains(member.Name, StringComparer.Ordinal) || function.Parameters != Parameters[index] ||
                function.Convention != (index == 0 ? 7 : 4))
                throw new NotSupportedException("Original expression utility member has no public callable signature owner: " + member.Name);
            if (index != 11 && pdb.AbiType(function.Return) != Returns[index])
                throw new NotSupportedException("Original utility return ABI differs from its native callable owner: " + member.Name);
            for (var argument = 0; argument < function.Arguments.Count; ++argument)
            {
                var kind = index == 4 && argument == 1 ? "uint32" :
                    (index is 17 or 20) && argument == 1 ? "uint8" : "pointer32";
                if (pdb.AbiType(function.Arguments[argument]) != kind)
                    throw new NotSupportedException("Original utility argument ABI differs from its native callable owner: " + member.Name);
            }
        }
        var array = pdb.FunctionPointer(members[11].Type);
        var arrayAbi = pdb.AbiType(array.Return);
        var arrayKind = NativeNvseArrayAccessor.Read(members[11].Name, arrayAbi,
            arrayAbi == "pointer32" && pdb.PointerTo(array.Return, "NVSEArrayVarInterface::Array") ? "NVSEArrayVarInterface::Array" :
            arrayAbi == "pointer32" ? "unowned-pointer-pointee" : null);
        var pointer = arrayKind == NativeNvseArrayAccessorKind.InternalObject;
        var callable = 88U; var reservedStart = 22;
        if (members.Length > 22 && members[22].Name == "ReportError")
        {
            var report = pdb.FunctionPointer(members[22].Type);
            if (report.Convention != 4 || report.Parameters != 3 || pdb.AbiType(report.Return) != "void" ||
                report.Arguments.Any(type => pdb.AbiType(type) != "pointer32"))
                throw new NotSupportedException("Original ReportError ABI is not its public fastcall signature.");
            callable = 92; reservedStart = 23;
        }
        for (var index = reservedStart; index < members.Length; ++index)
        {
            if (members[index].Name != "Reserved_" + (index - reservedStart + 1))
                throw new NotSupportedException("Original utility tail contains a callable extension without its native owner.");
            var reserved = pdb.FunctionPointer(members[index].Type);
            if (reserved.Convention != 0 || reserved.Parameters != 0 || pdb.AbiType(reserved.Return) != "void")
                throw new NotSupportedException("Original reserved utility field is not its source-declared empty cdecl slot.");
        }
        return new(module.Sha256, "matched-original-CodeView/TPI:ExpressionEvaluatorUtils:" + pdb.Sha256 + ":array-return=" + arrayKind,
            structure.Extent, callable, pointer);
    }
}
