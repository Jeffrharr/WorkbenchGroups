using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Makes Nice Bill Tab report one queued bill as <c>BillStatus.NoOneCanDo</c>, its red state.
    ///
    /// This is a test-only override of *their* code, and it exists because the state cannot be
    /// reached honestly. Nice Bill Tab 1.6 declares <c>NoOneCanDo</c>, gives it a red stripe in
    /// <c>DrawStatusedBillBackground</c>, and ships a <c>BillValidator.CanAnyOneExecuteBill</c>
    /// that would compute it — but nothing calls the validator, and <c>GetBillStatus</c> only
    /// ever returns Processed, Pending or Doned. Setting every cook's priority to zero changes
    /// nothing on their tab. Our compat layer still handles the status, because a future release
    /// that wires the validator up would otherwise have our green or blue painted over their red.
    ///
    /// So the only way to see that rule on screen is to hand their drawer the status it would
    /// receive. A postfix on <c>GetBillStatus</c> does exactly that and nothing else: their own
    /// row code then paints the red, and our prefix reads the same argument it would read in the
    /// real case. A capture made with this step says "this is what our layer does when they say
    /// red", not "this is a state players see today" — captions must say so.
    ///
    /// Patched by name for the same reason the shipped compat layer is: a hard reference to
    /// NiceBillTab.dll would make this whole bridge fail to load in every scenario without it.
    /// </summary>
    public sealed class WbgForceNiceBillTabStatusStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgForceNiceBillTabStatus";

        /// <summary>Their <c>BillStatus.NoOneCanDo</c>, pinned by NiceBillTabApiTests.</summary>
        private const int NoOneCanDo = 4;

        private static readonly HashSet<Bill> Forced = new HashSet<Bill>();

        private static bool patched;

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgForceNiceBillTabStatus requires 'slot' (the index WbgAddBill queued the bill at)";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            int slot = int.Parse(args["slot"]);
            if (slot < 0 || slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail(
                    $"WbgForceNiceBillTabStatus: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            if (!patched)
            {
                MethodInfo target = AccessTools.Method(
                    AccessTools.TypeByName("NiceBillTab.TabBillsDrawer"), "GetBillStatus");
                if (target == null)
                {
                    return StepOutcome.Fail(
                        "WbgForceNiceBillTabStatus: NiceBillTab.TabBillsDrawer.GetBillStatus not found — is Nice Bill Tab active?");
                }

                new Harmony("joof.workbenchgroups.probes.nbtstatus").Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(WbgForceNiceBillTabStatusStep), nameof(GetBillStatusPostfix)));
                patched = true;
            }

            Forced.Add(WbgTestState.Bills[slot]);
            return new StepOutcome();
        }

        /// <summary>
        /// <c>ref object</c> because their return type is their own enum; Harmony boxes it, and
        /// <c>Enum.ToObject</c> puts back a value of that same enum type.
        /// </summary>
        private static void GetBillStatusPostfix(Bill bill, ref object __result)
        {
            if (bill != null && __result != null && Forced.Contains(bill))
            {
                __result = Enum.ToObject(__result.GetType(), NoOneCanDo);
            }
        }
    }
}
