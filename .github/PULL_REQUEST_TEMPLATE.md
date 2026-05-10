## What does this PR do?

<!-- A brief summary of the change and why it's needed. -->

## Linked Issue

Closes #

## Type of change

- [ ] Bug fix
- [ ] New feature / gameplay addition
- [ ] Simulation / netcode change
- [ ] Performance improvement
- [ ] Refactor / internal improvement
- [ ] Documentation update
- [ ] Other (describe below)

## Determinism check

_For any simulation code changes:_

- [ ] No `float`/`double` introduced in simulation paths
- [ ] No `System.Random` used in simulation paths
- [ ] No real-time values (`DateTime.Now`, frame delta) in simulation
- [ ] Dictionary/collection iteration order is deterministic
- [ ] N/A — this change does not touch simulation code

## Testing

- [ ] `dotnet test CorditeWars.sln` passes
- [ ] Tested in-game (describe scenario below)
- [ ] Tested LAN multiplayer for sync (if simulation change)

## Screenshots / recordings

<!-- If this is a visual change, include before/after screenshots or a recording. -->

## Breaking changes?

- [ ] No
- [ ] Yes — describe save/replay/network compatibility impact below

## Notes for reviewer

<!-- Anything tricky, performance implications, or simulation edge cases to watch for. -->
