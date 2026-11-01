using System.Globalization;
using ElyProxy.Models;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class SubscriptionUserInfoTests
{
    [Fact]
    public void Parse_ReadsTrafficAndExpiry()
    {
        var info = SubscriptionUserInfo.Parse("upload=1073741824; download=2147483648; total=10737418240; expire=1767225600");

        Assert.NotNull(info);
        Assert.Equal(1073741824, info.UploadBytes);
        Assert.Equal(2147483648, info.DownloadBytes);
        Assert.Equal(10737418240, info.TotalBytes);
        Assert.NotNull(info.ExpiresAt);
        Assert.Equal(3221225472, info.UsedBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("upload=0; download=0")]
    public void Parse_ReturnsNullForEmptyOrUnknownHeaders(string header)
    {
        Assert.Null(SubscriptionUserInfo.Parse(header));
    }

    [Fact]
    public void Subscription_TrafficSummary_FormatsVolumeAndExpiry()
    {
        var subscription = new Subscription
        {
            UploadBytes = 1073741824,
            DownloadBytes = 1073741824,
            TotalBytes = 10737418240,
            ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(3))
        };

        Assert.True(subscription.HasTrafficInfo);
        Assert.Contains("2 ГБ из 10 ГБ", subscription.TrafficSummary);
        Assert.Contains("01.02.2026", subscription.TrafficSummary);
    }

    [Fact]
    public void Subscription_TrafficSummary_IsEmptyWithoutInfo()
    {
        var subscription = new Subscription();

        Assert.False(subscription.HasTrafficInfo);
        Assert.Equal(string.Empty, subscription.TrafficSummary);
    }

    [Fact]
    public void Subscription_Dates_AreCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = new CultureInfo("th-TH");
            Assert.IsNotType<GregorianCalendar>(culture.DateTimeFormat.Calendar);
            CultureInfo.CurrentCulture = culture;

            var subscription = new Subscription
            {
                DownloadBytes = 2L * 1024 * 1024 * 1024,
                TotalBytes = 10L * 1024 * 1024 * 1024,
                ExpiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                LastUpdated = new DateTime(2026, 2, 1, 13, 5, 0)
            };

            Assert.Contains("01.02.2026", subscription.TrafficSummary);
            Assert.Contains("01.02.2026 13:05", subscription.LastUpdateStatus);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TrafficFormatter_FormatsBytesAndSpeed()
    {
        Assert.Equal("0 Б", TrafficFormatter.FormatBytes(0));
        Assert.Equal("512 Б", TrafficFormatter.FormatBytes(512));
        Assert.Equal("1.5 КБ", TrafficFormatter.FormatBytes(1536));
        Assert.Equal("2 МБ/с", TrafficFormatter.FormatSpeed(2 * 1024 * 1024));
    }
}
