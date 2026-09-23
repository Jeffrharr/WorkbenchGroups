using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Aims an unfinished-item order's resume job at the bench the pawn walked to, instead of the
    /// bench that owns the list.
    ///
    /// <c>FinishUftJob</c> reads <c>bill.billStack.billGiver</c> twice — once for the haul-off
    /// that clears the bench's ingredient cells, once for the resume job's <c>targetA</c>. Each of
    /// those two-instruction reads (<c>ldfld Bill::billStack</c>, <c>ldfld BillStack::billGiver</c>)
    /// is replaced by one <c>call UnfinishedItemSharing.ResumeGiver(Bill)</c>. That is
    /// stack-neutral — one <c>Bill</c> in, one <c>IBillGiver</c> out, exactly like the pair — and
    /// it matches the field reads themselves, so it does not care how the bill got onto the stack.
    ///
    /// A transpiler rather than a prefix that re-implements the method, because the method is
    /// eleven lines of vanilla we would otherwise be copying and keeping in step, and because
    /// Hauler's Dream and friends patch around <c>WorkGiver_DoBill</c> — a transpiler composes
    /// with their patches where a skipping prefix would not.
    ///
    /// All or nothing: the rewrite happens only when there are exactly
    /// <see cref="IlPairRewrite.ExpectedFinishUftJobMatches"/> matches. On any other count the
    /// method has changed shape, so we log, return the original IL, and leave
    /// <see cref="UnfinishedItemSharing.RedirectInstalled"/> false — which makes
    /// <c>BenchEligibility.IsShareableBill</c> refuse unfinished-item orders again. The mod falls
    /// back to its old behaviour instead of shipping half a redirect.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "FinishUftJob")]
    public static class Patch_WorkGiver_DoBill_FinishUftJob
    {
        private static readonly FieldInfo BillStackField = AccessTools.Field(typeof(Bill), nameof(Bill.billStack));

        private static readonly FieldInfo BillGiverField = AccessTools.Field(typeof(BillStack), nameof(BillStack.billGiver));

        private static readonly MethodInfo ResumeGiver =
            AccessTools.Method(typeof(UnfinishedItemSharing), nameof(UnfinishedItemSharing.ResumeGiver));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<int> starts = IlPairRewrite.FindPairs(code, IsBillStackRead, IsBillGiverRead);

            if (!IlPairRewrite.ShouldRewrite(starts.Count, IlPairRewrite.ExpectedFinishUftJobMatches))
            {
                Log.Error($"[Workbench Groups] WorkGiver_DoBill.FinishUftJob has changed shape: expected "
                    + $"{IlPairRewrite.ExpectedFinishUftJobMatches} reads of bill.billStack.billGiver, "
                    + $"found {starts.Count}. Leaving it unpatched; orders that leave an unfinished item "
                    + "behind will not be allowed in shared lists until the mod is updated.");
                UnfinishedItemSharing.NotifyRedirect(false);
                return code;
            }

            UnfinishedItemSharing.NotifyRedirect(true);
            return Rewrite(code, new HashSet<int>(starts));
        }

        private static bool IsBillStackRead(CodeInstruction instruction)
        {
            return instruction.LoadsField(BillStackField);
        }

        private static bool IsBillGiverRead(CodeInstruction instruction)
        {
            return instruction.LoadsField(BillGiverField);
        }

        /// <summary>
        /// Emits the method with each matched pair collapsed to one call.
        ///
        /// Labels and exception blocks are carried from both replaced instructions onto the call.
        /// Only the first can carry them in today's IL, but a branch landing on either one would
        /// otherwise land nowhere — an invalid-program crash the first time a pawn resumes a shirt.
        /// </summary>
        private static List<CodeInstruction> Rewrite(List<CodeInstruction> code, HashSet<int> starts)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(code.Count);

            int i = 0;
            while (i < code.Count)
            {
                if (starts.Contains(i))
                {
                    CodeInstruction call = new CodeInstruction(OpCodes.Call, ResumeGiver);
                    call.MoveLabelsFrom(code[i]).MoveBlocksFrom(code[i]);
                    call.MoveLabelsFrom(code[i + 1]).MoveBlocksFrom(code[i + 1]);
                    result.Add(call);
                    i += 2;
                }
                else
                {
                    result.Add(code[i]);
                    i += 1;
                }
            }

            return result;
        }
    }
}
