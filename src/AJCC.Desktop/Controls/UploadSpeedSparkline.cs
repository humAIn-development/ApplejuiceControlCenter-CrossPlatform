using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AJCC.Desktop.Controls;

public sealed class UploadSpeedSparkline : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 27, 35));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromRgb(58, 78, 92)), 1.0);
    private static readonly IPen BaselinePen = new Pen(new SolidColorBrush(Color.FromRgb(84, 105, 118)), 1.0);
    private static readonly IPen LinePen = new Pen(new SolidColorBrush(Color.FromRgb(129, 212, 250)), 1.6);
    private static readonly IBrush PointBrush = new SolidColorBrush(Color.FromRgb(179, 229, 252));

    public static readonly StyledProperty<IReadOnlyList<long>?> ValuesProperty =
        AvaloniaProperty.Register<UploadSpeedSparkline, IReadOnlyList<long>?>(nameof(Values));

    public static readonly StyledProperty<long> CurrentSpeedProperty =
        AvaloniaProperty.Register<UploadSpeedSparkline, long>(nameof(CurrentSpeed));

    static UploadSpeedSparkline()
    {
        AffectsRender<UploadSpeedSparkline>(ValuesProperty);
        AffectsRender<UploadSpeedSparkline>(CurrentSpeedProperty);
    }

    public IReadOnlyList<long>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public long CurrentSpeed
    {
        get => GetValue(CurrentSpeedProperty);
        set => SetValue(CurrentSpeedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width)
            ? 120.0
            : Math.Max(60.0, availableSize.Width);
        return new Size(width, 18.0);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 2 || height <= 2)
            return;

        Rect bounds = new(0.5, 0.5, Math.Max(0, width - 1.0), Math.Max(0, height - 1.0));
        context.DrawRectangle(BackgroundBrush, BorderPen, bounds, 3.0, 3.0);

        List<long> values = new();
        if (Values is not null)
        {
            foreach (long value in Values)
                values.Add(Math.Max(0L, value));
        }

        if (values.Count == 0)
            values.Add(Math.Max(0L, CurrentSpeed));

        if (values.Count == 1)
            values.Insert(0, values[0]);

        long max = Math.Max(MaxValue(values), Math.Max(0L, CurrentSpeed));
        double padding = 3.0;
        double graphWidth = Math.Max(1.0, width - padding * 2.0);
        double graphHeight = Math.Max(1.0, height - padding * 2.0);
        double baselineY = padding + graphHeight;

        context.DrawLine(BaselinePen, new Point(padding, baselineY), new Point(width - padding, baselineY));

        if (max <= 0)
            return;

        List<Point> points = new(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            double x = padding + graphWidth * index / (values.Count - 1);
            double ratio = Math.Clamp((double)values[index] / max, 0.0, 1.0);
            double y = padding + (1.0 - ratio) * graphHeight;
            points.Add(new Point(x, y));
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], isFilled: false);

            if (points.Count == 2)
            {
                geometryContext.LineTo(points[1]);
            }
            else
            {
                for (int index = 1; index < points.Count; index++)
                {
                    Point previous = points[index - 1];
                    Point current = points[index];
                    double controlX = previous.X + (current.X - previous.X) * 0.5;
                    geometryContext.CubicBezierTo(
                        new Point(controlX, previous.Y),
                        new Point(controlX, current.Y),
                        current);
                }
            }

            geometryContext.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, LinePen, geometry);

        Point lastPoint = points[^1];
        context.DrawEllipse(PointBrush, null, lastPoint, 2.2, 2.2);
    }

    private static long MaxValue(IReadOnlyList<long> values)
    {
        long max = 0;
        for (int index = 0; index < values.Count; index++)
            max = Math.Max(max, values[index]);
        return max;
    }
}
