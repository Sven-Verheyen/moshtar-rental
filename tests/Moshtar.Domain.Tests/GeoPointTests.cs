using Moshtar.Domain.Common;

namespace Moshtar.Domain.Tests;

public class GeoPointTests
{
    [Theory]
    [InlineData("51.1702, 5.1703", 51.1702, 5.1703)]
    [InlineData(" 51.1702 5.1703 ", 51.1702, 5.1703)]
    [InlineData("51,1702; 5,1703", 51.1702, 5.1703)]
    [InlineData("-33.86,151.21", -33.86, 151.21)]
    public void Coordinates_as_copied_from_a_map_are_a_point(string input, double latitude, double longitude) =>
        Assert.Equal(new GeoPoint(latitude, longitude), GeoPoint.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("51.17")]
    [InlineData("Kerkstraat 1")]
    [InlineData("91, 5")]
    [InlineData("51, 181")]
    [InlineData("51,1702, 5,1703")]
    public void Anything_else_is_not_a_point(string? input) =>
        Assert.Null(GeoPoint.Parse(input));

    [Fact]
    public void A_point_is_written_with_a_decimal_point_for_waze_and_the_map() =>
        Assert.Equal("51.170212,5.1703", new GeoPoint(51.1702123, 5.1703).ToString());
}
