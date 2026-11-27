using PixFix.Core.Calls;

namespace PixFix.Core.Tests;

public class CallStatusTests
{
    [Theory]
    [InlineData("Noch zu bearbeiten", CallStatus.Open)]
    [InlineData("In Bearbeitung", CallStatus.InProgress)]
    [InlineData("Später erneut anrufen", CallStatus.CallBackLater)]
    [InlineData("Morgen erneut anrufen", CallStatus.CallBackTomorrow)]
    [InlineData("Erledigt", CallStatus.Done)]
    [InlineData(" erledigt ", CallStatus.Done)]
    public void ParseLegacyReadsPixFix69Values(string legacy, CallStatus expected)
    {
        Assert.Equal(expected, CallStatusText.ParseLegacy(legacy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unbekannt")]
    public void ParseLegacyFallsBackToOpen(string? legacy)
    {
        Assert.Equal(CallStatus.Open, CallStatusText.ParseLegacy(legacy));
    }

    [Fact]
    public void DisplayNameRoundTrips()
    {
        foreach (var status in Enum.GetValues<CallStatus>())
        {
            Assert.Equal(status, CallStatusText.ParseLegacy(status.ToDisplayName()));
        }
    }

    [Fact]
    public void OnlyCallBackStatusesRequireReminder()
    {
        Assert.True(CallStatus.CallBackLater.RequiresReminder());
        Assert.True(CallStatus.CallBackTomorrow.RequiresReminder());
        Assert.False(CallStatus.Done.RequiresReminder());
        Assert.False(CallStatus.InProgress.RequiresReminder());
    }
}
