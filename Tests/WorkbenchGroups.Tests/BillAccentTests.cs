using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class BillAccentTests
{
    [TestCase(false, false, false)]
    [TestCase(true, true, true)]
    [TestCase(false, true, false)]
    public void Blocked_outranks_every_other_state(bool here, bool elsewhere, bool nextUp)
    {
        // Red is the one that contradicts the others rather than ranking against them: a bill can
        // be in progress and then have its work type set to priority zero, and a row claiming
        // "starting next" in blue while nobody can start it is worse than no colour at all.
        Assert.That(
            BillAccentRule.Classify(blocked: true, workedHere: here, workedElsewhere: elsewhere, isNextUp: nextUp),
            Is.EqualTo(BillAccent.Blocked));
    }

    [Test]
    public void A_bill_nobody_is_on_and_that_is_not_next_gets_nothing()
    {
        Assert.That(
            BillAccentRule.Classify(blocked: false, workedHere: false, workedElsewhere: false, isNextUp: false),
            Is.EqualTo(BillAccent.None));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Work_at_this_bench_outranks_everything(bool elsewhere, bool nextUp)
    {
        // A "do 10 times" bill can be in progress here and still have room to start again, so
        // these genuinely co-occur. The nearest, most concrete fact wins.
        Assert.That(
            BillAccentRule.Classify(blocked: false, workedHere: true, workedElsewhere: elsewhere, isNextUp: nextUp),
            Is.EqualTo(BillAccent.WorkedHere));
    }

    [Test]
    public void Next_up_outranks_work_at_another_bench()
    {
        // The deliberate ordering: to a player standing at this bench, "this is what starts here
        // next" is more use than "the other bench is on it".
        Assert.That(
            BillAccentRule.Classify(blocked: false, workedHere: false, workedElsewhere: true, isNextUp: true),
            Is.EqualTo(BillAccent.NextUp));
    }

    [Test]
    public void Work_at_another_bench_shows_when_nothing_else_applies()
    {
        Assert.That(
            BillAccentRule.Classify(blocked: false, workedHere: false, workedElsewhere: true, isNextUp: false),
            Is.EqualTo(BillAccent.WorkedElsewhere));
    }

    [Test]
    public void Next_up_shows_when_nobody_is_on_it()
    {
        Assert.That(
            BillAccentRule.Classify(blocked: false, workedHere: false, workedElsewhere: false, isNextUp: true),
            Is.EqualTo(BillAccent.NextUp));
    }

    [TestCase(BillAccent.None)]
    [TestCase(BillAccent.WorkedHere)]
    [TestCase(BillAccent.NextUp)]
    [TestCase(BillAccent.WorkedElsewhere)]
    public void A_marked_row_takes_the_fill_whatever_the_accent(BillAccent accent)
    {
        // The marked order is at the head, so it is usually the blue "next up" row as well; a red
        // wash over a blue one reads as violet, which means neither. The accent keeps its edge bar.
        Assert.That(BillAccentRule.WashFor(accent, marked: true, compact: false), Is.EqualTo(RowWash.NextOrder));
    }

    [TestCase(BillAccent.WorkedHere, RowWash.Accent)]
    [TestCase(BillAccent.NextUp, RowWash.Accent)]
    [TestCase(BillAccent.WorkedElsewhere, RowWash.None)]
    [TestCase(BillAccent.Blocked, RowWash.None)]
    [TestCase(BillAccent.None, RowWash.None)]
    public void An_unmarked_row_is_filled_only_by_the_strong_accents(BillAccent accent, RowWash expected)
    {
        Assert.That(BillAccentRule.WashFor(accent, marked: false, compact: false), Is.EqualTo(expected));
    }

    [TestCase(BillAccent.WorkedHere, true)]
    [TestCase(BillAccent.NextUp, false)]
    [TestCase(BillAccent.None, true)]
    [TestCase(BillAccent.Blocked, true)]
    public void A_compact_host_gets_no_fill_from_us(BillAccent accent, bool marked)
    {
        // Nice Bill Tab tints the row background itself; we recolour that tint instead.
        Assert.That(BillAccentRule.WashFor(accent, marked, compact: true), Is.EqualTo(RowWash.None));
    }

    [TestCase(BillAccent.WorkedHere, true)]
    [TestCase(BillAccent.NextUp, true)]
    [TestCase(BillAccent.WorkedElsewhere, false)]
    [TestCase(BillAccent.Blocked, false)]
    [TestCase(BillAccent.None, false)]
    public void Only_claims_about_this_bench_fill_the_row(BillAccent accent, bool expected)
    {
        // Found on screen: when work elsewhere recoloured Nice Bill Tab's stripes, it was the same
        // green as work here and the two rows could not be told apart.
        Assert.That(BillAccentRule.FillsRow(accent), Is.EqualTo(expected));
    }

    [TestCase(BillAccent.None)]
    [TestCase(BillAccent.WorkedHere)]
    [TestCase(BillAccent.NextUp)]
    [TestCase(BillAccent.WorkedElsewhere)]
    public void A_marked_row_is_painted_red_in_the_compact_host_too(BillAccent accent)
    {
        // Red means "do this next" in both tabs. The first version left the marked row with only
        // an outline while their NoOneCanDo row was red, and the red row read as the priority one.
        Assert.That(BillAccentRule.StripeFor(accent, marked: true), Is.EqualTo(RowWash.NextOrder));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Blocked_is_grey_and_outranks_even_the_marker(bool marked)
    {
        Assert.That(BillAccentRule.StripeFor(BillAccent.Blocked, marked), Is.EqualTo(RowWash.Blocked));
    }

    [TestCase(BillAccent.WorkedHere, RowWash.Accent)]
    [TestCase(BillAccent.NextUp, RowWash.Accent)]
    [TestCase(BillAccent.WorkedElsewhere, RowWash.None)]
    [TestCase(BillAccent.None, RowWash.None)]
    public void Unmarked_compact_rows_follow_the_fill_rule(BillAccent accent, RowWash expected)
    {
        Assert.That(BillAccentRule.StripeFor(accent, marked: false), Is.EqualTo(expected));
    }

    [Test]
    public void Nothing_but_the_marker_is_ever_red()
    {
        // The invariant the user asked for, stated once over the whole input space: no state of
        // an unmarked row, in either tab, paints it red.
        foreach (BillAccent accent in System.Enum.GetValues(typeof(BillAccent)))
        {
            Assert.That(BillAccentRule.StripeFor(accent, marked: false), Is.Not.EqualTo(RowWash.NextOrder), accent.ToString());
            Assert.That(BillAccentRule.WashFor(accent, marked: false, compact: false), Is.Not.EqualTo(RowWash.NextOrder), accent.ToString());
        }
    }
}
