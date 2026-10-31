using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutConditionRuntime
{
    // This binds parsed script invocations to the same function bodies as CTDA.
    // It is not an SCDA decoder/VM: compiled instruction execution is separate.
    internal FalloutScriptFunction? ScriptFunction(string operation, FalloutPluginRecord declaration,
        Func<FalloutFormKey> subject, Func<string, FalloutFormKey> form,
        Func<string, (FalloutFormKey Owner, uint Index)> variable, bool explicitReceiver = false)
    {
        double Call(Function function, FalloutFormKey? argument = null, uint number = 0, uint second = 0, bool scoped = true)
        {
            if (!scoped && explicitReceiver) RequireReference(subject());
            return Evaluate(new FalloutConditionQuery(new(declaration, 0, 0, (ushort)function, number, second, 0, 0), argument),
                new(scoped ? subject() : declaration.FormKey));
        }
        FalloutFormKey FormValue(FalloutScriptArgument argument) => argument.Value.Kind == FalloutScriptValueKind.Number
            ? records.RuntimeFormKey(Index(argument.Number)) : argument.Value.FormKey(records);
        static uint Index(double value) => double.IsFinite(value) && value >= 0 && value <= uint.MaxValue && value == Math.Truncate(value)
            ? (uint)value : throw new InvalidDataException("Script function index is not an unsigned integer.");
        FalloutScriptFunction None(Function id, bool scoped = true) => new([], _ => Call(id, scoped: scoped)) { ReadOnly = id != Function.GetRandomPercent };
        FalloutScriptFunction Form(Function id, bool scoped = true) => new([FalloutScriptArgumentKind.Value],
            arguments => Call(id, FormValue(arguments[0]), scoped: scoped))
        { ReadOnly = true };
        FalloutScriptFunction Quest(Function id) => new([FalloutScriptArgumentKind.Identifier],
            arguments => Call(id, form(arguments[0].Identifier!), scoped: false))
        { ReadOnly = true };
        FalloutScriptFunction IndexedQuest(Function id) => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number],
            arguments => Call(id, form(arguments[0].Identifier!), second: Index(arguments[1].Number), scoped: false))
        { ReadOnly = true };
        FalloutScriptFunction Sex(Function id, bool scoped) => new([FalloutScriptArgumentKind.Identifier], arguments =>
            Call(id, number: arguments[0].Identifier!.ToLowerInvariant() switch
            {
                "male" or "0" => 0,
                "female" or "1" => 1,
                _ => throw new InvalidDataException("Script sex argument is not Male or Female."),
            }, scoped: scoped))
        { ReadOnly = true };
        return operation switch
        {
            "getisid" => Form(Function.GetIsID),
            "getisreference" => Form(Function.GetIsReference),
            "getisrace" => Form(Function.GetIsRace),
            "getinfaction" => Form(Function.GetInFaction),
            "getfactionrank" => Form(Function.GetFactionRank),
            "getisvoicetype" => Form(Function.GetIsVoiceType),
            "isinlist" => Form(Function.IsInList),
            "getiscurrentpackage" => Form(Function.GetIsCurrentPackage),
            "islastidleplayed" => Form(Function.IsLastIdlePlayed),
            "getitemcount" => Form(Function.GetItemCount),
            "isequipped" or "getequipped" => Form(Function.GetEquipped),
            "getdistance" => Form(Function.GetDistance),
            "getpos" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                Call(Function.GetPos, number: arguments[0].Identifier!.ToUpperInvariant() switch
                {
                    "X" => 'X',
                    "Y" => 'Y',
                    "Z" => 'Z',
                    _ => throw new InvalidDataException("GetPos axis is not X, Y or Z."),
                }))
            { ReadOnly = true },
            "getinsamecell" => Form(Function.GetInSameCell),
            "getincell" => Form(Function.GetInCell),
            "getinzone" => Form(Function.GetInZone),
            "getissex" => Sex(Function.GetIsSex, true),
            "getpcissex" => Sex(Function.GetPCIsSex, false),
            "getactorvalue" or "getav" => new([FalloutScriptArgumentKind.Identifier], arguments =>
                Call(Function.GetActorValue, number: ScriptActorValueIndex(arguments[0].Identifier!)))
            { ReadOnly = true },
            "getsitting" => None(Function.GetSitting),
            "getsleeping" => None(Function.GetSleeping),
            "gettalkedtopc" => None(Function.GetTalkedToPC),
            "isgreetingplayer" => None(Function.IsGreetingPlayer),
            "istalking" => None(Function.IsTalking),
            "getsandman" => None(Function.GetSandman),
            "getcannibal" => None(Function.GetCannibal),
            "getdisabled" => None(Function.GetDisabled),
            "getdead" => None(Function.GetDead),
            "getunconscious" => None(Function.GetUnconscious),
            "getknockedstate" => None(Function.GetKnockedState),
            "isessential" => None(Function.IsEssential),
            "gethealthpercentage" => None(Function.GetHealthPercentage),
            "isincombat" => None(Function.IsInCombat),
            "isininterior" => None(Function.IsInInterior),
            "getlocked" => None(Function.GetLocked),
            "getlocklevel" => None(Function.GetLockLevel),
            "getopenstate" => None(Function.GetOpenState),
            "getmapmarkervisible" => None(Function.GetMapMarkerVisible),
            "isrunning" => None(Function.IsRunning),
            "issneaking" => None(Function.IsSneaking),
            "isweaponout" => None(Function.IsWeaponOut),
            "getweaponanimtype" => None(Function.GetWeaponAnimType),
            "getcurrenttime" => None(Function.GetCurrentTime, false),
            "getrandompercent" => None(Function.GetRandomPercent, false),
            "getglobalvalue" => Quest(Function.GetGlobalValue),
            "getvampire" => None(Function.GetVampire, false),
            "isxbox" => None(Function.IsXBox, false),
            "isps3" => None(Function.IsPS3, false),
            "iswin32" => None(Function.IsWin32, false),
            "getquestrunning" => Quest(Function.GetQuestRunning),
            "getstage" => Quest(Function.GetStage),
            "getquestcompleted" => Quest(Function.GetQuestCompleted),
            "getstagedone" => IndexedQuest(Function.GetStageDone),
            "getobjectivecompleted" => IndexedQuest(Function.GetObjectiveCompleted),
            "getobjectivedisplayed" => IndexedQuest(Function.GetObjectiveDisplayed),
            "getquestvariable" or "getscriptvariable" => new([FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Identifier],
                arguments =>
                {
                    var slot = variable(arguments[0].Identifier! + "." + arguments[1].Identifier!);
                    return Call(Function.GetScriptVariable, slot.Owner, second: slot.Index, scoped: false);
                })
            { ReadOnly = true },
            _ => null,
        };
    }

    private static uint ScriptActorValueIndex(string name) =>
        checked((uint)Definitions.FalloutActorValueNames.Parse(name));
}
