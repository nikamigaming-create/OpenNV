using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal void PrepareStandalonePlayerLateActorTime(FalloutGameTime calendar,
        FalloutNumericIniSettings settings, Action requireLifetime)
    {
        RequireStandaloneControlPublication();
        var scene = CampaignStandalonePlayerScene;
        var following = _standaloneFollowingSource ??
            throw new InvalidOperationException("Actor-time construction has no actual following source.");
        var reference = _sourceReferenceScriptInitializer ??
            throw new InvalidOperationException("Actor-time construction has no actual reference flag owner.");
        var owned = records.OwnedSource ??
            throw new NotSupportedException("source-Player-Actor-time-owned-executable-unbound");
        var source = FalloutExecutableStringTable.ReadPlayerLateActorTime(owned.FalloutExecutablePath, following);
        var prefix = FalloutExecutableStringTable.ReadPlayerLateFramePrefix(owned.FalloutExecutablePath, following);
        var actor = ActorProcesses.Read(_enginePlayer);
        var current = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        void Living()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            requireLifetime();
            if (_sourceReferenceScriptInitializer != reference || _standaloneFollowingSource != following)
                throw new InvalidOperationException("Actor-time left its actual source reference or following lifetime.");
        }
        ActorUpdates.BindPlayerFrameGlobalAi(source, Living);
        calendar.BindActorFrameCalendar(source, scene.Stack, scene.Process, Living);
        scene.BindLateActorTime(source, current, ProcessCommon, ProcessRuntime, ActorUpdates,
            reference, calendar, ProcessRuntime.RequirePlayerControlTimer(), settings, Living);
        scene.BindLateFramePrefix(prefix, actor.Source);
    }
}
