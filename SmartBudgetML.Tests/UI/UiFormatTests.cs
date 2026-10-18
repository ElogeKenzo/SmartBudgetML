using SmartBudgetML.Components.Shared;
using Xunit;

namespace SmartBudgetML.Tests.UI;

public class UiFormatTests
{
    [Fact]
    public void Money_FormatsPositiveAmount_Correctly()
    {
        var result = UiFormat.Money(2450.50m, "$");
        Assert.Equal("$2,450.50", result);
    }

    [Fact]
    public void Money_FormatsNegativeAmount_WithLeadingMinus()
    {
        var result = UiFormat.Money(-84.20m, "$");
        Assert.Equal("-$84.20", result);
    }

    [Fact]
    public void Money_FormatsCustomCurrency_AndDecimals()
    {
        var result = UiFormat.Money(1500m, "€", 0);
        Assert.Equal("€1,500", result);

        var resultFcfa = UiFormat.Money(50000m, "FCFA ", 0);
        Assert.Equal("FCFA 50,000", resultFcfa);
    }

    [Fact]
    public void Num_AlwaysUsesPeriodDecimalSeparator_ForSvgSafety()
    {
        var result = UiFormat.Num(42.75);
        Assert.Equal("42.75", result);
        Assert.DoesNotContain(",", result);
    }

    [Fact]
    public void Percent_FormatsCorrectly()
    {
        Assert.Equal("75%", UiFormat.Percent(75));
        Assert.Equal("75.5%", UiFormat.Percent(75.5, 1));
    }

    [Theory]
    [InlineData("Lissom Abel", "LA")]
    [InlineData("Abel", "A")]
    [InlineData("John Fitzgerald Kennedy", "JK")]
    [InlineData("", "?")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void Initials_ComputesExpectedInitials(string? name, string expected)
    {
        Assert.Equal(expected, UiFormat.Initials(name));
    }

    [Theory]
    [InlineData("utensils", "🍽️")]
    [InlineData("car", "🚗")]
    [InlineData("home", "🏠")]
    [InlineData("shopping-cart", "🛒")]
    [InlineData("film", "🎬")]
    [InlineData("wallet", "💼")]
    [InlineData("unknown_key", "🏷️")]
    [InlineData(null, "🏷️")]
    public void CategoryGlyph_MapsIconKeysToGlyphs(string? key, string expected)
    {
        Assert.Equal(expected, UiFormat.CategoryGlyph(key));
    }

    [Fact]
    public void TileBackground_AppendsAlphaHexOrProvidesFallback()
    {
        var fallback = UiFormat.TileBackground(null);
        Assert.Equal("rgba(99,102,241,0.15)", fallback);

        var hexWithAlpha = UiFormat.TileBackground("#3B82F6");
        Assert.Equal("#3B82F626", hexWithAlpha);
    }
}
