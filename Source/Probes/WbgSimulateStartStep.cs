using System.Collections.Generic;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;
using Verse.AI;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Has a pawn take the bill currently at the top of the shared list.
    ///
    /// This is the step that makes the round-robin and overshoot behaviour observable. It works by
    /// starting a real job carrying a real bill through <c>Pawn_JobTracker.StartJob</c>, so the
    /// shipped Harmony postfix is genuinely in the path — the scenario is not calling our rotation
    /// or tracking code directly, it is doing the thing that triggers them.
    ///
    /// The job itself is a long Wait rather than DoBill on purpose. What is under test is the
    /// decision made at the moment a pawn commits to a bill; making them actually walk over and
    /// craft would drag in ingredient availability, power, pathing and work priorities, none of
    /// which this mod touches and all of which would make the result depend on the fixture colony.
    /// A Wait job also stays current, which is what keeps the claim held while the probe reads it —
    /// a job that ended would be cleaned up and release its claim before anything could observe it.
    ///
    /// A different pawn is used per start where one is free, because one pawn starting a second
    /// job ends their first and hands that claim straight back. Where the fixture has run out of
    /// colonists it reuses one, which is harmless for rotation — the list has already moved — but
    /// means only the most recent claim is still held. Pass <c>requireDistinct</c> when a scenario
    /// needs several claims outstanding at once, so it fails loudly instead of measuring one.
    /// </summary>
    public sealed class WbgSimulateStartStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgSimulateBillStart";

        private const int HoldTicks = 100000;

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (args.TryGetValue("requireDistinct", out string raw) && !bool.TryParse(raw, out _))
            {
                error = $"WbgSimulateBillStart: 'requireDistinct' is not a boolean (got '{raw}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            if (WbgTestState.Benches.Count == 0)
            {
                return StepOutcome.Fail("WbgSimulateBillStart: no benches tracked — run WbgLinkBenches first");
            }

            BillStack stack = WbgTestState.Benches[0].billStack;
            if (stack == null || stack.Count == 0)
            {
                return StepOutcome.Fail("WbgSimulateBillStart: the shared list has no bills");
            }

            Bill headBill = stack[0];

            bool requireDistinct = args.TryGetValue("requireDistinct", out string raw)
                                   && bool.Parse(raw);

            Pawn worker = NextUnusedWorker(ctx.Map) ?? (requireDistinct ? null : AnyWorker(ctx.Map));
            if (worker == null)
            {
                return StepOutcome.Fail(
                    $"WbgSimulateBillStart: no {(requireDistinct ? "unused " : "")}colonist available " +
                    $"(already used {WbgTestState.SimulatedWorkers.Count})");
            }

            Job job = JobMaker.MakeJob(JobDefOf.Wait, HoldTicks);
            job.bill = headBill;

            worker.jobs.StartJob(job, JobCondition.InterruptForced);
            WbgTestState.SimulatedWorkers.Add(worker);

            return new StepOutcome();
        }

        /// <summary>Any usable colonist, for reuse once the fixture's roster is exhausted.</summary>
        private static Pawn AnyWorker(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (!pawn.Downed && !pawn.Dead)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static Pawn NextUnusedWorker(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (!WbgTestState.SimulatedWorkers.Contains(pawn) && !pawn.Downed && !pawn.Dead)
                {
                    return pawn;
                }
            }

            return null;
        }
    }
}
