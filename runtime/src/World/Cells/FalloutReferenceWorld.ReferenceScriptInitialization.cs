using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferenceScriptExtraInitializer? _sourceReferenceScriptInitializer;
    private FalloutReferenceScriptExtraInitializationInvocation? _enteredReferenceScriptInitialization;

    private void PrepareStandaloneReferenceScriptInitialization(FalloutActorPackageCommandFields fields)
    {
        if (_sourceReferenceScriptInitializer is not null)
            throw new InvalidOperationException("Player Script initialization was already composed for this source lifetime.");
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-reference-Script-initialization-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadReferenceScriptExtraInitializer(owned.FalloutExecutablePath, fields.Source);
        var initializer = new FalloutReferenceScriptExtraInitializer(source, records, fields, scene.ThreadContext,
            RequireActualReferenceScriptInitialization);
        _sourceReferenceScriptInitializer = initializer;
        // Cold fields retain the original initializer's returned/failed history.
        // The fresh owner binds it without repeating reference or Extra stores.
        if (_processCommonRestore is not null) return;
        var invocation = new FalloutReferenceScriptExtraInitializationInvocation(Guid.NewGuid(), fields.Actor,
            fields.Process, fields.Epoch, fields.Constructor, Environment.CurrentManagedThreadId);
        _enteredReferenceScriptInitialization = invocation;
        try { _ = initializer.Initialize(invocation); }
        finally { _enteredReferenceScriptInitialization = null; }
    }

    private void RequireActualReferenceScriptInitialization(FalloutReferenceScriptExtraInitializationInvocation invocation)
    {
        RequireStandaloneControlPublication();
        if (invocation != _enteredReferenceScriptInitialization || _enteredPackageCommand is not null ||
            invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _sourceReferenceScriptInitializer is not { } initializer || _sourcePackageRegistry is null ||
            !_sourcePackageFields.TryGetValue(invocation.Actor.Reference, out var fields) ||
            initializer.FieldsFactory != fields.Factory || initializer.Actor != fields.Actor ||
            invocation.Constructor != fields.Constructor || invocation.Process != fields.Process || invocation.Epoch != fields.Epoch)
            throw new InvalidOperationException("Reference Script child left its actual initialization, field owner, constructor or thread.");
        var actor = ActorProcesses.Read(invocation.Actor.Reference);
        var common = ProcessCommon.ReadSceneQueries(invocation.Actor.Reference, invocation.Epoch);
        var scene = CampaignStandalonePlayerScene;
        if (actor.Retired || actor.Source != invocation.Actor || actor.Epoch != invocation.Epoch ||
            common.Process != invocation.Process || common.Actor != invocation.Actor || common.Stack != scene.Stack ||
            scene.ThreadContext.Process != scene.Process || scene.ThreadContext.Source.Main != scene.Source.Main ||
            initializer.Source.Fields != fields.Source || fields.Source.Controls.Main != scene.Source.Main)
            throw new InvalidOperationException("Reference Script child lost its separate actual Actor, Common or scene/thread source owner.");
        _sourcePackageRegistry.RequireCurrent();
    }
}
