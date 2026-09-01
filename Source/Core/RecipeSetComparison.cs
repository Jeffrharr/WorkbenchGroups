using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Decides whether two benches make the same things, which is this mod's linkability rule.
    ///
    /// Why identical recipe sets rather than "any two benches": a shared bill list is offered
    /// wholesale to every member, and vanilla's bill selection has no notion of "this bill is
    /// only valid at some of these benches". Linking a stove to a tailoring bench would put
    /// meals in the tailor's tab and send cooks to it. Requiring the same set makes every bill
    /// trivially valid everywhere in the group, which is what lets us leave vanilla's selection
    /// loop untouched.
    ///
    /// It is a *set* comparison rather than a def comparison so an electric and a fueled stove —
    /// different defs, same recipes — can link, which is the case players actually hit.
    /// </summary>
    public static class RecipeSetComparison
    {
        private static readonly string[] None = new string[0];

        /// <summary>
        /// Order-insensitive, duplicate-tolerant equality over recipe defNames. Duplicates are
        /// counted rather than collapsed: a def that somehow lists a recipe twice is not the same
        /// bench as one that lists it once, and silently treating them as equal would hide a
        /// genuine def error behind our feature.
        /// </summary>
        public static bool SameRecipeSet(string[] a, string[] b)
        {
            string[] left = a ?? None;
            string[] right = b ?? None;

            if (left.Length != right.Length)
            {
                return false;
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(left.Length);
            foreach (string name in left)
            {
                counts.TryGetValue(name, out int seen);
                counts[name] = seen + 1;
            }

            foreach (string name in right)
            {
                if (!counts.TryGetValue(name, out int remaining) || remaining == 0)
                {
                    return false;
                }

                counts[name] = remaining - 1;
            }

            // Equal lengths plus every right-hand entry consumed a left-hand one means the
            // residual counts are all zero; no second pass needed.
            return true;
        }
    }
}
