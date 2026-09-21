using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class BillOrderingTests
{
    /// <summary>
    /// Most of these cases predate "do this next" and say nothing about it, so they pass false
    /// and read as they always did. The marked-bill cases are grouped separately below.
    /// </summary>
    private const bool NotMarked = false;

    [Test]
    public void Rotating_the_head_of_a_list_moves_it_to_the_tail()
    {
        Assert.That(
            BillOrdering.TryPlanRotateToTail(4, 0, NotMarked, out int removeAt, out int insertAt),
            Is.True);
        Assert.That(removeAt, Is.EqualTo(0));

        // After removing one entry the list is 3 long, so 3 is an append — the largest index
        // List.Insert accepts. One more would throw from inside a Harmony postfix on a hot path.
        Assert.That(insertAt, Is.EqualTo(3));
    }

    [Test]
    public void A_bill_that_is_not_in_the_list_is_refused()
    {
        // This is the case that corrupts vanilla's BillStack.Reorder: index -1 makes its Remove a
        // no-op and its Insert add a foreign bill to the stack. Reachable whenever the player
        // deletes a bill while a pawn is walking to the bench.
        Assert.That(BillOrdering.TryPlanRotateToTail(4, -1, NotMarked, out _, out _), Is.False);
    }

    [TestCase(4, 4)]
    [TestCase(4, 99)]
    public void An_index_past_the_end_is_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanRotateToTail(count, index, NotMarked, out _, out _), Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    public void Lists_too_short_to_rotate_are_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanRotateToTail(count, index, NotMarked, out _, out _), Is.False);
    }

    [Test]
    public void An_entry_already_at_the_tail_is_left_alone()
    {
        // Not an error, just a no-op: skipping it keeps the tab still when a group is down to one
        // eligible bill, which would otherwise churn visibly on every single craft.
        Assert.That(BillOrdering.TryPlanRotateToTail(3, 2, NotMarked, out _, out _), Is.False);
    }

    [Test]
    public void Applying_the_plan_to_a_real_list_rotates_it()
    {
        List<string> bills = new() { "a", "b", "c" };

        Assert.That(
            BillOrdering.TryPlanRotateToTail(bills.Count, 0, NotMarked, out int removeAt, out int insertAt),
            Is.True);
        string moved = bills[removeAt];
        bills.RemoveAt(removeAt);
        bills.Insert(insertAt, moved);

        Assert.That(bills, Is.EqualTo(new[] { "b", "c", "a" }));
    }

    // ---- "do this next" outranks round robin -------------------------------------------------

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void The_marked_order_is_never_rotated_away(int index)
    {
        // Whatever position it is in, and whatever the rotation would otherwise have done. Without
        // this the two features undo each other once per job start: round robin sends the marked
        // bill to the tail and the marker promotes it straight back, so the list jumps twice per
        // craft and ends up exactly where it began.
        Assert.That(BillOrdering.TryPlanRotateToTail(4, index, true, out _, out _), Is.False);
    }

    [Test]
    public void An_unmarked_bill_in_the_same_list_still_rotates()
    {
        // The marker suppresses rotation for its own bill only. A group whose marked order cannot
        // run must still round-robin everything else, or marking one order would quietly freeze
        // the whole list.
        Assert.That(BillOrdering.TryPlanRotateToTail(4, 1, NotMarked, out int removeAt, out _), Is.True);
        Assert.That(removeAt, Is.EqualTo(1));
    }

    // ---- promote to head ---------------------------------------------------------------------

    [Test]
    public void Promoting_moves_the_entry_to_the_front()
    {
        Assert.That(BillOrdering.TryPlanPromoteToHead(4, 2, out int removeAt, out int insertAt), Is.True);
        Assert.That(removeAt, Is.EqualTo(2));
        Assert.That(insertAt, Is.EqualTo(0));
    }

    [Test]
    public void Promoting_the_last_entry_is_allowed()
    {
        // The mirror of the rotation's "already at the tail" no-op, and the common case: the
        // order you suddenly need is usually the one you just added, which vanilla appends.
        Assert.That(BillOrdering.TryPlanPromoteToHead(3, 2, out int removeAt, out int insertAt), Is.True);
        Assert.That(removeAt, Is.EqualTo(2));
        Assert.That(insertAt, Is.EqualTo(0));
    }

    [Test]
    public void Promoting_an_entry_already_at_the_head_is_refused()
    {
        // A no-op remove-and-insert would still read as a list mutation to anything watching, and
        // this plan is re-applied every time the list is rebuilt underneath the marker.
        Assert.That(BillOrdering.TryPlanPromoteToHead(4, 0, out _, out _), Is.False);
    }

    [Test]
    public void Promoting_a_bill_that_is_not_in_the_list_is_refused()
    {
        // Same corruption as the rotation case: the marked bill can be deleted at any moment, and
        // a plan computed against index -1 would move whatever happens to be at the end instead.
        Assert.That(BillOrdering.TryPlanPromoteToHead(4, -1, out _, out _), Is.False);
    }

    [TestCase(4, 4)]
    [TestCase(4, 99)]
    public void Promoting_an_index_past_the_end_is_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanPromoteToHead(count, index, out _, out _), Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    public void Promoting_in_a_list_too_short_to_reorder_is_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanPromoteToHead(count, index, out _, out _), Is.False);
    }

    [Test]
    public void Applying_the_promotion_to_a_real_list_moves_only_the_marked_entry()
    {
        List<string> bills = new() { "a", "b", "c", "d" };

        Assert.That(
            BillOrdering.TryPlanPromoteToHead(bills.Count, 2, out int removeAt, out int insertAt),
            Is.True);
        string moved = bills[removeAt];
        bills.RemoveAt(removeAt);
        bills.Insert(insertAt, moved);

        // Everything else keeps its relative order, which is what makes the marker an override of
        // the player's arrangement rather than a replacement for it.
        Assert.That(bills, Is.EqualTo(new[] { "c", "a", "b", "d" }));
    }

    // ---- when a marker has been satisfied ----------------------------------------------------

    [TestCase(0)]
    [TestCase(-1)]
    public void A_do_x_times_order_with_nothing_left_is_spent(int repeatCount)
    {
        // Negative as well as zero. Vanilla clamps at zero in Notify_IterationCompleted, but
        // treating "at or below" as done costs nothing and closes the case for good.
        Assert.That(BillOrdering.IsNextOrderSpent(RepeatModeCode.RepeatCount, repeatCount), Is.True);
    }

    [Test]
    public void A_do_x_times_order_with_work_left_is_not_spent()
    {
        Assert.That(BillOrdering.IsNextOrderSpent(RepeatModeCode.RepeatCount, 1), Is.False);
    }

    [TestCase(RepeatModeCode.Forever)]
    [TestCase(RepeatModeCode.TargetCount)]
    public void The_other_repeat_modes_never_report_themselves_spent(RepeatModeCode mode)
    {
        // Deliberate, and the honest limit of the feature. "Do forever" has no completion at all,
        // and "do until you have X" can only be answered by a map-wide product count — far too
        // expensive for a test consulted once per visible row per frame. Markers in those modes
        // stay until the player clears them, and the tooltip says so.
        //
        // The repeat count passed is zero, the value that *would* make RepeatCount report spent,
        // so this fails rather than passing vacuously if the mode check is ever dropped.
        Assert.That(BillOrdering.IsNextOrderSpent(mode, 0), Is.False);
    }
}
