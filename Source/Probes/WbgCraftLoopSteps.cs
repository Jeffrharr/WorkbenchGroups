using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
    /// Boilerplate shared by the craft-loop steps: every one of them mutates the map, and none is
    /// safe to fire through the live channel at someone's real colony.
    /// </summary>
    public abstract class WbgCraftLoopStep : IStepSpec, IStepAction
    {
        public abstract string Type { get; }

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public virtual bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;
            return true;
        }

        public abstract StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx);

        protected static bool Require(IReadOnlyDictionary<string, string> args, string key, string type, out string error)
        {
            error = args.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
                ? null
                : $"{type} requires '{key}'";
            return error == null;
        }
    }

    /// <summary>
    /// Adds a bill for any recipe straight onto the shared stack and checks whether
    /// <c>Patch_BillStack_AddBill</c> let it in.
    ///
    /// Unlike <c>WbgAddBill</c> this skips the "can this bench make it" check on purpose: the
    /// negative path is precisely a bill no group bench offers — mech gestation, forming — reaching
    /// <c>AddBill</c> from code, which is the route the guard exists for. <c>ifPresent</c> lets a
    /// DLC recipe be named on a machine without that DLC; the skip is logged, not silent.
    /// </summary>
    public sealed class WbgTryAddBillStep : WbgCraftLoopStep
    {
        public override string Type => "WbgTryAddBill";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            if (!Require(args, "recipe", Type, out error) || !Require(args, "expect", Type, out error))
            {
                return false;
            }

            string expect = args["expect"];
            if (expect != "added" && expect != "refused")
            {
                error = $"{Type}: 'expect' must be added or refused (got '{expect}')";
                return false;
            }

            return true;
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            BillStack stack = WbgTestState.Benches.FirstOrDefault()?.billStack;
            if (stack == null)
            {
                return StepOutcome.Fail($"{Type}: no benches tracked — run WbgLinkBenches first");
            }

            string recipeName = args["recipe"];
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeName);
            if (recipe == null)
            {
                bool optional = args.TryGetValue("ifPresent", out string raw) && bool.Parse(raw);
                if (!optional)
                {
                    return StepOutcome.Fail($"{Type}: no RecipeDef named '{recipeName}'");
                }

                Log.Message($"[Workbench Groups] {Type}: '{recipeName}' not loaded, skipped");
                return new StepOutcome();
            }

            Bill bill = recipe.MakeNewBill();
            stack.AddBill(bill);
            string outcome = stack.Bills.Contains(bill) ? "added" : "refused";

            Log.Message($"[Workbench Groups] {Type}: {recipeName} ({bill.GetType().Name}) was {outcome}");

            if (outcome != args["expect"])
            {
                return StepOutcome.Fail(
                    $"{Type}: {recipeName} ({bill.GetType().Name}) was {outcome}, expected {args["expect"]}");
            }

            if (outcome == "added" && bill is Bill_Production production)
            {
                WbgTestState.Bills.Add(production);
            }

            return new StepOutcome();
        }
    }

    /// <summary>
    /// Spawns one stack of a def at a cell. <c>PlaceThings</c> places buildings one per cell; an
    /// order needing forty cloth needs a stack. <c>role: control</c> records the stack as the
    /// hauler's control item.
    /// </summary>
    public sealed class WbgSpawnStackStep : WbgCraftLoopStep
    {
        public override string Type => "WbgSpawnStack";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            if (!Require(args, "def", Type, out error) || !Require(args, "cell", Type, out error))
            {
                return false;
            }

            if (!WbgCraftLoopSupport.TryParseCell(null, args["cell"], out _))
            {
                error = $"{Type}: 'cell' must be \"dx,dz\" (got '{args["cell"]}')";
                return false;
            }

            return true;
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(args["def"]);
            if (def == null)
            {
                return StepOutcome.Fail($"{Type}: no ThingDef named '{args["def"]}'");
            }

            WbgCraftLoopSupport.TryParseCell(ctx.Map, args["cell"], out IntVec3 cell);
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(def.stackLimit,
                args.TryGetValue("count", out string raw) ? int.Parse(raw) : 1);

            GenSpawn.Spawn(thing, cell, ctx.Map);

            if (args.TryGetValue("role", out string role) && role == "control")
            {
                WbgTestState.ControlItem = thing;
            }

            return new StepOutcome();
        }
    }

    /// <summary>
    /// A stockpile over a rectangle, accepting everything or only the defs named in <c>allow</c>. The hauler needs somewhere better
    /// to take things, or "it left the item alone" would only mean it had nowhere to put it.
    ///
    /// Loose items already in the rectangle are destroyed first. The fixture scatters forbidden
    /// rock chunks around the map centre, and a stockpile whose every cell holds one accepts
    /// nothing — which the first run of the craft-loop scenario found the hard way, as a control
    /// item that never moved.
    /// </summary>
    public sealed class WbgMakeStockpileStep : WbgCraftLoopStep
    {
        public override string Type => "WbgMakeStockpile";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            return Require(args, "from", Type, out error) && Require(args, "to", Type, out error);
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            if (!WbgCraftLoopSupport.TryParseCell(ctx.Map, args["from"], out IntVec3 from)
                || !WbgCraftLoopSupport.TryParseCell(ctx.Map, args["to"], out IntVec3 to))
            {
                return StepOutcome.Fail($"{Type}: 'from' and 'to' must be \"dx,dz\"");
            }

            Zone_Stockpile zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, ctx.Map.zoneManager);
            ctx.Map.zoneManager.RegisterZone(zone);
            // "allow" narrows the stockpile to named defs. The fixture's whole colony is strewn
            // with loose items, and an accept-everything stockpile had the hauler spending the
            // entire window carrying the colony's junk before it reached the control stack.
            if (args.TryGetValue("allow", out string allow))
            {
                zone.settings.filter.SetDisallowAll();
                foreach (string name in allow.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (def == null)
                    {
                        return StepOutcome.Fail($"{Type}: no ThingDef named '{name}' in 'allow'");
                    }

                    zone.settings.filter.SetAllow(def, true);
                }
            }
            else
            {
                zone.settings.filter.SetAllowAll(null);
            }

            foreach (IntVec3 cell in CellRect.FromLimits(from, to))
            {
                foreach (Thing item in cell.GetThingList(ctx.Map).Where(t => t.def.category == ThingCategory.Item).ToList())
                {
                    item.Destroy();
                }

                zone.AddCell(cell);
            }

            return new StepOutcome();
        }
    }

    /// <summary>
    /// Picks a crafter (can tailor), a hauler (can haul) and a blocker (anyone else) from the
    /// colonists nearest the map centre, then switches off every work type for every free
    /// colonist and gives the crafter tailoring alone.
    ///
    /// Switching everyone off is what makes the craft loop readable: the fixture's own colonist
    /// would otherwise wander in and take the order, and a spawned colonist with random priorities
    /// might spend the whole window cleaning.
    /// </summary>
    public sealed class WbgAssignRolesStep : WbgCraftLoopStep
    {
        public override string Type => "WbgAssignRoles";

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            List<Pawn> candidates = WbgCraftLoopSupport.CandidateColonists(ctx.Map);

            Pawn crafter = candidates.FirstOrDefault(pawn =>
                CanTailor(pawn));
            Pawn hauler = candidates.FirstOrDefault(pawn =>
                pawn != crafter && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
            Pawn blocker = candidates.FirstOrDefault(pawn => pawn != crafter && pawn != hauler);

            if (crafter == null || hauler == null || blocker == null)
            {
                return StepOutcome.Fail(
                    $"{Type}: need a tailor, a hauler and one more colonist; found {candidates.Count} candidates "
                    + $"(crafter={crafter?.LabelShort}, hauler={hauler?.LabelShort}, blocker={blocker?.LabelShort})");
            }

            foreach (Pawn pawn in ctx.Map.mapPawns.FreeColonistsSpawned)
            {
                DisableAllWork(pawn);
            }

            crafter.workSettings.SetPriority(TailoringWork(), 3);

            WbgTestState.Crafter = crafter;
            WbgTestState.Hauler = hauler;
            WbgTestState.Blocker = blocker;

            Log.Message($"[Workbench Groups] {Type}: crafter={crafter.LabelShort}, hauler={hauler.LabelShort}, "
                + $"blocker={blocker.LabelShort}");
            return new StepOutcome();
        }

        private static WorkTypeDef TailoringWork()
        {
            return DefDatabase<WorkTypeDef>.GetNamed("Tailoring");
        }

        private static bool CanTailor(Pawn pawn)
        {
            return !pawn.WorkTypeIsDisabled(TailoringWork())
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        private static void DisableAllWork(Pawn pawn)
        {
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings.SetPriority(work, 0);
                }
            }
        }
    }

    /// <summary>Turns one work type on for one of the assigned roles.</summary>
    public sealed class WbgEnableWorkStep : WbgCraftLoopStep
    {
        public override string Type => "WbgEnableWork";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            return Require(args, "role", Type, out error) && Require(args, "workType", Type, out error);
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Pawn pawn = WbgCraftLoopSupport.PawnFor(args["role"]);
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(args["workType"]);
            if (pawn == null || work == null)
            {
                return StepOutcome.Fail($"{Type}: unknown role or work type — run WbgAssignRoles first");
            }

            pawn.workSettings.SetPriority(work, 3);
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Holds one bench busy — <c>which: anchor | member | none</c> — the way a colonist working at
    /// it would: the blocker takes a long wait job and reserves the bench under it.
    ///
    /// A reservation is exactly what <c>WorkGiver_DoBill.JobOnThing</c> tests first
    /// (<c>pawn.CanReserve(thing)</c>), so a busy bench drops out of the crafter's scan for the
    /// real reason. Any previous hold is released first, so switching which bench is busy is one
    /// step. Benches cannot simply be forbidden: most have no forbiddable comp.
    /// </summary>
    public sealed class WbgOccupyBenchStep : WbgCraftLoopStep
    {
        private const int HoldTicks = 100000;

        public override string Type => "WbgOccupyBench";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            if (!Require(args, "which", Type, out error))
            {
                return false;
            }

            string which = args["which"];
            if (which != "anchor" && which != "member" && which != "none")
            {
                error = $"{Type}: 'which' must be anchor, member or none (got '{which}')";
                return false;
            }

            return true;
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Pawn blocker = WbgTestState.Blocker;
            if (blocker == null)
            {
                return StepOutcome.Fail($"{Type}: no blocker — run WbgAssignRoles first");
            }

            if (blocker.CurJob != null)
            {
                blocker.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }

            string which = args["which"];
            if (which == "none")
            {
                return new StepOutcome();
            }

            Building_WorkTable bench = which == "anchor"
                ? WbgCraftLoopSupport.Anchor(ctx.Map)
                : WbgCraftLoopSupport.Member(ctx.Map);
            if (bench == null)
            {
                return StepOutcome.Fail($"{Type}: no {which} bench — run WbgLinkBenches first");
            }

            Job job = JobMaker.MakeJob(JobDefOf.Wait, HoldTicks);
            blocker.jobs.StartJob(job, JobCondition.InterruptForced);
            if (!blocker.Reserve(bench, job))
            {
                return StepOutcome.Fail($"{Type}: could not reserve the {which} bench");
            }

            return new StepOutcome();
        }
    }

    /// <summary>
    /// Drafts or undrafts the crafter. Drafting is the interruption a player actually uses, and it
    /// ends the DoBill job the ordinary way, leaving the unfinished item where the pawn was
    /// working.
    /// </summary>
    public sealed class WbgDraftStep : WbgCraftLoopStep
    {
        public override string Type => "WbgDraft";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            if (!Require(args, "drafted", Type, out error))
            {
                return false;
            }

            if (!bool.TryParse(args["drafted"], out _))
            {
                error = $"{Type}: 'drafted' is not a boolean";
                return false;
            }

            return true;
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Pawn crafter = WbgTestState.Crafter;
            if (crafter?.drafter == null)
            {
                return StepOutcome.Fail($"{Type}: no draftable crafter — run WbgAssignRoles first");
            }

            crafter.drafter.Drafted = bool.Parse(args["drafted"]);
            return new StepOutcome();
        }
    }

    /// <summary>Records the order's unfinished item, so a later probe can tell it was resumed, not restarted.</summary>
    public sealed class WbgRememberUftStep : WbgCraftLoopStep
    {
        public override string Type => "WbgRememberUft";

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            if (uft == null)
            {
                return StepOutcome.Fail($"{Type}: the order has no unfinished item yet");
            }

            WbgTestState.RememberedUftId = uft.thingIDNumber;
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Moves the order's unfinished item onto a bench by hand. Used only by the control scenario,
    /// where the redirect is withheld and the crafter therefore cannot carry it to the member
    /// bench itself — the hauler half of the control still needs an item parked there.
    /// </summary>
    public sealed class WbgMoveUftStep : WbgCraftLoopStep
    {
        public override string Type => "WbgMoveUft";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            return Require(args, "to", Type, out error);
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            Building_WorkTable bench = args["to"] == "anchor"
                ? WbgCraftLoopSupport.Anchor(ctx.Map)
                : WbgCraftLoopSupport.Member(ctx.Map);
            if (uft == null || !uft.Spawned || bench == null)
            {
                return StepOutcome.Fail($"{Type}: need a spawned unfinished item and a {args["to"]} bench");
            }

            uft.DeSpawn();
            GenSpawn.Spawn(uft, bench.Position, ctx.Map);
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Removes named Workbench Groups patches for the rest of the process, or puts them back
    /// (<c>restore: true</c>). This is what turns the craft-loop scenario into an A/B: the control
    /// runs the same scene with the fix withheld and asserts vanilla's broken outcome, which proves
    /// the scenario can see the difference at all.
    ///
    /// Harmony state is process-wide, so it survives a scenario reload. A scenario that withholds
    /// must restore at its end, and must never share a run with others — if it fails midway the
    /// patches stay off.
    /// </summary>
    public sealed class WbgWithholdPatchesStep : WbgCraftLoopStep
    {
        private const string ModHarmonyId = "joof.workbenchgroups";

        public override string Type => "WbgWithholdPatches";

        public override bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            return Require(args, "classes", Type, out error);
        }

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            HashSet<string> names = new HashSet<string>(
                args["classes"].Split(',').Select(name => name.Trim()).Where(name => name.Length > 0));
            bool restore = args.TryGetValue("restore", out string raw) && bool.Parse(raw);

            List<Type> types = typeof(BillGroupOps).Assembly.GetTypes()
                .Where(type => names.Contains(type.Name))
                .ToList();
            if (types.Count != names.Count)
            {
                return StepOutcome.Fail($"{Type}: found {types.Count} of {names.Count} patch classes");
            }

            Harmony harmony = new Harmony(ModHarmonyId);
            int touched = restore ? Restore(harmony, types) : Withhold(harmony, types);

            Log.Message($"[Workbench Groups] {Type}: {(restore ? "restored" : "withheld")} {touched} patch method(s)");
            return touched > 0
                ? new StepOutcome()
                : StepOutcome.Fail($"{Type}: nothing to {(restore ? "restore" : "withhold")}");
        }

        private static int Withhold(Harmony harmony, List<Type> types)
        {
            int removed = 0;
            foreach (System.Reflection.MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                IEnumerable<Patch> ours = info.Prefixes.Concat(info.Postfixes)
                    .Concat(info.Transpilers).Concat(info.Finalizers)
                    .Where(patch => patch.owner == ModHarmonyId && types.Contains(patch.PatchMethod.DeclaringType))
                    .ToList();

                foreach (Patch patch in ours)
                {
                    harmony.Unpatch(original, patch.PatchMethod);
                    removed++;
                }
            }

            return removed;
        }

        private static int Restore(Harmony harmony, List<Type> types)
        {
            int applied = 0;
            foreach (Type type in types)
            {
                applied += harmony.CreateClassProcessor(type).Patch()?.Count ?? 0;
            }

            return applied;
        }
    }

    /// <summary>
    /// Closes the debug log window. It auto-opens on any logged error — including load-time noise
    /// from unrelated mods on this machine — and then sits over the middle of every capture.
    /// </summary>
    public sealed class WbgCloseDebugLogStep : WbgCraftLoopStep
    {
        public override string Type => "WbgCloseDebugLog";

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Find.WindowStack.TryRemove(typeof(LudeonTK.EditWindow_Log), doCloseSound: false);
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Logs what the crafter is doing and how its item stands. Diagnostic only: spawned
    /// colonists are random, so when a timed phase comes up short the log should say why.
    /// </summary>
    public sealed class WbgLogCrafterStep : WbgCraftLoopStep
    {
        public override string Type => "WbgLogCrafter";

        public override StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Pawn crafter = WbgTestState.Crafter;
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            Log.Message($"[Workbench Groups] {Type}: {crafter?.LabelShort} job={crafter?.CurJob?.def?.defName} "
                + $"target={crafter?.CurJob?.targetA.Thing?.LabelShort} food={crafter?.needs?.food?.CurLevelPercentage:F2} "
                + $"rest={crafter?.needs?.rest?.CurLevelPercentage:F2} drafted={crafter?.Drafted} "
                + $"uftWorkLeft={uft?.workLeft:F0} uftAt={uft?.Position} | "
                + $"hauler {WbgTestState.Hauler?.LabelShort} job={WbgTestState.Hauler?.CurJob?.def?.defName} "
                + $"at={WbgTestState.Hauler?.Position} | control spawned={WbgTestState.ControlItem?.Spawned} "
                + $"at={WbgTestState.ControlItem?.Position} stored={WbgTestState.ControlItem?.IsInValidStorage()} "
                + $"carriedBy={WbgTestState.ControlItem?.ParentHolder}");
            return new StepOutcome();
        }
    }
}
