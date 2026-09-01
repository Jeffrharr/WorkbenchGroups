using System.Collections.Generic;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Finds an existing group on the map and records it for the probes, without linking anything.
    ///
    /// This is what makes the reload half of the save round-trip observable. The harness has no
    /// mid-scenario reload step, so the round-trip is run as two scenarios in two loads: one that
    /// links and saves, and one whose <c>saveFile</c> is that save. In the second, nothing has run
    /// <c>WbgLinkBenches</c>, so <see cref="WbgTestState"/> is empty and every probe would read -1.
    ///
    /// Finds the group by membership rather than by def and position, which is both simpler and
    /// stricter: it locates the benches that claim to be grouped, and leaves whether the group
    /// actually *works* — whether the field redirect was reinstalled — entirely to the probes. A
    /// load where <c>CompBillGroup.PostMapInit</c> failed still has membership, because the anchor
    /// reference is scribed, so this step still finds the pair and the probes still fail. Finding
    /// the benches by looking for a working group would have made the test vacuous.
    /// </summary>
    public sealed class WbgTrackGroupStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgTrackGroup";

        public string Type => TypeName;

        // Records references; changes nothing on the map.
        public ScenarioResidue Residue => ScenarioResidue.None;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (args.TryGetValue("expectSize", out string size) && !int.TryParse(size, out _))
            {
                error = $"WbgTrackGroup: 'expectSize' is not a number (got '{size}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            WbgTestState.Reset();

            List<Building_WorkTable> roster = LargestGroupOn(ctx.Map);
            if (roster.Count == 0)
            {
                return StepOutcome.Fail("WbgTrackGroup: no bench on the map claims to be in a group");
            }

            if (args.TryGetValue("expectSize", out string rawSize) && roster.Count != int.Parse(rawSize))
            {
                return StepOutcome.Fail(
                    $"WbgTrackGroup: largest group has {roster.Count} benches, expected {rawSize}");
            }

            WbgTestState.Benches.AddRange(roster);

            // The bills come back from the save as new objects, so the ones the saving scenario
            // queued cannot be matched by reference. Recording the shared list's contents in its
            // current order lets the head-bill probe still mean something after a load.
            foreach (Bill bill in roster[0].billStack?.Bills ?? new List<Bill>())
            {
                if (bill is Bill_Production production)
                {
                    WbgTestState.Bills.Add(production);
                }
            }

            return new StepOutcome();
        }

        /// <summary>
        /// The biggest group on the map, anchor first and the rest by thing ID.
        ///
        /// Anchor first because the probes are written against that order — the bill-count probe
        /// deliberately reads the *second* bench, which only proves sharing if the first is the
        /// one that owns the list.
        /// </summary>
        private static List<Building_WorkTable> LargestGroupOn(Map map)
        {
            BillGroupIndex index = BillGroupIndex.For(map);
            List<Building_WorkTable> best = new List<Building_WorkTable>();
            if (index == null)
            {
                return best;
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                Building_WorkTable bench = thing as Building_WorkTable;
                Building_WorkTable anchor = bench == null ? null : index.AnchorOf(bench);
                if (anchor != null && anchor == bench)
                {
                    List<Building_WorkTable> roster = index.RosterOf(bench);
                    if (roster.Count > best.Count)
                    {
                        best = roster;
                    }
                }
            }

            return best;
        }
    }
}
