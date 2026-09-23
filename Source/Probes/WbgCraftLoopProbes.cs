using System.Linq;
using RimWorld;
using RimWorldTestHarness.Mod.Probes;
using Verse;
using Verse.AI;

namespace WorkbenchGroups.Probes
{
    // Probes for the unfinished-item craft loop (issue #11). Bench answers are roles, not indices:
    // 0 = anchor, 1 = member, -1 = neither, -2 = nothing to report. See WbgCraftLoopSupport.

    /// <summary>Whether the FinishUftJob transpiler applied — the interlock that admits these orders.</summary>
    public sealed class RedirectInstalledProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_redirect_installed";
        public string Description => "1 if the FinishUftJob redirect is installed, 0 if the mod fell back to refusing unfinished-item orders.";
        public string Unit => "bool";

        public float Read(Map map)
        {
            return UnfinishedItemSharing.RedirectInstalled ? 1f : 0f;
        }
    }

    /// <summary>
    /// Which bench the crafter's current DoBill job is aimed at (<c>job.targetA</c>) — the bench
    /// the pawn is actually walking to or working at.
    /// </summary>
    public sealed class CrafterJobAtProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_crafter_job_at";
        public string Description => "Role of the bench the crafter's DoBill job targets: 0 anchor, 1 member, -1 neither, -2 no DoBill job.";
        public string Unit => "role";

        public float Read(Map map)
        {
            Job job = WbgTestState.Crafter?.CurJob;
            if (job == null || job.def != JobDefOf.DoBill)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return WbgCraftLoopSupport.RoleOf(map, job.targetA.Thing);
        }
    }

    /// <summary>
    /// What vanilla's <c>FinishUftJob</c> would have aimed the same job at:
    /// <c>job.bill.billStack.billGiver</c>. Read beside <see cref="CrafterJobAtProbe"/>, it shows
    /// the redirect is what put the pawn where it is — the two disagree exactly when it mattered.
    /// </summary>
    public sealed class CrafterJobVanillaAtProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_crafter_job_vanilla_at";
        public string Description => "Role of bill.billStack.billGiver for the crafter's DoBill job — where vanilla would have sent it. -2 no DoBill job.";
        public string Unit => "role";

        public float Read(Map map)
        {
            Job job = WbgTestState.Crafter?.CurJob;
            if (job?.bill == null || job.def != JobDefOf.DoBill)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return WbgCraftLoopSupport.RoleOf(map, job.bill.billStack?.billGiver as Thing);
        }
    }

    /// <summary>
    /// Asks the real work giver, right now, what job the crafter would get from the member bench,
    /// and reports that job's target. Deterministic where the think tree is not: this is the
    /// transpiler's answer with nothing else in the way, and the control scenario reads 0 here
    /// where the fixed build reads 1.
    ///
    /// <c>JobOnThing</c> reserves nothing, so calling it from a probe does not disturb the scene.
    /// </summary>
    public sealed class ResumeTargetAtProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_resume_target_at";
        public string Description => "Role of targetA of the job WorkGiver_DoBill.JobOnThing(crafter, member bench) returns now. -2 no job.";
        public string Unit => "role";

        public float Read(Map map)
        {
            Pawn crafter = WbgTestState.Crafter;
            Building_WorkTable member = WbgCraftLoopSupport.Member(map);
            WorkGiver_DoBill worker = WbgCraftLoopSupport.WorkGiverFor(member);
            if (crafter == null || worker == null)
            {
                return WbgCraftLoopSupport.Missing;
            }

            Job job = worker.JobOnThing(crafter, member, forced: true);
            if (job == null)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return WbgCraftLoopSupport.RoleOf(map, job.targetA.Thing);
        }
    }

    /// <summary>How many unfinished items on the map belong to the scenario's orders.</summary>
    public sealed class UftCountProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_count";
        public string Description => "Unfinished items on the map bound to one of the scenario's orders.";
        public string Unit => "items";

        public float Read(Map map)
        {
            return map.listerThings.AllThings
                .OfType<UnfinishedThing>()
                .Count(uft => uft.BoundBill != null && WbgTestState.Bills.Contains(uft.BoundBill));
        }
    }

    /// <summary>Which bench the order's unfinished item is resting on or beside.</summary>
    public sealed class UftAtProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_at";
        public string Description => "Role of the bench whose footprint+1 holds the order's unfinished item: 0 anchor, 1 member, -1 elsewhere, -2 none/unspawned.";
        public string Unit => "role";

        public float Read(Map map)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            if (uft == null || !uft.Spawned)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return WbgCraftLoopSupport.RoleAt(map, uft.Position);
        }
    }

    /// <summary>
    /// <c>UnfinishedThing.BoundWorkTable</c>, through the real getter — so the postfix is in the
    /// path. Vanilla always answers the anchor.
    /// </summary>
    public sealed class UftBoundTableAtProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_bound_table_at";
        public string Description => "Role of UnfinishedThing.BoundWorkTable for the order's item: 0 anchor, 1 member, -2 none.";
        public string Unit => "role";

        public float Read(Map map)
        {
            Thing table = WbgCraftLoopSupport.BoundUft()?.BoundWorkTable;
            return table == null ? WbgCraftLoopSupport.Missing : WbgCraftLoopSupport.RoleOf(map, table);
        }
    }

    /// <summary>
    /// Whether vanilla's own leave-it-alone test — the anchor's footprint expanded by one — covers
    /// the item where it lies. 0 while the item sits at the member is what makes the haul postfix
    /// necessary rather than decorative.
    /// </summary>
    public sealed class UftVanillaGuardProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_vanilla_guard";
        public string Description => "1 if the anchor's footprint+1 contains the order's unfinished item (vanilla would leave it alone), 0 if not, -2 none.";
        public string Unit => "bool";

        public float Read(Map map)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            Building_WorkTable anchor = WbgCraftLoopSupport.Anchor(map);
            if (uft == null || !uft.Spawned || anchor == null)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return anchor.OccupiedRect().ExpandedBy(1).Contains(uft.Position) ? 1f : 0f;
        }
    }

    /// <summary>
    /// <c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c> for the hauler and the order's item,
    /// through the real method — so the postfix is in the path.
    /// </summary>
    public sealed class UftHaulableProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_haulable";
        public string Description => "1 if the hauler may automatically haul the order's unfinished item, 0 if not, -2 no hauler/item.";
        public string Unit => "bool";

        public float Read(Map map)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            Pawn hauler = WbgTestState.Hauler;
            if (uft == null || !uft.Spawned || hauler == null)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return HaulAIUtility.PawnCanAutomaticallyHaulFast(hauler, uft, forced: false) ? 1f : 0f;
        }
    }

    /// <summary>Whether the order's item is the one remembered earlier — resumed, not restarted.</summary>
    public sealed class UftSameItemProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_uft_same_item";
        public string Description => "1 if the order's unfinished item is the one WbgRememberUft recorded, 0 if a different one, -2 none.";
        public string Unit => "bool";

        public float Read(Map map)
        {
            UnfinishedThing uft = WbgCraftLoopSupport.BoundUft();
            if (uft == null || WbgTestState.RememberedUftId < 0)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return uft.thingIDNumber == WbgTestState.RememberedUftId ? 1f : 0f;
        }
    }

    /// <summary>Remaining count on the first queued order — 0 once the product is made.</summary>
    public sealed class FirstBillRemainingProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_first_bill_remaining";
        public string Description => "repeatCount of the first queued order; drops as products are finished.";
        public string Unit => "count";

        public float Read(Map map)
        {
            Bill_Production bill = WbgTestState.Bills.FirstOrDefault();
            return bill == null ? WbgCraftLoopSupport.Missing : bill.repeatCount;
        }
    }

    /// <summary>
    /// Whether the hauler's control item has reached storage. Without this, "the unfinished item
    /// stayed put" could just mean the hauler never hauled anything.
    /// </summary>
    public sealed class ControlInStorageProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_control_in_storage";
        public string Description => "1 if the control item is in valid storage, 0 if not, -2 none.";
        public string Unit => "bool";

        public float Read(Map map)
        {
            Thing control = WbgTestState.ControlItem;
            if (control == null || control.Destroyed)
            {
                return WbgCraftLoopSupport.Missing;
            }

            // A hauler carrying it is not yet a pass; only a stack at rest in the stockpile is.
            return control.Spawned && control.IsInValidStorage() ? 1f : 0f;
        }
    }

    /// <summary>
    /// Which queued order the crafter is working, by the slot the scenario queued it in. With
    /// <c>wbg_head_bill_slot</c> this shows round robin resuming an interrupted item before
    /// starting the next order.
    /// </summary>
    public sealed class CrafterBillSlotProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_crafter_bill_slot";
        public string Description => "Queued slot of the bill on the crafter's current job, -1 not a scenario bill, -2 no bill job.";
        public string Unit => "slot";

        public float Read(Map map)
        {
            Bill bill = WbgTestState.Crafter?.CurJob?.bill;
            if (bill == null)
            {
                return WbgCraftLoopSupport.Missing;
            }

            return WbgTestState.Bills.IndexOf(bill as Bill_Production);
        }
    }
}
