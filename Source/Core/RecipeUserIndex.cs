using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>One recipe as it appears on a bench, once the index has resolved it there.</summary>
    public readonly struct BenchRecipe
    {
        public readonly string DefName;
        public readonly RecipeShape Shape;

        public BenchRecipe(string defName, RecipeShape shape)
        {
            DefName = defName;
            Shape = shape;
        }
    }

    /// <summary>
    /// One recipe as the index reads it: its name, the shape <see cref="RecipeGate"/> judges, and
    /// the bench defNames it nominates as users.
    /// </summary>
    public readonly struct RecipeEntry
    {
        public readonly string DefName;
        public readonly RecipeShape Shape;
        public readonly IReadOnlyList<string> UserDefNames;

        public RecipeEntry(string defName, RecipeShape shape, IReadOnlyList<string> userDefNames)
        {
            DefName = defName;
            Shape = shape;
            UserDefNames = userDefNames;
        }
    }

    /// <summary>
    /// Works out which recipes a bench offers without asking <c>ThingDef.AllRecipes</c>.
    ///
    /// This exists for one reason, and it is a compatibility reason rather than a performance one.
    /// <c>ThingDef.AllRecipes</c> is lazily built and cached in <c>allRecipesCached</c> on first
    /// access, and vanilla never invalidates that cache — there is no setter and no reset. Our
    /// comp injector runs in a <c>[StaticConstructorOnStartup]</c> and needs every bench's recipes
    /// to decide eligibility, so reading the property there would make *us* the thing that freezes
    /// the list for the entire game. A framework mod editing <c>recipeUsers</c> any later than our
    /// static constructor would then find its change invisible — not merely invisible to us, but
    /// to everything that reads <c>AllRecipes</c> afterwards, because we already built the cache.
    ///
    /// Measured before writing this: nothing in vanilla reads <c>AllRecipes</c> during startup
    /// (all ten call sites are gameplay, UI or debug), and of the mods we declare a load order
    /// against, the earliest reader is Nice Bill Tab - Expansion's preloader, which runs from a
    /// <c>GameComponent</c> at game load. So we really were first, and this really was ours to fix.
    ///
    /// The replacement reproduces the getter's two sources exactly — a def's own <c>recipes</c>
    /// list, then every recipe whose <c>recipeUsers</c> names that def — so the answer is the same
    /// list vanilla would have produced, in the same order, including any duplicate a recipe that
    /// is in both would produce. It is a copy of vanilla's logic rather than a cleverer version on
    /// purpose: the point is to stop touching the cache, not to disagree about its contents.
    ///
    /// This does not make our answer live. We still freeze at static-constructor time, because
    /// that is when comps must be injected. What changes is that we freeze it for ourselves in our
    /// own dictionary instead of freezing vanilla's cache for everybody.
    /// </summary>
    public static class RecipeUserIndex
    {
        /// <summary>
        /// Inverts recipes-to-benches into benches-to-recipes.
        ///
        /// Built once over the whole recipe database rather than asked per bench, because the
        /// per-bench question ("which recipes name me?") is a scan of every recipe, and asking it
        /// for every bench def is the quadratic version of the same work vanilla's getter does.
        ///
        /// Insertion order is preserved per bench so the result matches the order vanilla's getter
        /// would append in, which is database order. Callers only ask set-shaped questions of the
        /// result today, but a list that silently differs in order from the one it replaces is the
        /// kind of difference that surfaces years later in someone's UI.
        /// </summary>
        public static Dictionary<string, List<BenchRecipe>> Build(IEnumerable<RecipeEntry> recipes)
        {
            var index = new Dictionary<string, List<BenchRecipe>>();
            if (recipes == null)
            {
                return index;
            }

            foreach (RecipeEntry recipe in recipes)
            {
                if (recipe.UserDefNames != null)
                {
                    foreach (string user in recipe.UserDefNames)
                    {
                        if (!string.IsNullOrEmpty(user))
                        {
                            if (!index.ContainsKey(user))
                            {
                                index[user] = new List<BenchRecipe>();
                            }

                            index[user].Add(new BenchRecipe(recipe.DefName, recipe.Shape));
                        }
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// The full recipe list for one bench def: the recipes it declares itself, followed by the
        /// ones that nominated it. Mirrors the order <c>ThingDef.AllRecipes</c> builds in.
        ///
        /// <paramref name="ownRecipes"/> is the def's own <c>recipes</c> field, which is a plain
        /// list and safe to read — it is <c>AllRecipes</c>, the derived and cached one, that must
        /// not be touched.
        /// </summary>
        public static List<BenchRecipe> RecipesOn(
            Dictionary<string, List<BenchRecipe>> index,
            string benchDefName,
            IEnumerable<BenchRecipe> ownRecipes)
        {
            var result = new List<BenchRecipe>();

            if (ownRecipes != null)
            {
                result.AddRange(ownRecipes);
            }

            if (index != null
                && !string.IsNullOrEmpty(benchDefName)
                && index.ContainsKey(benchDefName))
            {
                result.AddRange(index[benchDefName]);
            }

            return result;
        }
    }
}
