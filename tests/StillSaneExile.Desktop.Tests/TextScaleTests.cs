using System.Windows;
using System.Windows.Media;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Tests;

public class TextScaleTests
{
    private static readonly Size MainMinimum = new(1260, 640);

    [Theory]
    [InlineData(null, 1.0)] // never set: Windows writes nothing until the slider is first moved
    [InlineData(100, 1.0)]
    [InlineData(125, 1.25)]
    [InlineData(225, 2.25)]
    [InlineData(300, 2.25)]
    [InlineData(50, 1.0)]
    [InlineData("150", 1.0)] // not a DWORD
    public void The_registry_value_reads_as_a_clamped_factor(object? value, double factor) =>
        Assert.Equal(factor, TextScale.FromRegistry(value));

    [Theory]
    [InlineData(1.5, 2160, 1500, 1.5)] // room to spare
    [InlineData(1.5, 1920, 1032, 1.5)] // 1080p at 100 % display scaling
    [InlineData(2.0, 1920, 1032, 1.52)] // the minimum 1260 wide fits 1.52 times
    [InlineData(1.25, 1536, 824, 1.21)] // 1080p at 125 % display scaling
    [InlineData(1.5, 1280, 672, 1.01)] // 1080p at 150 %: nearly no room left
    [InlineData(1.5, 1000, 600, 1.0)] // smaller than the minimum: never below 1
    [InlineData(1.0, 3840, 2100, 1.0)]
    public void The_factor_stops_where_the_window_would_no_longer_fit(double wanted, double width, double height, double fitted) =>
        Assert.Equal(fitted, TextScale.Fit(wanted, new Size(width, height), MainMinimum), 2);

    [Fact]
    public void A_fixed_factor_stays_in_the_Windows_range()
    {
        using var high = TextScale.Fixed(3);
        using var low = TextScale.Fixed(0.5);

        Assert.Equal(2.25, high.Factor);
        Assert.Equal(1.0, low.Factor);
    }

    [Fact]
    public void The_main_window_scales_its_content_and_its_minimum_size() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = new MainWindow { DataContext = app.Shell };
        try
        {
            window.UseTextScale(TextScale.Fixed(2), screen: new Size(1920, 1032));

            Assert.Equal(1.52, window.ContentScale, 2);
            var scale = Assert.IsType<ScaleTransform>(((FrameworkElement)window.Content).LayoutTransform);
            Assert.Equal(1.52, scale.ScaleX, 2);
            Assert.Equal(1260 * 1.52, window.MinWidth, 1);
            Assert.Equal(640 * 1.52, window.MinHeight, 1);
            Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode((FrameworkElement)window.Content));

            window.UseTextScale(TextScale.Fixed(1), screen: new Size(1920, 1032));

            Assert.Equal(1, window.ContentScale);
            Assert.True(((FrameworkElement)window.Content).LayoutTransform.Value.IsIdentity);
            Assert.Equal(1260, window.MinWidth);
            Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode((FrameworkElement)window.Content));
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData("150", 1.5)]
    [InlineData(null, 1.0)]
    [InlineData("big", 1.0)]
    public void Snapshots_take_the_text_size_in_percent(string? arg, double factor) =>
        Assert.Equal(factor, SnapshotRenderer.ParseScale(arg));

    [Fact]
    public void Snapshots_take_a_window_size()
    {
        Assert.Equal(new Size(1920, 1032), SnapshotRenderer.ParseSize("1920x1032"));
        Assert.Null(SnapshotRenderer.ParseSize("1920"));
        Assert.Null(SnapshotRenderer.ParseSize(null));
    }
}
