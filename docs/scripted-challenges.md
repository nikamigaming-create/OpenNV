# Scripted challenges

`FalloutChallenges` owns winning CHAL definitions and persistent player progress.
`UnlockChallenge` enables the selected source challenge. `IncrementScriptedChallenge`
admits the scripted type, respects locked/already-completed state and advances
one point. Completion preserves once-only state, subtracts recurring thresholds,
and updates the source-filtered challenge-completion statistic cascade.

Completion notifications use the existing HUD queue and the owned executable's
message declaration and duration. An attached reward script remains an explicit
retained failure until its immediate player-script execution has an owner;
its consumed increment is never silently replayed. Snapshots validate against
the winning source hashes and reject impossible progress.

Focused contracts and the selected CG02 citizenship audit pass: completion1,
statistic27 count1, recurring challenge-of-challenges progress1, cold continuation
and duplicate increment without replay. This isolated result-entry audit does
not establish ordinary birthday completion, other challenge event families,
HUD/audio pixels or matched retail parity.
