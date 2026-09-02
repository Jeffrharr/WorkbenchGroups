#!/usr/bin/env bash
# Tests/run_roundtrip.sh — the save/reload round-trip, run as two game loads.
#
# The one thing no single scenario can check. `CompBillGroup.PostMapInit` reinstalls the shared
# bill list's field redirect after a load, and nothing had ever watched it do so: the save half is
# measured directly (duplicate load-ID warnings), the load half needs an actual load. The harness
# has no mid-scenario reload step and does not need one — a scenario's `saveFile` is a fixture, so
# writing a save in one run and naming it as the next run's fixture is a reload by another route.
#
#   Phase A  reload_roundtrip_save  links two stoves, gives them three bills, switches on round
#            robin, saves as wbg_roundtrip.
#   Bridge   copies that save out of RimWorld's Saves/ into the harness's Fixtures/.
#   Phase B  reload_roundtrip_load  boots with that save as its fixture and probes only. Nothing
#            in it links anything, so whatever it observes was rebuilt during the load.
#
# Both phases must run with the SAME --mod set: a save embeds its mod list, and phase B's fixture
# is phase A's save.
#
# Phase B asserts nothing a screenshot could show — the claim is that two benches point at one
# BillStack *object*, which no frame distinguishes from two equal lists. So the check that it is
# not vacuously green is a negative control, run once by hand: point phase B at minimal_colony.rws
# instead and it fails on every probe, with WbgTrackGroup reporting "no bench on the map claims to
# be in a group". Re-run that if you ever change how the group is located.
#
# Usage:  Tests/run_roundtrip.sh [extra run_test.sh flags]
#         MOD_MAIN=/path/to/main/checkout Tests/run_roundtrip.sh     # if not the default sibling
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKTREE="$(dirname "$HERE")"

# The mod's permanent checkout, which is what Mods/<Mod> symlinks to and therefore what actually
# activates the packageId. --mod-overlay then swaps in this worktree's build. Naming only the
# worktree would report success while the game loaded the other branch's assemblies.
#
# Derived from git rather than from the path, because a worktree lives under a sibling
# .worktrees/ directory and walking up from it lands on directories that merely look right:
# .worktrees/ holds a checkout of the harness too, and pointing at that one runs a different
# branch's harness build, which is a native crash with nothing in the log to explain it.
MOD_MAIN="${MOD_MAIN:-$(dirname "$(git -C "$WORKTREE" rev-parse --git-common-dir)")}"
HARNESS="${HARNESS:-$(dirname "$MOD_MAIN")/RimWorldTestHarness}"

for dir in "$MOD_MAIN" "$HARNESS"; do
    if [[ ! -d "$dir" ]]; then
        echo "run_roundtrip: $dir does not exist — set MOD_MAIN / HARNESS" >&2
        exit 1
    fi
done

CONFIG_DIR="${RWTH_CONFIG_DIR:-$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios}"
SAVE_SRC="$CONFIG_DIR/Saves/wbg_roundtrip.rws"
SAVE_DEST="$HARNESS/Fixtures/wbg_roundtrip.rws"

run_phase() {
    # --mod-overlay swaps the activated mod's ASSEMBLIES and nothing else, so a worktree's XML is
    # not picked up by it. Languages has to be overlaid separately or the run reads the main
    # checkout's keys — which shows up as raw "WBG_..." strings on new gizmos and is easy to
    # misread as a broken translation rather than a stale file. --install rolls back on teardown
    # the same way the assembly overlay does.
    "$HARNESS/Runner/run_test.sh" \
        --mod "$MOD_MAIN" \
        --mod-overlay "$WORKTREE" \
        --install "$WORKTREE/Languages:$MOD_MAIN/Languages" \
        --mod "$WORKTREE/TestMod" \
        --no-profiler \
        "$@"
}

echo "== phase A: link, add bills, save =="
# Stale saves are removed first. A phase A that fails before saving would otherwise leave an old
# file in place for phase B to load, and phase B would pass against a save nothing in this run
# produced — the exact shape of green that means nothing.
rm -f "$SAVE_SRC"
run_phase "$HERE/Scenarios/roundtrip/reload_roundtrip_save.json"

if [[ ! -f "$SAVE_SRC" ]]; then
    echo "run_roundtrip: phase A passed but wrote no save at $SAVE_SRC" >&2
    exit 1
fi

echo "== bridge: $SAVE_SRC -> $SAVE_DEST =="
cp "$SAVE_SRC" "$SAVE_DEST"

echo "== phase B: load that save, probe only =="
run_phase "$HERE/Scenarios/roundtrip/reload_roundtrip_load.json"

echo "== round-trip passed =="
