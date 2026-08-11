using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.App.Views;

namespace LilacMacro.Tests;

public sealed class PlacementTimelineSnapshotTests
{
    [Fact]
    public void CaptureRendersAChildArrangedBelowTheParentOrigin()
    {
        Exception? failure = null;
        bool containsOpaquePixels = false;
        Thread thread = new(() =>
        {
            try
            {
                Canvas owner = new() { Width = 200, Height = 300 };
                Border lowerRow = new()
                {
                    Width = 160,
                    Height = 40,
                    Background = Brushes.DeepPink,
                };
                Canvas.SetLeft(lowerRow, 20);
                Canvas.SetTop(lowerRow, 220);
                owner.Children.Add(lowerRow);
                owner.Measure(new Size(owner.Width, owner.Height));
                owner.Arrange(new Rect(0, 0, owner.Width, owner.Height));
                owner.UpdateLayout();

                ImageBrush brush = PlacementTimelineSnapshot.Capture(lowerRow);
                BitmapSource bitmap = Assert.IsAssignableFrom<BitmapSource>(brush.ImageSource);
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                containsOpaquePixels = pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(containsOpaquePixels);
    }
}
