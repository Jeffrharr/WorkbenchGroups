# TODO — and context for a new agent

## Where things stand

`WorkbenchGroups` (packageId `joof.workbenchgroups`) links crafting stations so they share
one bill list, plus a per-group round-robin ordering mode. Three commits on `main`,
built, symlinked into the game's Mods folder, 81 offline tests green, four live scenarios
green.

Read `DESIGN.md` first — it carries the reasoning. The short version of the load-bearing
facts, so they don't have to be re-derived from the decompile:

- `WorkGiver_DoBill` already follows the bench the pawn **walked to** (`giver`, stored as
  `job.targetA`), not `bill.billStack.billGiver`. That is the only reason sharing a
  `BillStack` is a small mod.
- A long tail of vanilla **hard-casts** `bill.billStack.billGiver`, so the shared stack
  must be owned by a real spawned bench (the *anchor*).
- Installation is a **field swap** on `Building_WorkTable.billStack`. It cannot be a
  patched property: `ITab_Bills` and Nice Bill Tab read the field, `WorkGiver_DoBill`
  reads the property.
- That forces the `Building_WorkTable.ExposeData` prefix/finalizer. Without it every
  member deep-saves the same bills and the save is corrupt on load.
- Round robin rotates at **job start**, never on iteration completion.

Layout: pure dependency-free logic in `Source/Core/` (compiled into the test project by
`<Compile Include>`, so tests run the shipped files); Verse adapter in `Source/`; Harmony
patches in `Source/Patches/`; dev-only probes and scenario steps in `Source/Probes/`,
excluded from the shipped DLL and compiled by `TestMod/`.

Build: `./build.sh`, then `./TestMod/build.sh` (the bridge references built output, so
order matters). Test offline: `./test.sh`. Test live:

```bash
Runner/run_test.sh --mod <repo> --mod <repo>/TestMod --no-profiler <scenario.json>
```

---

## 1. Replace the bench whitelist with a recipe-shaped gate (+ a small blacklist)

**Why this is the top item.** `BenchEligibility.GroupableBenchClasses` is currently a
whitelist of exactly two types. That excludes every modded workbench with a custom
`thingClass`, which is most interesting ones. It already bit us once: stoves are
`Building_WorkTable_HeatPush`, so the first cut of the rule silently excluded the mod's
own headline use case, and only writing the live test caught it.

**The better gate is not a blacklist of classes — it's a property of the recipes.**
`BillUtility.MakeNewBill` decides the `Bill` subclass purely from the `RecipeDef`:

```csharp
if (recipe.UsesUnfinishedThing)  return new Bill_ProductionWithUft(...);
if (recipe.mechResurrection)     return new Bill_ResurrectMech(...);
if (recipe.gestationCycles > 0)  return new Bill_ProductionMech(...);
if (recipe.formingTicks > 0)     return new Bill_Autonomous(...);
return new Bill_Production(...);
```

Every bill type we must refuse is therefore predictable from the def alone, with no
reference to the bench's C# class. So:

- **Primary gate (class-agnostic).** A def is groupable iff it is `is Building_WorkTable`
  and **every** recipe in `def.AllRecipes` would make a plain `Bill_Production` — i.e.
  none of `UsesUnfinishedThing`, `mechResurrection`, `gestationCycles > 0`,
  `formingTicks > 0`. This admits modded benches automatically, and excludes mech
  gestators and subcore encoders automatically *because of their recipes*, without naming
  their classes at all.
- **Safety net (the blacklist).** Also reject anything assignable to
  `Building_WorkTableAutonomous` (which covers `Building_MechGestator`). Cheap, and it
  guards a future vanilla class whose recipes don't give the danger away.
- **Escape hatch.** A mod setting holding a list of excluded `thingClass` names, so a
  player who hits a bad modded bench can exclude it without waiting on a patch.

**The residual risk to state plainly in `DESIGN.md`:** a modded bench class could still
hard-cast `billStack.billGiver` to its own type in its own code, and no def-level rule can
see that. The escape hatch is the answer, plus wrapping `BillGroupOps.Link` so a throw
during linking cannot leave benches half-swapped.

Touches `BenchEligibility.IsGroupableBench` / `IsGroupableDef` (the injector already
routes through the latter, so both stay in sync). Move the recipe predicate into
`Source/Core/` and unit-test it — it is pure boolean logic over four fields. Update the
Cecil test that currently pins `Building_WorkTable_HeatPush`'s shape: under the new rule
that class is no longer special, but a test should pin the four `RecipeDef` fields
instead, since the gate now depends on them.

---

## 2. Extend the live scenarios

