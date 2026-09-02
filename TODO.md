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

## 1. Per-bill linkage — relax the link rules, mark the exceptions

**The idea.** Today every refusal in `BillGroupOps.CanLink` refuses the *link*. Instead,
link the benches and decide per *bill* which members may work it. A bill that only some
members can make stays in the shared list, marked with a broken-chain icon, and only the
capable benches ever pick it up. The player linked these benches deliberately, so "you get
what you selected, and the list tells you which orders are special" beats a flat refusal.

This is a better shape than the same-recipe-set rule it replaces, because the gate lands on
the bill rather than on the group. It is **not** a loosening of a constant — see below for
which rules it can and cannot cover.

### Which of the five refusals this actually covers

`WorkGiver_DoBill.StartOrResumeBillJob` decides everything on the normal path from `giver` —
the bench the pawn walked to — and only the unfinished-thing path routes through
`bill.billStack.billGiver`, which is the anchor. That split is what determines the answer:

| `CanLink` refusal | Becomes per-bill? | Why |
|---|---|---|
| Mismatched recipe sets | **Yes** | Ingredient search, job target and work stats are all `giver`-relative on the normal path, so a plain `Bill_Production` owned by the anchor and worked at another member is already correct. Only the *selection* needs gating. |
| Unshareable (UFT) bill | **No** | `FinishUftJob` resolves the unfinished item through `bill.billStack.billGiver` (WorkGiver_DoBill.cs:175,180), so a UFT bill in a shared stack is broken *however* it was selected. Pinning it changes who starts it, not where its unfinished item is looked for. Would need the bill to keep its own stack, and `ITab_Bills` reads one `billStack` object, so there is nowhere to put it. Stays a hard refusal. |
| Over `BillStack.MaxCount` (15) | **No** | A vanilla cap on the stack itself, not a property of any one bill. |
| Non-groupable bench class | **No** | About whether anchoring is safe at all, not about sharing. |
| Different maps | **No** | The anchor must be a spawned bench on the same map. |

So the feature is really "**mismatched recipe sets become per-bill**", and the broken-chain
marker is the UI for it. Worth saying plainly in `DESIGN.md`, because "an edge case for each
rule" is the natural expectation and only one rule can have one.

### The mechanism, which is cheaper than it looks

Both hooks already exist and both already only ever turn a yes into a no:

1. `Patch_WorkGiver_DoBill_JobOnThing` already brackets the whole scan with a
   prefix/finalizer pair and already knows the bench. Record the bench being scanned in a
   static there. The finalizer is what makes that safe — it must clear on the exception path
   or every later `ShouldDoNow` call reads a stale bench.
2. `Patch_Bill_Production_ShouldDoNow` already postfixes the exact method the selection loop
   calls per bill. Add: if a bench is currently being scanned and its
   `def.AllRecipes` does not contain `__instance.recipe`, return false.

Note `JobOnThing` early-returns on `BillStack.AnyShouldDoNow`, which is inside the prefix
window, so the fast path and the loop agree without extra work.

Outside that window the context is null and `ShouldDoNow` answers exactly as vanilla, which
is what keeps the bills tab's colouring honest — a pinned bill should read normal on its own
bench's tab and be distinguished by the icon, not by being drawn as un-startable everywhere.

The pure half is a set-membership test over recipe defNames, so it belongs in `Source/Core/`
next to `RecipeSetComparison` and is unit-testable without a game.

### What has to change beyond the gate

- `CanLink` drops the same-recipe-set refusal and gains an *overlap* requirement — at least
  one shared plain recipe, or the group is pointless.
- `RecipeSetComparison` stops being the link rule and becomes the "is this bill universal"
  test. Keep it; it is what decides whether a bill gets the icon.
- Round robin rotates the shared list at job start and would now rotate past bills the
  current bench cannot make. Check that rotation still advances sensibly when a member can
  work only a subset — the current-bench skip must not count as a turn.
- The overshoot guard and in-flight tracking are unaffected: both key off the bill.

### UI (deferred by the requester, but it is the whole point)

