using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    private void Store(FieldScope scope, FalloutPackageCommandField field, uint? bits = null,
        FalloutFormKey? form = null, Guid? @object = null, Guid? row = null)
    {
        Guard(scope);
        var store = new FalloutPackageCommandFieldStore(field, Next(), bits, form, @object, row);
        _values = ApplyStore(_values, store, scope.PublishedRows, scope.EventFactory);
        scope.Stores.Add(store);
    }
    private static FalloutPackageCommandFieldValues ApplyStore(FalloutPackageCommandFieldValues value,
        FalloutPackageCommandFieldStore store, IReadOnlyList<FalloutPackageScriptEventRow?>? publishedRows, Guid? eventFactory)
    {
        if (!Enum.IsDefined(store.Field) || store.Changed < 1 || store.Object == Guid.Empty || store.Row == Guid.Empty)
            throw new InvalidDataException("Package field store lost its exact source field or logical object identity.");
        ValidateForm(store.Form);
        var bits = store.Bits;
        uint Word()
        {
            if (bits is null || store.Form is not null || store.Object is not null || store.Row is not null)
                throw new InvalidDataException("Package field store changed a full independent source word payload.");
            return bits.Value;
        }
        FalloutFormKey? Form()
        {
            if (bits is not null || store.Object is not null || store.Row is not null)
                throw new InvalidDataException("Package form field borrowed another field's payload.");
            return store.Form;
        }
        switch (store.Field)
        {
            case FalloutPackageCommandField.PositionX: return value with { Position = value.Position with { X = Word() } };
            case FalloutPackageCommandField.PositionY: return value with { Position = value.Position with { Y = Word() } };
            case FalloutPackageCommandField.PositionZ: return value with { Position = value.Position with { Z = Word() } };
            case FalloutPackageCommandField.RotationZ: return value with { RotationZ = Word() };
            case FalloutPackageCommandField.PositionMode:
                var mode = Word(); if (mode > byte.MaxValue) throw new InvalidDataException("Position mode widened its source byte.");
                return value with { PositionMode = (byte)mode };
            case FalloutPackageCommandField.ParentCell: return value with { ParentCell = Form() };
            case FalloutPackageCommandField.ScriptConstruction:
                if (bits is not null || store.Object is null || store.Form is not null || store.Row is not null ||
                    value.ScriptExtra is not null || value.PendingScriptExtra is not null)
                    throw new InvalidDataException("Script Extra construction reused an object or invented current membership.");
                return value with { PendingScriptExtra = new(store.Object.Value, null, null, null, []) };
            case FalloutPackageCommandField.ScriptPresence:
                if (bits != 1 || store.Object is null || store.Form is not null || store.Row is not null || value.ScriptExtra is not null ||
                    value.PendingScriptExtra is not { } pendingScript || pendingScript.Identity != store.Object)
                    throw new InvalidDataException("Script Extra attachment lost its actual returned constructor object.");
                return value with { ScriptExtra = pendingScript, PendingScriptExtra = null };
            case FalloutPackageCommandField.ScriptForm:
                var script = Form();
                if (value.PendingScriptExtra is { } constructingScript)
                    return value with { PendingScriptExtra = constructingScript with { Script = script } };
                if (value.ScriptExtra is null) throw new InvalidDataException("Script setter has no actual Extra row.");
                return value with { ScriptExtra = value.ScriptExtra with { Script = script } };
            case FalloutPackageCommandField.ScriptConstructorEventList:
                if (value.PendingScriptExtra is not { } constructingExtra || bits is not null || store.Form is not null ||
                    store.Object is not null || store.Row is not null)
                    throw new InvalidDataException("Script Extra constructor event-list store borrowed a current publication.");
                return value with { PendingScriptExtra = constructingExtra with { EventList = null, EventFactory = null, Rows = [] } };
            case FalloutPackageCommandField.ScriptEventList:
                if (value.ScriptExtra is null || bits is not null || store.Form is not null || store.Row is not null ||
                    (store.Object is null) != (eventFactory is null) || publishedRows is null)
                    throw new InvalidDataException("Event-list setter lost its distinct Script Extra or current list factory.");
                ValidateRows(publishedRows);
                return value with
                {
                    ScriptExtra = value.ScriptExtra with
                    { EventList = store.Object, EventFactory = eventFactory, Rows = Array.AsReadOnly(publishedRows.ToArray()) }
                };
            case FalloutPackageCommandField.EventMask:
                if (value.ScriptExtra is not { EventList: not null } extra || store.Row is null || bits is null ||
                    store.Form is not null || store.Object is not null || !extra.Rows.Any(row => row?.Identity == store.Row))
                    throw new InvalidDataException("Package marker invented an event row or changed an independent payload.");
                return value with
                {
                    ScriptExtra = extra with
                    {
                        Rows = Array.AsReadOnly(extra.Rows.Select(row =>
                    row?.Identity == store.Row ? row with { Mask = bits.Value } : row).ToArray())
                    }
                };
            case FalloutPackageCommandField.SavedConstruction:
                if (bits is not null || store.Object is null || store.Form is not null || store.Row is not null ||
                    value.SavedPosition is not null || value.PendingSavedPosition is not null)
                    throw new InvalidDataException("Saved-position construction reused membership or another Extra object.");
                return value with { PendingSavedPosition = new(store.Object.Value, null, new(0, 0, 0), 0) };
            case FalloutPackageCommandField.SavedPresence:
                if (bits != 1 || store.Object is null || store.Form is not null || store.Row is not null || value.SavedPosition is not null ||
                    value.PendingSavedPosition is not { } pendingSaved || pendingSaved.Identity != store.Object)
                    throw new InvalidDataException("Saved-position attachment lost its actual returned constructor object.");
                return value with { SavedPosition = pendingSaved, PendingSavedPosition = null };
            case FalloutPackageCommandField.SavedSpace:
                return StoreSaved(value, Saved(value) with { Space = Form() });
            case FalloutPackageCommandField.SavedX:
                return StoreSaved(value, Saved(value) with { Position = Saved(value).Position with { X = Word() } });
            case FalloutPackageCommandField.SavedY:
                return StoreSaved(value, Saved(value) with { Position = Saved(value).Position with { Y = Word() } });
            case FalloutPackageCommandField.SavedZ:
                return StoreSaved(value, Saved(value) with { Position = Saved(value).Position with { Z = Word() } });
            case FalloutPackageCommandField.SavedRotationZ:
                return StoreSaved(value, Saved(value) with { RotationZ = Word() });
            case FalloutPackageCommandField.View:
                var view = Word(); if (view > byte.MaxValue) throw new InvalidDataException("Animation view widened its source byte.");
                return value with { View = (byte)view };
            case FalloutPackageCommandField.AnimationOverride:
                return value with { AnimationOverride = Context() };
            case FalloutPackageCommandField.FirstPersonAnimation:
                return value with { FirstPersonAnimation = Context() };
            default: throw new InvalidDataException("Unknown source package command field store.");
        }
        Guid? Context()
        {
            if (bits is not null || store.Form is not null || store.Row is not null)
                throw new InvalidDataException("Animation context borrowed another source payload.");
            return store.Object;
        }
    }
    private static FalloutPackageSavedPosition Saved(FalloutPackageCommandFieldValues value) => value.PendingSavedPosition ?? value.SavedPosition ??
        throw new InvalidDataException("Saved-position setter has no actual Extra instance.");
    private static FalloutPackageCommandFieldValues StoreSaved(FalloutPackageCommandFieldValues value, FalloutPackageSavedPosition saved) =>
        value.PendingSavedPosition is not null ? value with { PendingSavedPosition = saved } : value with { SavedPosition = saved };
    private static void ValidateForm(FalloutFormKey? form)
    {
        if (form is { } key && (string.IsNullOrWhiteSpace(key.OwnerPlugin) || key.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Package command field has an unowned form identity.");
    }
    private static void ValidateRows(IReadOnlyList<FalloutPackageScriptEventRow?> rows)
    {
        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null) continue;
            ValidateForm(row.Form);
            if (row.Identity == Guid.Empty || !ids.Add(row.Identity)) throw new InvalidDataException("Event-list row identity is missing or duplicated.");
        }
    }
    private static FalloutPackageCommandFieldValues CloneValues(FalloutPackageCommandFieldValues value) => value with
    {
        ScriptExtra = value.ScriptExtra is { } extra ? extra with { Rows = Array.AsReadOnly(extra.Rows.ToArray()) } : null,
        PendingScriptExtra = value.PendingScriptExtra is { } pending ? pending with { Rows = Array.AsReadOnly(pending.Rows.ToArray()) } : null
    };
    private static FalloutPackageCommandFieldOperation CloneOperation(FalloutPackageCommandFieldOperation value) => value with
    {
        Stores = Array.AsReadOnly(value.Stores.ToArray()),
        PublishedRows = value.PublishedRows is { } rows ? Array.AsReadOnly(rows.ToArray()) : null,
        EventMark = value.EventMark is { } mark ? mark with { Stores = Array.AsReadOnly(mark.Stores.ToArray()) } : null,
        SavedPositionCall = value.SavedPositionCall is { } saved ? saved with { Stores = Array.AsReadOnly(saved.Stores.ToArray()) } : null,
        SavedPositionInitialization = value.SavedPositionInitialization is { } initialization ? initialization with
        { Prefix = initialization.Prefix with { Dependencies = initialization.Prefix.Dependencies is { } dependencies ? Array.AsReadOnly(dependencies.ToArray()) : null } } : null
    };
}
