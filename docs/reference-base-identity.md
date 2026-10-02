# Reference base identity

Dialogue GetIsID resolves its source speaker, listener or explicit reference
before comparing the requested base form. The engine's player reference maps to
its winning NPC base, independently of speaker traits, voice selection or current
race/appearance. NPC listeners and explicit placed objects read their own winning
NAME. The same C# source identity owner serves ordinary script GetIsID calls;
typed forms and finite integral runtime-form arguments are supported.

This fixes source dialogue selection where an earlier candidate is intended for
an NPC listener and must be false for the player. An unused candidate can no
longer raise a missing player-target identity fault and block later eligible INFO.
There is no topic, actor, stage or mod-name special case.

Synthetic checks cover player, speaker, NPC listener, explicit player/object,
missing or invalid references and unsupported contexts. Actual source-script
activation verifies qualified player and unqualified object queries through the
normal dispatcher. The selected installed TTW audit checks its authored
player-target condition without a fallback player/speaker substitution.

A separate ordinary genuine stage80 Continue, door activation and following
passes the birthday identity fault and reaches CG02 stage7. The next source
speech fails on an unbound owned-font glyph for a curly apostrophe. Two telemetry
publication interval failures remain recorded. Recording was off; campaign
completion and matched retail parity remain unverified.

Leveled placements with a distinct selected permanent base still require that
identity owner. Missing source layouts and unsupported contexts fail visibly;
the query does not construct an actor or grant gameplay outcomes.