A broken-chain icon next to any bill in the list that is not workable at every member of the
group. Vanilla has no such icon; `TexCommand.RearmTrap` is already standing in as a
placeholder elsewhere in this mod (see loose ends), so this wants a real texture. Hover text
should name the benches that *can* do it, since "why is this one marked" is the only question
the icon raises.

### Verification this needs before it ships

The negative-test suite in the next item, and — for the first time in this mod — a real craft
loop scenario. This is the change that makes "a pawn walks to the right bench" a claim about
correctness rather than about plumbing: the whole point is that a cook must *not* path to the
bench that cannot cook.

## 2. Extend the live scenarios

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

### 2a. Anchor handover

Needs a new step (`WbgDestroyBench`) — the harness has no destroy/despawn step. Destroy the
anchor and assert: the group survives, the bills survive, a new anchor is elected, and the
survivors' stacks still point at one object. This is the single most consequential
untested path: `HandOffAnchorIfNeeded` failing means blowing up one bench cancels every
craft in the group and deletes the orders.

Also worth covering with the same step: minify/reinstall (redirect withdrawn on despawn,
reinstalled on spawn), and a group falling to one member (dissolves, survivor keeps bills).

### 2b. Negative tests — every refusal path

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
refusal set in a way nobody intended. They are also the prerequisite for item 1, which
rewrites two of the five branches — without a passing negative suite there is no baseline to
say what the rewrite changed.

### 2c. The real craft loop

Currently the behavioural scenarios hold a `Wait` job rather than `DoBill`, so "pawns walk
to the right bench and make the right number of things" is unverified. To do it properly:
`SpawnPawn` several colonists, place a powered stove pair plus ingredients, then
`FastForward` and count products. Expect this to be flaky against the shared fixture —
budget time for a leaner purpose-built fixture, and see the harness's own `Fixtures/README.md`.

The specific claim worth proving here is the one round robin exists for: with three bills
and enough workers, products come out roughly 1/1/1 rather than 3/0/0.

### 2d. Ingredient-mute isolation

`IngredientMuteIsolation` is implemented and completely untested. A probe reading the
remembered per-(bill, bench) tick would let a scenario show that one bench failing an
ingredient search does not mute the bill at the other.

### 2e. Nice Bill Tab compatibility — a planned follow-up, not a bug hunt

The tab-side UI in this mod is drawn by Harmony postfixes, and that is fine alone and fragile
alongside Nice Bill Tab, which prefixes `ITab_Bills.FillTab` and rebuilds the panel.

**The specific mechanism, so nobody re-derives it:** a Harmony postfix still runs when a prefix
skips the original. So when Nice Bill Tab replaces the tab, our postfix keeps drawing into a
panel laid out by someone else. Two things ride on that:

- `Patch_ITab_Bills_FillTab` draws the ordering dropdown at a fixed rect
  (`168, 2, 190, 26`), chosen as the gap between vanilla's "Add bill" and paste buttons. Under a
  rebuilt tab that gap may hold something else.
- `Patch_Bill_DoInterface` (chain icon, active-row highlight) is safer: it postfixes
  `Bill.DoInterface` and positions everything from the *returned* row rect, so it follows the row
  wherever the other mod puts it. It only breaks if Nice Bill Tab draws rows without calling
  `DoInterface` at all.

Neither is a correctness risk — nothing here writes state — so the failure mode is cosmetic
overlap, not a broken save. That is why the ordering control is *also* reflected in the inspect
line: a group left in the wrong mode is a preference, not data loss.

**What the work actually is**, once the feature set settles:

1. Get the mod installed. It is not on this machine — `Runner/fetch_mods.sh` pulls a scenario's
   `requiredMods` from the Workshop via anonymous SteamCMD, which may refuse for a paid app's
   items; the fallback is subscribing in-game.
2. Run the existing scenarios with it active and *look at the frames*. Every probe will pass
   either way, because probes read state and this is entirely a drawing problem — the same trap
   that hid the unclaimed-bench and stale-Languages failures.
3. Decide between: detecting a rebuilt tab and skipping our own draw; asking for a rect from a
   layout helper rather than hardcoding one; or exposing the ordering control somewhere neither
   mod owns.

Do this after the UI stops moving. Every layout change invalidates the frames, and the whole cost
of this item is in looking at frames.

### 2f. Conflicting mods, generally

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
