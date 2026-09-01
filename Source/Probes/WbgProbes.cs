using RimWorld;
using RimWorldTestHarness.Mod.Probes;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>How many benches share the first linked bench's bill list.</summary>
    public sealed class GroupSizeProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_group_size";
        public string Description => "Benches sharing the first linked bench's bill list.";
        public string Unit => "benches";

        public float Read(Map map)
        {
            Building_WorkTable bench = WbgTestState.Benches.Count > 0 ? WbgTestState.Benches[0] : null;
            if (bench == null)
            {
                return -1f;
            }

            return BillGroupIndex.For(map)?.GroupSize(bench) ?? -1f;
        }
    }

    /// <summary>
    /// Bills visible from the *second* bench.
    ///
    /// This is the probe that actually proves sharing. Reading the first bench would pass whether
    /// or not the link worked, because the first bench is usually the anchor and owns the list
    /// either way.
    /// </summary>
    public sealed class SharedBillCountProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_bills_visible_at_second_bench";
        public string Description => "Bills the second linked bench can see. Equals the group's total when sharing works, its own count when it does not.";
        public string Unit => "bills";

        public float Read(Map map)
        {
            if (WbgTestState.Benches.Count < 2)
            {
                return -1f;
            }

            return WbgTestState.Benches[1].billStack?.Count ?? -1f;
        }
    }

    /// <summary>
    /// Which of the queued bills is currently at the top of the shared list, by the order the
    /// scenario queued them. This is how round-robin rotation is observed.
    /// </summary>
    public sealed class HeadBillSlotProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_head_bill_slot";
        public string Description => "Index, in the order the scenario queued them, of the bill now at the top of the shared list.";
        public string Unit => "slot";

        public float Read(Map map)
        {
            Building_WorkTable bench = WbgTestState.Benches.Count > 0 ? WbgTestState.Benches[0] : null;
            BillStack stack = bench?.billStack;
            if (stack == null || stack.Count == 0)
            {
                return -1f;
            }

            return WbgTestState.Bills.IndexOf(stack[0] as Bill_Production);
        }
    }

    /// <summary>
    /// Whether the first queued bill would be started right now — the overshoot guard's answer,
    /// read through vanilla's own method so the Harmony postfix is genuinely in the path.
    /// </summary>
    public sealed class FirstBillShouldDoNowProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_first_bill_should_do_now";
        public string Description => "1 if the first queued bill would be started now, 0 if not. Goes to 0 once its remaining count is fully claimed.";
        public string Unit => "boolean";

        public float Read(Map map)
        {
            if (WbgTestState.Bills.Count == 0)
            {
                return -1f;
            }

            return WbgTestState.Bills[0].ShouldDoNow() ? 1f : 0f;
        }
    }

    /// <summary>The group's ordering mode, so a scenario can assert the toggle took effect.</summary>
    public sealed class OrderingModeProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_ordering_mode";
        public string Description => "0 = in order (vanilla), 1 = round robin.";
        public string Unit => "enum";

        public float Read(Map map)
        {
            Building_WorkTable bench = WbgTestState.Benches.Count > 0 ? WbgTestState.Benches[0] : null;
            Building_WorkTable anchor = BillGroupIndex.For(map)?.AnchorOf(bench);
            CompBillGroup comp = anchor?.GetComp<CompBillGroup>();
            if (comp == null)
            {
                return -1f;
            }

            return comp.Ordering == OrderingMode.RoundRobin ? 1f : 0f;
        }
    }

    /// <summary>
    /// Whether every member of the tracked group points at one and the same BillStack object.
    ///
    /// This is the probe the reload half of the round-trip turns on. Counting bills at the second
    /// bench can pass on two stacks that merely happen to hold the same number — after a load that
    /// is exactly the near-miss to worry about, because without the redirect each bench comes back
    /// holding its own deep-loaded copy of the list. Reference equality is the only reading that
    /// tells sharing apart from coincidence.
    /// </summary>
    public sealed class SharedStackIdentityProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_stacks_reference_equal";
        public string Description => "1 if every tracked bench's billStack is the same object as the first bench's, 0 if any differs.";
        public string Unit => "boolean";

        public float Read(Map map)
        {
            if (WbgTestState.Benches.Count < 2)
            {
                return -1f;
            }

            BillStack anchorStack = WbgTestState.Benches[0].billStack;
            if (anchorStack == null)
            {
                return -1f;
            }

            foreach (Building_WorkTable bench in WbgTestState.Benches)
            {
                if (!ReferenceEquals(bench.billStack, anchorStack))
                {
                    return 0f;
                }
            }

            return 1f;
        }
    }

    /// <summary>
    /// Duplicate load-ID warnings RimWorld logged during the last save.
    ///
    /// This is the direct measurement of the one failure that would silently corrupt a save:
    /// several benches deep-saving the same bills. Anything above zero means the save-time field
    /// swap did not do its job.
    /// </summary>
    public sealed class DuplicateSaveIdProbe : IProbe, IProbeMetadata
    {
        public string Name => "wbg_duplicate_save_ids";
        public string Description => "Duplicate load-ID warnings during the last WbgSaveGame. Must be 0.";
        public string Unit => "warnings";

        public float Read(Map map)
        {
            return WbgSaveGameStep.DuplicateLoadIdWarnings;
        }
    }
}
