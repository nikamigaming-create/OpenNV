# Creature package movement

Creature package selection follows winning PKID priority and source conditions.
Follow and Dialogue declarations may omit PLDT; requiring a start location for
every package incorrectly rejected ordinary companion packages. The admitted
Follow procedure reads its target and distance from PTDT. Creature Dialogue can
approach its target and request the shared conversation owner. Reached start/end
locations, other procedures, unsupported conditions and event effects remain
explicit failures.

Native movement shares the actor's source BBX envelope, KF root displacement,
turn/speed configuration, source navigation and Godot collision solver with
combat. Package clocks remain separate from ambient idles and combat. Gameplay
pause, restraint, disabled references and combat suspend package movement.
ResetAI/EVP reach both creature and humanoid package owners. Conversation history
and package motion persist in save v15; v14 Aid saves remain readable. A moved
reference's dialogue cell comes from its live placement, not source ancestry.

The native owned-data fixture exercises movement, source stopping distance and
cold position/clock continuation on an explicitly synthetic floor. ED-E covers
the ordinary 330-unit package and source scaled health. Contract checks cover the
500-unit alternative, priority, missing/unsupported targets, invalid saves and
level clamps. These checks do not establish ordinary repair/recruitment, door
transfer, companion combat, retail AI timing or final-eye acceptance.

Run the owned package check with the ReferenceScriptContractProbe's
`--companion-packages` option. NativeActorCombatAudit accepts owned Data followed
by `--follow <ACRE runtime hex> <PACK runtime hex> [MoveTo reference runtime hex]`
for the isolated motion check. The optional reference uses the real MoveTo owner
before admission, so a relocated actor uses its current cell's encounter policy.
