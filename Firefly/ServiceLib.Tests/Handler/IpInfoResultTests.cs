using Xunit;

namespace ServiceLib.Tests.Handler;

public class IpInfoResultTests
{
    [Fact]
    public void ToString_ShouldShowCountryAndRegionWithoutRawIp()
    {
        var result = new IpInfoResult("United States", "California", "US");

        var display = result.ToString();

        Assert.Contains("United States", display);
        Assert.Contains("California", display);
        Assert.DoesNotContain("104.28.123.123", display);
    }
}
