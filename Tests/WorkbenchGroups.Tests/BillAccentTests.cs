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
}
