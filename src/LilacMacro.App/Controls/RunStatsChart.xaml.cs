using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LilacMacro.App.Controls;

public sealed record RunStatsPoint(TimeSpan Elapsed, bool IsWin);

public partial class RunStatsChart : UserControl
{
    private IReadOnlyList<RunStatsPoint> _points = [];

    public RunStatsChart() => InitializeComponent();

    public void SetPoints(IReadOnlyList<RunStatsPoint> points)
    {
        _points = points;
        WinsText.Text = $"{points.Count(point => point.IsWin)} W";
        LossesText.Text = $"{points.Count(point => !point.IsWin)} L";
        RenderChart();
    }

    private void ChartCanvas_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs) => RenderChart();

    private void RenderChart()
    {
        ChartCanvas.Children.Clear();
        EmptyText.Visibility = _points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        double width = ChartCanvas.ActualWidth;
        double height = ChartCanvas.ActualHeight;
        if (width < 24 || height < 24) return;

        const double inset = 8;
        AddGuide(inset, height - inset, width - inset, height - inset);
        AddGuide(inset, height / 2, width - inset, height / 2);
        AddGuide(inset, inset, width - inset, inset);
        if (_points.Count == 0) return;

        const int bucketCount = 10;
        double maximumSeconds = Math.Max(1, _points.Max(point => point.Elapsed.TotalSeconds));
        double baseline = height - inset;
        int[] winsByBucket = new int[bucketCount];
        int[] lossesByBucket = new int[bucketCount];
        foreach (RunStatsPoint point in _points)
        {
            int bucket = Math.Min(
                bucketCount - 1,
                (int)Math.Floor((point.Elapsed.TotalSeconds / maximumSeconds) * bucketCount));
            if (point.IsWin) winsByBucket[bucket]++;
            else lossesByBucket[bucket]++;
        }

        int maximumRuns = Enumerable.Range(0, bucketCount)
            .Max(index => winsByBucket[index] + lossesByBucket[index]);
        List<Point> totalPoints = [];
        List<Point> lossPoints = [];
        for (int index = 0; index < bucketCount; index++)
        {
            double xRatio = index / (double)(bucketCount - 1);
            double x = inset + ((width - (inset * 2)) * xRatio);
            double totalRatio = (winsByBucket[index] + lossesByBucket[index]) / (double)maximumRuns;
            double lossRatio = lossesByBucket[index] / (double)maximumRuns;
            double totalY = baseline - ((height - (inset * 2)) * totalRatio);
            double lossY = baseline - ((height - (inset * 2)) * lossRatio);
            totalPoints.Add(new Point(x, totalY));
            lossPoints.Add(new Point(x, lossY));
        }

        AddArea(
            [new Point(lossPoints[0].X, baseline), .. lossPoints, new Point(lossPoints[^1].X, baseline)],
            "DangerBrush");
        AddArea([.. lossPoints, .. totalPoints.AsEnumerable().Reverse()], "SuccessBrush");
        AddLine(lossPoints, "DangerBrush");
        AddLine(totalPoints, "SuccessBrush");
        AddPoint(lossPoints[^1].X, lossPoints[^1].Y, "DangerBrush");
        AddPoint(totalPoints[^1].X, totalPoints[^1].Y, "SuccessBrush");
    }

    private void AddGuide(double x1, double y1, double x2, double y2)
    {
        Line line = new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1, Opacity = 0.55 };
        line.SetResourceReference(Shape.StrokeProperty, "ChromeBorderBrush");
        ChartCanvas.Children.Add(line);
    }

    private void AddArea(IEnumerable<Point> points, string brush)
    {
        Polygon area = new() { Points = new PointCollection(points), Opacity = 0.18 };
        area.SetResourceReference(Shape.FillProperty, brush);
        ChartCanvas.Children.Add(area);
    }

    private void AddLine(IEnumerable<Point> points, string brush)
    {
        Polyline line = new()
        {
            Points = new PointCollection(points),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        line.SetResourceReference(Shape.StrokeProperty, brush);
        ChartCanvas.Children.Add(line);
    }

    private void AddPoint(double x, double y, string brush)
    {
        Ellipse point = new() { Width = 7, Height = 7 };
        point.SetResourceReference(Shape.FillProperty, brush);
        Canvas.SetLeft(point, x - 3.5);
        Canvas.SetTop(point, y - 3.5);
        ChartCanvas.Children.Add(point);
    }
}
