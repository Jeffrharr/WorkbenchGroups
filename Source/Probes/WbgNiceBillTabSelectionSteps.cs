using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;
using WorkbenchGroups.Compat;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Selects a queued bill in Nice Bill Tab's list through their own <c>SelectBill</c> — the
    /// call a click on the row makes, including shift-click's <c>add</c>. <c>slot</c> -1 clears
    /// the selection. Resolved by name, like everything that touches their assembly.
    /// </summary>
    public sealed class WbgNbtSelectBillStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgNbtSelectBill";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;
            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgNbtSelectBill requires 'slot' (queued index, or -1 to clear)";
                return false;
            }

            if (args.TryGetValue("add", out string add) && !bool.TryParse(add, out _))
            {
                error = $"WbgNbtSelectBill: 'add' is not a boolean (got '{add}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            MethodInfo select = AccessTools.Method(
                AccessTools.TypeByName("NiceBillTab.TabBillsDrawer"), "SelectBill");
            if (select == null)
            {
                return StepOutcome.Fail("WbgNbtSelectBill: TabBillsDrawer.SelectBill not found — is Nice Bill Tab active?");
            }

            int slot = int.Parse(args["slot"]);
            if (slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail($"WbgNbtSelectBill: slot {slot} out of range");
            }

            Bill bill = slot < 0 ? null : WbgTestState.Bills[slot];
            bool add = args.TryGetValue("add", out string raw) && bool.Parse(raw);
            select.Invoke(null, new object[] { bill, add });
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Presses the "do this next" button above Nice Bill Tab's list, through the same handler
    /// the button calls. <c>expectActed</c> fails the step if the press did (or did not) act, so
    /// the refused cases — nothing selected, several selected — are asserted, not just survived.
    /// </summary>
    public sealed class WbgNbtPressDoNextStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgNbtPressDoNext";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;
            if (!args.TryGetValue("expectActed", out string raw) || !bool.TryParse(raw, out _))
            {
                error = "WbgNbtPressDoNext requires boolean 'expectActed'";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            Building_WorkTable bench = Find.Selector?.SingleSelectedThing as Building_WorkTable;
            if (bench == null)
            {
                return StepOutcome.Fail("WbgNbtPressDoNext: no single bench selected — run WbgFocusBench first");
            }

            bool expected = bool.Parse(args["expectActed"]);
            bool acted = NiceBillTabCompat.PressDoNext(bench);
            return acted == expected
                ? new StepOutcome()
                : StepOutcome.Fail($"WbgNbtPressDoNext: pressing acted={acted}, expected {expected}");
        }
    }
}