Existing scenarios in `Tests/Scenarios/`: `link_smoke`, `round_robin_rotation`,
`overshoot_guard`, `shared_save_integrity`. Steps and probes live in `Source/Probes/`.

Two harness facts that shape everything below:

- `saveFile` resolves to `<harness>/Fixtures/<name>`, copied to `Saves/autostart.rws` at
  boot. **All scenarios in one run must share a `saveFile`**, so anything needing a
  different starting world is a separate run.
- The fixture (`minimal_colony.rws`) is a real permadeath colony in a bad way — one able
  colonist, starving, a medical emergency. Anything needing several simultaneous workers
  must spawn its own pawns (`SpawnPawn`), and `WbgSimulateBillStart` already falls back to
  reusing a colonist rather than failing.

### 2a. The reload round-trip — the biggest gap

Nothing has yet observed `CompBillGroup.PostMapInit` reinstalling the field redirect after
a load. The save half is measured (`wbg_duplicate_save_ids`), the load half is not.

The harness has no mid-scenario reload step, but **it doesn't need one**: run two
scenarios back to back and carry the save between them.

1. Phase A: link benches, add bills, `WbgSaveGame` → writes `Saves/wbg_roundtrip.rws`.
2. Copy that file to `<harness>/Fixtures/wbg_roundtrip.rws`.
3. Phase B: a scenario with `"saveFile": "wbg_roundtrip.rws"` whose steps are *only*
   probes — group size 2, bills visible at the second bench, and a new probe asserting the
   second bench's `billStack` is reference-equal to the anchor's.

Wrap the three in a script (`Tests/run_roundtrip.sh`). Phase B must run with the same
`--mod` set, since the save embeds its mod list. Add a probe for reference-equality of the
stacks; without it Phase B could pass on two benches that merely happen to hold equal
counts.

### 2b. Anchor handover

Needs a new step (`WbgDestroyBench`) — the harness has no destroy/despawn step. Destroy the
anchor and assert: the group survives, the bills survive, a new anchor is elected, and the
survivors' stacks still point at one object. This is the single most consequential
untested path: `HandOffAnchorIfNeeded` failing means blowing up one bench cancels every
craft in the group and deletes the orders.

Also worth covering with the same step: minify/reinstall (redirect withdrawn on despawn,
reinstalled on spawn), and a group falling to one member (dissolves, survivor keeps bills).

### 2c. Negative tests — every refusal path

`BillGroupOps.CanLink` has five refusal branches and none is exercised. Each needs a probe
exposing the refusal reason (add `wbg_last_refusal_code`, an int, set by a
`WbgTryLink` step that expects failure):

- mismatched recipe sets (link a stove to a tailoring bench)
- combined bills over `BillStack.MaxCount` (15)
- a bench holding an unshareable bill (a UFT recipe)
- a non-groupable bench class
- benches on different maps

These are cheap and they protect the rule in item 1 — after that change the refusal set
shifts, and a passing negative suite is what will show whether it shifted the way intended.

### 2d. The real craft loop

Currently the behavioural scenarios hold a `Wait` job rather than `DoBill`, so "pawns walk
to the right bench and make the right number of things" is unverified. To do it properly:
`SpawnPawn` several colonists, place a powered stove pair plus ingredients, then
`FastForward` and count products. Expect this to be flaky against the shared fixture —
budget time for a leaner purpose-built fixture, and see the harness's own `Fixtures/README.md`.

The specific claim worth proving here is the one round robin exists for: with three bills
and enough workers, products come out roughly 1/1/1 rather than 3/0/0.

### 2e. Ingredient-mute isolation

`IngredientMuteIsolation` is implemented and completely untested. A probe reading the
remembered per-(bill, bench) tick would let a scenario show that one bench failing an
ingredient search does not mute the bill at the other.

### 2f. Conflicting mods

`About.xml` declares seven `loadAfter` entries. Only the baseline load has ever run. At
minimum, one run with Hauler's Dream and Nice Bill Tab active, asserting the existing
probes still pass — those two are the ones that rewrite the surfaces we depend on.

---

## 3. Smaller loose ends

- `BillGroupGizmos.OrderingCommand` uses `TexCommand.RearmTrap` as a placeholder icon.
- The unlink gizmo acts on the whole selection; confirm that reads correctly when benches
  from two different groups are selected at once.
- `InFlightTracker.Reconcile` is called every 250 ticks from `BillGroupIndex`. Its cost has
  never been profiled — run a scenario under `--profiler` and quote the table before
  claiming it is free.
- No `Preview.png` in `About/`, and no `PublishedFileId.txt` (not published).
