# TODO — and context for a new agent

## Where things stand

`WorkbenchGroups` (packageId `joof.workbenchgroups`) links crafting stations so they share
one bill list, plus a per-group round-robin ordering mode. Built, symlinked into the game's
Mods folder, 109 offline tests green, seven live scenarios green.

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
- Eligibility is decided from **recipes, not bench classes**: a bench is groupable if at
  least one of its recipes would make a plain `Bill_Production`
  (`BillUtility.MakeNewBill` picks the subclass from the `RecipeDef` alone). "At least one"
  and not "every one" — the strict form excludes every crafting bench in the game, which
  the `eligibility_gate` census measures. The per-bill half is enforced by
  `Patch_BillStack_AddBill`.

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

## 1. Extend the live scenarios

Existing scenarios in `Tests/Scenarios/`: `eligibility_gate`, `link_smoke`,
`round_robin_rotation`, `overshoot_guard`, `shared_save_integrity` — that glob is a valid
one-load suite. The two-load `roundtrip/` pair is deliberately not in it (different fixture);
run it with `Tests/run_roundtrip.sh`. Steps and probes live in `Source/Probes/`.

The reload round-trip pattern is reusable: the harness has no mid-scenario reload step and
does not need one — write a save in one run, name it as the next run's `saveFile`. See
`Tests/run_roundtrip.sh`.

Two harness facts that shape everything below:

- `saveFile` resolves to `<harness>/Fixtures/<name>`, copied to `Saves/autostart.rws` at
  boot. **All scenarios in one run must share a `saveFile`**, so anything needing a
  different starting world is a separate run.
- The fixture (`minimal_colony.rws`) is a real permadeath colony in a bad way — one able
  colonist, starving, a medical emergency. Anything needing several simultaneous workers
  must spawn its own pawns (`SpawnPawn`), and `WbgSimulateBillStart` already falls back to
  reusing a colonist rather than failing.

### 1a. Anchor handover

Needs a new step (`WbgDestroyBench`) — the harness has no destroy/despawn step. Destroy the
anchor and assert: the group survives, the bills survive, a new anchor is elected, and the
survivors' stacks still point at one object. This is the single most consequential
untested path: `HandOffAnchorIfNeeded` failing means blowing up one bench cancels every
craft in the group and deletes the orders.

Also worth covering with the same step: minify/reinstall (redirect withdrawn on despawn,
reinstalled on spawn), and a group falling to one member (dissolves, survivor keeps bills).

### 1b. Negative tests — every refusal path

`BillGroupOps.CanLink` has five refusal branches and none is exercised. Two newer refusals
belong in the same suite: `Patch_BillStack_AddBill` rejecting an unfinished-thing bill added
to a grouped bench (unit-tested predicate, never seen in play), and `BillGroupOps.Link`'s
rollback, which was written against a throw we cannot reproduce on demand and has never run.
A step that links a bench whose comp is rigged to throw would exercise it. Each needs a probe
exposing the refusal reason (add `wbg_last_refusal_code`, an int, set by a
`WbgTryLink` step that expects failure):

- mismatched recipe sets (link a stove to a tailoring bench)
- combined bills over `BillStack.MaxCount` (15)
- a bench holding an unshareable bill (a UFT recipe)
- a non-groupable bench class
- benches on different maps

These are cheap, and they are what would have caught the eligibility change shifting the
refusal set in a way nobody intended.

### 1c. The real craft loop

Currently the behavioural scenarios hold a `Wait` job rather than `DoBill`, so "pawns walk
to the right bench and make the right number of things" is unverified. To do it properly:
`SpawnPawn` several colonists, place a powered stove pair plus ingredients, then
`FastForward` and count products. Expect this to be flaky against the shared fixture —
budget time for a leaner purpose-built fixture, and see the harness's own `Fixtures/README.md`.

The specific claim worth proving here is the one round robin exists for: with three bills
and enough workers, products come out roughly 1/1/1 rather than 3/0/0.

### 1d. Ingredient-mute isolation

`IngredientMuteIsolation` is implemented and completely untested. A probe reading the
remembered per-(bill, bench) tick would let a scenario show that one bench failing an
ingredient search does not mute the bill at the other.

### 1e. Conflicting mods

`About.xml` declares seven `loadAfter` entries. Only the baseline load has ever run. At
minimum, one run with Hauler's Dream and Nice Bill Tab active, asserting the existing
probes still pass — those two are the ones that rewrite the surfaces we depend on.

---

## 2. Smaller loose ends

- `BillGroupGizmos.OrderingCommand` uses `TexCommand.RearmTrap` as a placeholder icon.
- The unlink gizmo acts on the whole selection; confirm that reads correctly when benches
  from two different groups are selected at once.
- `InFlightTracker.Reconcile` is called every 250 ticks from `BillGroupIndex`. Its cost has
  never been profiled — run a scenario under `--profiler` and quote the table before
  claiming it is free.
- No `Preview.png` in `About/`, and no `PublishedFileId.txt` (not published).
