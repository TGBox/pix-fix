using PixFix.Core.Calls;

namespace PixFix.Core.Tests;

public class CallRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static Call NewCall() => new() { Id = "c", CreatedUtc = Now, UpdatedUtc = Now };

    [Theory]
    [InlineData("0351 / 889-06 10", "003518890610")]
    [InlineData("+49 351 8890610", "0+493518890610")]
    [InlineData("  ", null)]
    [InlineData("keine", null)]
    public void DialStringKeepsDigitsAndPrefixesZero(string number, string? expected)
    {
        Assert.Equal(expected, CallRules.DialString(number));
    }

    [Fact]
    public void PhoneOptionsSkipEmptyNumbersAndPreferAlternative()
    {
        var call = NewCall() with { Phone1 = "1", Mobile = "2", CallbackTarget = "Mobil" };

        Assert.Equal(new[] { "Tel.", "Mobil" }, CallRules.PhoneOptions(call).Select(o => o.Label));
        Assert.Equal("Mobil", CallRules.PreferredPhoneLabel(call));
        Assert.Equal(CallRules.AlternativeNumberLabel, CallRules.PreferredPhoneLabel(call with { AlternativePhone = "3" }));
    }

    [Fact]
    public void EmailAddressFollowsTarget()
    {
        var call = NewCall() with { Email1 = "a@x.invalid", Email2 = "b@x.invalid" };

        Assert.Equal("b@x.invalid", CallRules.EmailAddress(call with { EmailTarget = "E-Mail2" }));
        Assert.Equal("a@x.invalid", CallRules.EmailAddress(call with { EmailTarget = "E-Mail" }));
        Assert.Equal("b@x.invalid", CallRules.EmailAddress(call with { Email1 = "", EmailTarget = "E-Mail" }));
        Assert.Null(CallRules.EmailAddress(NewCall()));
    }

    [Fact]
    public void TemplateUsesCallFields()
    {
        var call = NewCall() with { CustomerNumber = "00074", CustomerId = "1888", ReasonText = "KIM", SolutionText = "erklärt" };

        Assert.Equal("L 00074 / 1888: KIM --> erklärt", CallRules.ApplyTemplate(call, "L {LNUM} / {ID}: {WARUM} --> {LOESUNG}"));
    }

    [Fact]
    public void CobraTextJoinsReasonAndWork()
    {
        Assert.Equal("Drucker geht nicht --> Treiber neu", CallRules.CobraText("Drucker\r\ngeht   nicht", " Treiber neu "));
        Assert.Equal("nur Grund", CallRules.CobraText("nur Grund", ""));
        Assert.Equal(string.Empty, CallRules.CobraText(null, null));
    }

    [Fact]
    public void AppendNotesAddsWithSpace()
    {
        Assert.Equal("NE,", CallRules.AppendNotes("", "NE,"));
        Assert.Equal("Notiz NE,", CallRules.AppendNotes("Notiz  ", " NE,"));
        Assert.Equal("Notiz", CallRules.AppendNotes("Notiz", "  "));
    }

    [Fact]
    public void BillableCallNeedsTimesBeforeDone()
    {
        var call = NewCall() with { IsBillable = true };

        Assert.Equal("Start- und Endzeit", CallRules.CheckStatusChange(call, CallStatus.Done).MissingBillingTime);
        Assert.Equal("Endzeit", CallRules.CheckStatusChange(call with { BillingStartUtc = Now }, CallStatus.Done).MissingBillingTime);

        var complete = CallRules.CheckStatusChange(call with { BillingStartUtc = Now, BillingEndUtc = Now.AddMinutes(9) }, CallStatus.Done);
        Assert.False(complete.IsBlocked);
        Assert.True(complete.AskInvoiceMail);

        Assert.False(CallRules.CheckStatusChange(NewCall(), CallStatus.Done).AskInvoiceMail);
        Assert.True(CallRules.CheckStatusChange(NewCall(), CallStatus.CallBackLater).NeedsReminder);
    }

    [Fact]
    public void ApplyStatusSetsReminderAndCompletion()
    {
        var reminder = Now.AddMinutes(30);
        var later = CallRules.ApplyStatus(NewCall(), CallStatus.CallBackLater, reminder, Now);
        Assert.True(later.ReminderActive);
        Assert.Equal(reminder, later.ReminderUtc);
        Assert.Null(later.CompletedUtc);

        var done = CallRules.ApplyStatus(later, CallStatus.Done, null, Now.AddHours(1));
        Assert.False(done.ReminderActive);
        Assert.Null(done.ReminderUtc);
        Assert.Equal(Now.AddHours(1), done.CompletedUtc);

        var stillDone = CallRules.ApplyStatus(done, CallStatus.Done, null, Now.AddHours(2));
        Assert.Equal(Now.AddHours(1), stillDone.CompletedUtc);

        Assert.Null(CallRules.ApplyStatus(done, CallStatus.InProgress, null, Now).CompletedUtc);
        Assert.Throws<ArgumentException>(() => CallRules.ApplyStatus(NewCall(), CallStatus.CallBackTomorrow, null, Now));
    }

    [Fact]
    public void DefaultReminderIsIn30MinutesOrTomorrowAt9()
    {
        var nowLocal = new DateTime(2026, 11, 27, 14, 10, 0);

        Assert.Equal(new DateTime(2026, 11, 27, 14, 40, 0), CallRules.DefaultReminderLocal(NewCall(), false, nowLocal, Berlin));
        Assert.Equal(new DateTime(2026, 11, 28, 9, 0, 0), CallRules.DefaultReminderLocal(NewCall(), true, nowLocal, Berlin));

        var existing = NewCall() with { Status = CallStatus.CallBackLater, ReminderActive = true, ReminderUtc = new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero) };
        Assert.Equal(new DateTime(2026, 11, 27, 16, 0, 0), CallRules.DefaultReminderLocal(existing, false, nowLocal, Berlin));
    }

    [Fact]
    public void RemindersAreDueInOrderAndCanBeHandled()
    {
        var a = NewCall() with { Id = "a", ReminderActive = true, ReminderUtc = Now.AddMinutes(-5), Status = CallStatus.CallBackLater };
        var b = NewCall() with { Id = "b", ReminderActive = true, ReminderUtc = Now.AddMinutes(-10), Status = CallStatus.CallBackLater };
        var future = NewCall() with { Id = "f", ReminderActive = true, ReminderUtc = Now.AddMinutes(5) };

        Assert.Equal("b", CallRules.NextDueReminder(new[] { a, b, future }, Now)!.Id);
        Assert.Null(CallRules.NextDueReminder(new[] { future }, Now));

        var ok = CallRules.AcknowledgeReminder(b, Now);
        Assert.False(ok.ReminderActive);
        Assert.Equal(CallStatus.InProgress, ok.Status);

        var snoozed = CallRules.SnoozeReminder(b, TimeSpan.FromMinutes(10), Now);
        Assert.Equal(Now.AddMinutes(10), snoozed.ReminderUtc);
        Assert.Equal(CallStatus.CallBackLater, snoozed.Status);
    }
}
