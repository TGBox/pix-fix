using PixFix.Core.Common;

namespace PixFix.Core.Tests;

public sealed class ActionLabelTests
{
    [Theory]
    [InlineData("_Anrufen", "Anrufen")]
    [InlineData("Auf_legen", "Auflegen")]
    [InlineData("Datei__name", "Datei_name")]
    [InlineData("Löschen", "Löschen")]
    [InlineData("  + _Neu ", "+ Neu")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void PlainTextRemovesAccessKeyMarkers(string? label, string expected)
    {
        Assert.Equal(expected, ActionLabel.PlainText(label));
    }

    [Theory]
    [InlineData("_Anrufen", 'A')]
    [InlineData("Auf_legen", 'L')]
    [InlineData("Datei__name", null)]
    [InlineData("A__b_c", 'C')]
    [InlineData("Löschen", null)]
    [InlineData("Ende_", null)]
    [InlineData("_ Leer", null)]
    [InlineData(null, null)]
    public void AccessKeyFindsTheMarkedLetter(string? label, char? expected)
    {
        Assert.Equal(expected, ActionLabel.AccessKey(label));
    }

    [Fact]
    public void ToolTipCombinesLabelShortcutAndDescription()
    {
        Assert.Equal(
            "Anrufen (Alt+A)" + Environment.NewLine + "Nummer aus „Rückruf an“ wählen",
            ActionLabel.ToolTip("_Anrufen", "Nummer aus „Rückruf an“ wählen"));
    }

    [Theory]
    [InlineData("Löschen", null, "Löschen")]
    [InlineData("_Papierkorb", "", "Papierkorb (Alt+P)")]
    [InlineData("Löschen", "Löschen", "Löschen")]
    [InlineData(null, "Nur Beschreibung", "Nur Beschreibung")]
    [InlineData(null, null, "")]
    public void ToolTipHandlesMissingParts(string? label, string? description, string expected)
    {
        Assert.Equal(expected, ActionLabel.ToolTip(label, description));
    }
}
