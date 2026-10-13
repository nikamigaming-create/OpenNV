using OpenNV.Runtime.Content;

internal static partial class CompiledScriptContracts
{
    private static void AdditionalResultScopes(string directory)
    {
        var selected = Path.Combine(directory, "additional-scopes"); Directory.CreateDirectory(selected);
        var code = Call(0x120c, U16(0));
        byte[] Compiled() => Join(Field("SCHR", Header(code, 0)), Field("SCDA", code));
        var uncompiled = Header([], 2); uncompiled[18] = 0;
        File.WriteAllBytes(Path.Combine(selected, "Bytecode.esm"), Join(Tes4(),
            Record("REFR", 0x90, Field("NAME", U32(1)), Field("XPPA", []), Field("INAM", U32(0)),
                Field("SCHR", Header([], 0)), Field("TNAM", U32(0)), Field("DATA", new byte[24])),
            Record("ACRE", 0x91, Field("XPPA", []), Compiled(), Field("TNAM", U32(0))),
            Record("ACHR", 0x92, Field("XPPA", []), Compiled(), Field("XPPA", [])),
            Record("PERK", 0x93, Field("PRKE", [2, 1, 0]), Compiled(), Field("PRKF", []),
                Field("PRKE", [2, 1, 1]), Compiled(), Field("PRKF", [])),
            Record("PERK", 0x94, Field("PRKE", [2, 1, 0]), Compiled()),
            Record("PERK", 0x95, Field("PRKE", [2, 1]), Compiled(), Field("PRKF", [])),
            Record("TERM", 0x96, Field("ITXT", Text("Entry")), Field("SCHR", uncompiled),
                Field("SCTX", Text("Diagnostic source must never execute.")), Field("SCRO", U32(0x14))),
            Record("REFR", 0x97, Field("XPPA", []), Field("SCHR", Header([], 2)), Field("SCRO", U32(0x14))),
            Record("REFR", 0x98, Field("XPPA", []), Field("SCHR", Header(code, 0)))));
        using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
        var patrol = FalloutScriptScope.PatrolResult(records.GetEffective(Key(0x90)));
        Require(patrol.Kind == FalloutScriptScopeKind.PatrolPointResult && patrol.FieldStart == 2 && patrol.FieldCount == 2 &&
            !patrol.ValidateAbsentBody(), "Empty compiled patrol result lost its original range or terminator.");
        var creature = FalloutScriptScope.PatrolResult(records.GetEffective(Key(0x91)));
        Require(FalloutCompiledScriptProgram.Read(creature.Source, creature, false).CodeBytes == code.Length,
            "Creature patrol result lost its actual compiled body.");
        var perk = records.GetEffective(Key(0x93));
        var first = FalloutScriptScope.PerkResult(perk, 0); var second = FalloutScriptScope.PerkResult(perk, 1);
        Require(first.Kind == FalloutScriptScopeKind.PerkEntryResult && first.ResultOrdinal == 0 && second.ResultOrdinal == 1 &&
            first.ScopeSha256 != second.ScopeSha256 && FalloutCompiledScriptProgram.Read(perk, first, false).ProgramSha256 !=
            FalloutCompiledScriptProgram.Read(perk, second, false).ProgramSha256,
            "Identical perk result bytes collapsed their reader-owned entry ordinals.");
        var terminal = FalloutScriptScope.TerminalResult(records.GetEffective(Key(0x96)), 0);
        Require(terminal.ValidateAbsentBody() && !terminal.Compiled,
            "Uncompiled compiler metadata was promoted to instructions or treated as an empty compiled body.");
        Reject(() => FalloutCompiledScriptProgram.Read(terminal.Source, terminal, false));
        Reject(() => FalloutScriptScope.PatrolResult(records.GetEffective(Key(0x92))));
        Reject(() => FalloutScriptScope.PerkResult(records.GetEffective(Key(0x94)), 0));
        Reject(() => FalloutScriptScope.PerkResult(records.GetEffective(Key(0x95)), 0));
        Reject(() => FalloutScriptScope.PerkResult(perk, 2));
        Reject(() => FalloutScriptScope.PatrolResult(records.GetEffective(Key(0x97))).ValidateAbsentBody());
        Reject(() => FalloutScriptScope.PatrolResult(records.GetEffective(Key(0x98))).ValidateAbsentBody());
        Console.WriteLine("OPENNV_COMPILED_ADDITIONAL_SCOPES_PASS patrol=true perkOrdinals=true emptyCompiled=true uncompiledVisible=true missingDeclaredBodyRefused=true execution=UNEXECUTED");
    }
}
