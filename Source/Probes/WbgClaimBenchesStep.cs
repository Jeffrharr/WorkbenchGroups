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
    /// Gives scenario-placed benches to the player faction.
    ///
    /// <c>PlaceThings</c> spawns buildings unowned, which is invisible in a probe and decisive on
    /// screen: <c>BillGroupGizmos.GizmosFor</c> requires <c>Faction.OfPlayer</c>, so an unclaimed
    /// bench shows vanilla's "Claim" button and none of this mod's. Every screenshot taken before
    /// this step existed was therefore of a bench whose link, unlink, select-linked and ordering
    /// gizmos could not appear — the UI was being photographed with its subject absent, and only
    /// someone asking "where is the link button?" caught it.
    /// </summary>
    public sealed class WbgClaimBenchesStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgClaimBenches";

        public string Type => TypeName;

        // Changes ownership of things on the map, so a following scenario must get a fresh one.
        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("def", out string def) || string.IsNullOrWhiteSpace(def))
            {
                error = "WbgClaimBenches requires 'def' (the workbench ThingDef name)";
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
                return StepOutcome.Fail($"WbgClaimBenches: no ThingDef named '{defName}'");
            }

            int claimed = 0;
            foreach (Thing thing in new List<Thing>(ctx.Map.listerThings.ThingsOfDef(def)))
            {
                if (thing is Building_WorkTable bench && bench.Spawned
                    && bench.Faction != Faction.OfPlayer)
                {
                    bench.SetFaction(Faction.OfPlayer);
                    claimed++;
                }
            }

            if (claimed == 0)
            {
                return StepOutcome.Fail(
                    $"WbgClaimBenches: no unclaimed spawned '{defName}' found — the step did nothing");
            }

            return new StepOutcome();
        }
    }
}
