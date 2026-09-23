using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class RowMotionTests
{
    [Test]
    public void Only_work_at_this_bench_animates()
    {
        // Their tab animates "first bill anyone is working", which in a group can be the other
        // bench's. The bench whose tab is open is the only one a moving row may speak for.
        foreach (BillAccent accent in System.Enum.GetValues(typeof(BillAccent)))
        {
            Assert.That(RowMotionRule.Animates(accent), Is.EqualTo(accent == BillAccent.WorkedHere), accent.ToString());
        }
    }

    [TestCase(NbtBillStatus.Pending)]
    [TestCase(NbtBillStatus.Processed)]
    [TestCase(NbtBillStatus.Doned)]
    public void A_row_worked_here_always_moves(NbtBillStatus theirs)
    {
        // Pending: a second bill worked here, below the one their cache picked, or their 30-tick
        // cache not caught up yet. Doned: their own precedence puts "being worked" above "would
        // not start now", so a row mid-make whose target was just reached still moves.
        Assert.That(
            RowMotionRule.StatusFor(theirs, isProduction: true, BillAccent.WorkedHere, wouldStartNow: false),
            Is.EqualTo(NbtBillStatus.Processed));
    }

    [TestCase(BillAccent.WorkedElsewhere)]
    [TestCase(BillAccent.NextUp)]
    [TestCase(BillAccent.None)]
    public void A_row_they_animated_but_not_worked_here_rests_as_pending(BillAccent accent)
    {
        // The headline case: the other bench's bill sits highest in the shared list, so their tab
        // chose it. It rests at their "would start" look, the same one it would have without us.
        Assert.That(
            RowMotionRule.StatusFor(NbtBillStatus.Processed, isProduction: true, accent, wouldStartNow: true),
            Is.EqualTo(NbtBillStatus.Pending));
    }

    [Test]
    public void A_row_stopped_from_moving_that_would_not_start_now_rests_as_doned()
    {
        // Mirrors their GetBillStatus exactly for the row it would otherwise have produced, so the
        // text greying they apply to Doned rows isn't lost.
        Assert.That(
            RowMotionRule.StatusFor(NbtBillStatus.Processed, isProduction: true, BillAccent.WorkedElsewhere, wouldStartNow: false),
            Is.EqualTo(NbtBillStatus.Doned));
    }

    [TestCase(NbtBillStatus.Pending)]
    [TestCase(NbtBillStatus.Doned)]
    public void A_still_row_not_worked_here_is_left_as_they_said(NbtBillStatus theirs)
    {
        Assert.That(
            RowMotionRule.StatusFor(theirs, isProduction: true, BillAccent.WorkedElsewhere, wouldStartNow: true),
            Is.EqualTo(theirs));
    }

    [TestCase(BillAccent.WorkedHere)]
    [TestCase(BillAccent.Blocked)]
    [TestCase(BillAccent.None)]
    public void NoOneCanDo_is_never_rewritten(BillAccent accent)
    {
        // Their "nobody can do this" keeps its own (grey, by StripeFor) still row even while a
        // pawn is on it: scrolling grey would say "stuck" and "progressing" at once.
        Assert.That(
            RowMotionRule.StatusFor(NbtBillStatus.NoOneCanDo, isProduction: true, accent, wouldStartNow: true),
            Is.EqualTo(NbtBillStatus.NoOneCanDo));
    }

    [Test]
    public void Blocked_does_not_animate_even_though_the_row_may_be_worked()
    {
        Assert.That(RowMotionRule.Animates(BillAccent.Blocked), Is.False);
    }

    [TestCase(NbtBillStatus.Processed, BillAccent.None)]
    [TestCase(NbtBillStatus.Pending, BillAccent.WorkedHere)]
    [TestCase(NbtBillStatus.Doned, BillAccent.WorkedHere)]
    public void Non_production_bills_are_left_alone(NbtBillStatus theirs, BillAccent accent)
    {
        // Bill_Autonomous (mech gestation) is Processed because it is gestating, not because a
        // pawn is at a bench; "here" means nothing for it, so their animation stands.
        Assert.That(
            RowMotionRule.StatusFor(theirs, isProduction: false, accent, wouldStartNow: true),
            Is.EqualTo(theirs));
    }

    [Test]
    public void Paused_is_passed_through()
    {
        // Declared by them and never produced; we don't guess what it would mean.
        Assert.That(
            RowMotionRule.StatusFor(NbtBillStatus.Paused, isProduction: true, BillAccent.WorkedHere, wouldStartNow: true),
            Is.EqualTo(NbtBillStatus.Paused));
    }

    [TestCase(false, RowWash.Accent)]
    [TestCase(true, RowWash.NextOrder)]
    public void A_row_worked_here_moves_green_unless_it_is_the_marked_order_which_moves_red(bool marked, RowWash expected)
    {
        // Motion and hue are separate channels. Motion is the accent's ("being made here"), the hue
        // goes to the marker when there is one, because red means "do this next" and nothing else.
        // So the marked order being made here scrolls red; every other row made here scrolls green.
        Assert.Multiple(() =>
        {
            Assert.That(RowMotionRule.Animates(BillAccent.WorkedHere), Is.True);
            Assert.That(BillAccentRule.StripeFor(BillAccent.WorkedHere, marked), Is.EqualTo(expected));
        });
    }

    [Test]
    public void A_row_worked_elsewhere_is_neither_moving_nor_recoloured()
    {
        // It speaks through the edge bar alone; their resting tint is left under it.
        Assert.Multiple(() =>
        {
            Assert.That(RowMotionRule.Animates(BillAccent.WorkedElsewhere), Is.False);
            Assert.That(BillAccentRule.StripeFor(BillAccent.WorkedElsewhere, marked: false), Is.EqualTo(RowWash.None));
        });
    }
}
