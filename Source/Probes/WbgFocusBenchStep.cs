using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Points the camera at one bench, selects it, and opens its bills tab, so the next screenshot
    /// shows the thing this mod is actually about.
    ///
    /// Everything this mod does is invisible on the map — two stoves look identical linked or not.
    /// The only place the behaviour surfaces is the bills tab, and the only frame worth capturing
    /// is the tab of the *second* bench, which holds no bills of its own and shows the group's.
    ///
    /// Finds benches itself rather than reading <see cref="WbgTestState"/>, because the most
    /// useful capture is the "before" one, taken before anything has linked and populated it. The
    /// ordering matches anchor election (nearest the map centre first, then by thing ID), so
    /// `index` names the same bench before linking, after linking, and after a reload.
    ///
    /// Framing is left to a following <c>LookAt</c>: this jumps the camera so a capture is never
    /// of empty ground, but the scenario is what decides how much of the workshop to show.
    /// </summary>
    public sealed class WbgFocusBenchStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgFocusBench";

        public string Type => TypeName;

        // Moves the camera and opens a tab. Both are UI state, not map state, but a following
        // scenario that expected a clean viewport would be surprised by it.
        public ScenarioResidue Residue => ScenarioResidue.None;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("def", out string def) || string.IsNullOrWhiteSpace(def))
            {
                error = "WbgFocusBench requires 'def' (the workbench ThingDef name)";
                return false;
            }

            if (args.TryGetValue("index", out string index) && !int.TryParse(index, out _))
            {
                error = $"WbgFocusBench: 'index' is not a number (got '{index}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            string defName = args["def"];
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return StepOutcome.Fail($"WbgFocusBench: no ThingDef named '{defName}'");
            }

            List<Building_WorkTable> benches = NearestFirst(ctx.Map, def);
            int index = args.TryGetValue("index", out string rawIndex) ? int.Parse(rawIndex) : 0;

            if (index < 0 || index >= benches.Count)
            {
                return StepOutcome.Fail(
                    $"WbgFocusBench: index {index} out of range, found {benches.Count} spawned '{defName}'");
            }

            Building_WorkTable bench = benches[index];

            // The debug log auto-opens on the first red entry and then covers the middle of every
            // subsequent frame. The entries it opens for here are environmental — other mods in
            // the Mods folder with duplicate packageIds — so this is closing noise, not hiding our
            // own errors, which the run reports separately either way.
            Find.WindowStack.TryRemove(typeof(EditWindow_Log), doCloseSound: false);

            // Jump before selecting. TryJumpAndSelect does both, but the camera move is what makes
            // the capture reliable: LookAt's centre anchor frames the map's middle, which on a real
            // colony fixture is usually empty ground a long way from anything the scenario built.
            CameraJumper.TryJumpAndSelect(bench);
            InspectPaneUtility.OpenTab(typeof(ITab_Bills));

            // The pane lays out over the following frames, so a capture taken immediately gets the
            // previous tab. Left to the scenario's own Wait rather than hidden here, so the cost is
            // visible where someone tuning the sequence would look for it.
            return new StepOutcome { WaitFrames = 30 };
        }

        /// <summary>
        /// Spawned benches of a def, nearest the map centre first, ties broken by thing ID.
        ///
        /// Nearest-first because the fixture is a real colony that may own benches of this def
        /// already; the ones the scenario placed are at the centre. Thing ID as the tiebreak so
        /// the order is identical before and after a save, which is what lets the same `index`
        /// name the same bench across the two runs of the round-trip.
        /// </summary>
        private static List<Building_WorkTable> NearestFirst(Map map, ThingDef def)
        {
            List<Building_WorkTable> benches = new List<Building_WorkTable>();
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing is Building_WorkTable bench && bench.Spawned)
                {
                    benches.Add(bench);
                }
            }

            IntVec3 centre = map.Center;
            benches.Sort((a, b) =>
            {
                int byDistance = a.Position.DistanceToSquared(centre)
                    .CompareTo(b.Position.DistanceToSquared(centre));
                return byDistance != 0 ? byDistance : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });

            return benches;
        }
    }
}
