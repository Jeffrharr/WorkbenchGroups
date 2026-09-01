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
    /// Links every spawned bench of a given def on the map, and sets the group's ordering mode.
    ///
    /// A scenario cannot drive the link gizmo — that needs a mouse and a selection — so the step
    /// calls the same <c>BillGroupOps.Link</c> the gizmo calls. What it deliberately does not do is
    /// reimplement linking: eligibility, anchor election, bill merging and the field swap are all
    /// the shipped code, so a bug in any of them fails the scenario.
    /// </summary>
    public sealed class WbgLinkBenchesStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgLinkBenches";

        public string Type => TypeName;

        // Linking mutates the loaded map, so a following scenario in the same load must not
        // inherit it.
        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        // Silently regrouping someone's real workshop through the live channel is not something
        // this should ever be able to do by accident.
        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("def", out string def) || string.IsNullOrWhiteSpace(def))
            {
                error = "WbgLinkBenches requires 'def' (the workbench ThingDef name)";
                return false;
            }

            if (args.TryGetValue("mode", out string mode) && !IsKnownMode(mode))
            {
                error = $"WbgLinkBenches: 'mode' must be InOrder or RoundRobin (got '{mode}')";
                return false;
            }

            if (args.TryGetValue("limit", out string limit) && !int.TryParse(limit, out _))
            {
                error = $"WbgLinkBenches: 'limit' is not a number (got '{limit}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            WbgTestState.Reset();

            string defName = args["def"];
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return StepOutcome.Fail($"WbgLinkBenches: no ThingDef named '{defName}'");
            }

            List<Building_WorkTable> benches = new List<Building_WorkTable>();
            foreach (Thing thing in ctx.Map.listerThings.ThingsOfDef(def))
            {
                if (thing is Building_WorkTable bench && bench.Spawned)
                {
                    benches.Add(bench);
                }
            }

            if (benches.Count < 2)
            {
                return StepOutcome.Fail(
                    $"WbgLinkBenches: found {benches.Count} spawned '{defName}', need at least 2");
            }

            // The fixture is a real colony and may already own benches of this def. Taking the
            // ones nearest the map centre picks up exactly the benches the scenario placed there,
            // so an assertion on group size means what it says instead of counting the colony's.
            if (args.TryGetValue("limit", out string rawLimit))
            {
                IntVec3 centre = ctx.Map.Center;
                benches.Sort((a, b) => a.Position.DistanceToSquared(centre)
                    .CompareTo(b.Position.DistanceToSquared(centre)));

                int limit = int.Parse(rawLimit);
                if (benches.Count > limit)
                {
                    benches.RemoveRange(limit, benches.Count - limit);
                }
            }

            if (!BillGroupOps.CanLink(benches, out string reason))
            {
                return StepOutcome.Fail($"WbgLinkBenches: refused — {reason}");
            }

            BillGroupOps.Link(benches);
            WbgTestState.Benches.AddRange(benches);

            if (args.TryGetValue("mode", out string modeName) && modeName == "RoundRobin")
            {
                Building_WorkTable anchor = BillGroupIndex.For(ctx.Map)?.AnchorOf(benches[0]);
                RoundRobin.SetOrdering(anchor?.GetComp<CompBillGroup>(), OrderingMode.RoundRobin);
            }

            return new StepOutcome();
        }

        private static bool IsKnownMode(string mode)
        {
            return mode == "InOrder" || mode == "RoundRobin";
        }
    }
}
