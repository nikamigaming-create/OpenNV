# Quest-script elapsed time

The quest recurrence countdown and `GetSecondsPassed` measure separate things.
The countdown selects at most one invocation per caller frame and retains late
overshoot. After the initial zero-elapsed dispatch, every new gameplay frame
contributes to elapsed time, including frames whose countdown was already due.
Completing an invocation clears its elapsed accumulator. Initial linking,
globally disabled recurrence and the established due-frame order are retained.

Previously, a carried negative countdown returned before accruing the next
frame's time. Source quests with intervals below frame time repeatedly received
zero from `GetSecondsPassed`. Their timers took longer than the advancing camera
and effect clocks. The general repair retains scheduling debt while accruing
those later frame deltas. Existing snapshot fields preserve the overdue phase
and elapsed bits; no timer or authored speech wait is inserted.

Synthetic checks cover fast intervals, variable and long frames, once-per-frame
dispatch, modal GameMode suppression, following-invocation result order, atomic
overflow refusal and bit-identical cold continuation with overdue debt.
The selected owned audit reads the winning quest/script and source stage
variable writes, then runs its real GameMode through the shared scheduler at
30, 60 and 90 Hz and with a 750 ms caller frame. Its result target is observed
inside an isolated fixture. It checks the source timer, once-only result,
modal boundary and serialized cold continuation; it does not execute the
result stage's scene effects or prepare a playable quest checkpoint.

The fresh ordinary TTW observation that motivated this repair already shows
Camera1st moving through the owned player-package path. Correct elapsed time
can shorten that package's authored lifetime. Ordinary visible gurney departure,
matched speech/effect timing, complete cold actors and retail parity remain
unverified and require separate evidence.

```powershell
dotnet run --project contract-tests/FalloutPluginRuntimeProbe --configuration Release -- --script-contracts
dotnet run --project contract-tests/FalloutPluginRuntimeProbe --configuration Release -- --audit-quest-clock ttw 'D:/TTW/Installed' 'D:/SteamLibrary/steamapps/common/Fallout New Vegas' CG00 90 100 <dependency-folders>
```

Owned files remain read-only, private observations stay out of the repository,
and the audit does not record frames or control an ordinary game session.
