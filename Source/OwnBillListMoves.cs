namespace WorkbenchGroups
{
    /// <summary>
    /// A counter bumped every time this mod deliberately rearranges a shared bill list: a
    /// round-robin rotation, a "do this next" promotion, restoring the authored order.
    ///
    /// Exists for tabs that cache the list. Nice Bill Tab draws from a filtered copy it rebuilds
    /// only when its own code flags it (a delete, a drop, a paste, typing in the search box), so a
    /// move made by us — which it has no way to hear about — stayed invisible in an open tab until
    /// the player happened to do one of those. Found when the first live press of its "do this
    /// next" button marked the order, moved it to the head, and left it drawn third. A version
    /// number rather than an event keeps the cost where the tab is: the compat layer compares one
    /// integer per draw and only asks for a rebuild when something actually moved.
    /// </summary>
    public static class OwnBillListMoves
    {
        public static int Version { get; private set; }

        public static void Note()
        {
            Version++;
        }
    }
}
