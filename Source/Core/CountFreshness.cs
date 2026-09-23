namespace WorkbenchGroups.Core
{
    /// <summary>
    /// When a cached product count may be reused instead of recounted.
    ///
    /// <c>RecipeWorkerCounter.CountProducts</c> has a fast path — one dictionary lookup in the
    /// map's resource counter — and a slow one that walks every thing of the product's def, every
    /// minified thing and every haul source on the map. The slow one is taken by any bill with a
    /// quality range, hit-point range, include-zone or stuff limit, which is most bills on exactly
    /// the tailoring and smithing benches where "one of each first" is most attractive. The
    /// urgency sort wants a count per bill per scan, so it gets a short-lived cache.
    /// </summary>
    public static class CountFreshness
    {
        /// <summary>
        /// One in-game second. Stock moves at craft completion, hauling and consumption — minutes
        /// apart on any bench — so a second is far finer than the thing it measures. The two
        /// events that must not wait for it, a job starting and a job ending on the bill, drop its
        /// entry outright rather than relying on the clock.
        /// </summary>
        public const int TtlTicks = 60;

        /// <summary>
        /// Whether a count stamped at <paramref name="stampedAt"/> is still good at
        /// <paramref name="now"/>.
        /// </summary>
        /// <param name="stampedAt">Tick the count was taken, or negative for "never counted".</param>
        /// <param name="now">The current tick.</param>
        /// <param name="ttl">Lifetime in ticks. Zero or less means never reuse.</param>
        public static bool IsFresh(int stampedAt, int now, int ttl)
        {
            if (stampedAt < 0 || ttl <= 0)
            {
                return false;
            }

            // A stamp from the future is a count taken in a game that was since reloaded to an
            // earlier save — the cache is not saved, but a static one would survive the reload.
            // Stale rather than fresh-for-a-very-long-time.
            if (now < stampedAt)
            {
                return false;
            }

            return now - stampedAt < ttl;
        }
    }
}
