using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AJCC.Desktop.Controls;

/// <summary>
/// Adaptive 20-segment AppleJuice progress indicator.
/// The apples scale with the available cell width so the visual remains usable
/// in both compact and wide transfer lists.
/// </summary>
public sealed class AppleProgressBar : Control
{
    private const int SegmentCount = 20;

    private static readonly IBrush EmptyBodyBrush = new SolidColorBrush(Color.FromRgb(91, 101, 117));
    private static readonly IPen EmptyBodyPen = new Pen(new SolidColorBrush(Color.FromRgb(91, 101, 117)), 0.45);
    private static readonly IPen EmptyStemPen = new Pen(new SolidColorBrush(Color.FromRgb(107, 111, 118)), 0.9);
    private static readonly IBrush EmptyLeafBrush = new SolidColorBrush(Color.FromRgb(104, 121, 102));

    private static readonly IBrush FilledBodyBrush = new SolidColorBrush(Color.FromRgb(212, 175, 55));
    private static readonly IPen FilledBodyPen = new Pen(new SolidColorBrush(Color.FromRgb(243, 216, 120)), 0.45);
    private static readonly IPen FilledStemPen = new Pen(new SolidColorBrush(Color.FromRgb(123, 91, 42)), 0.9);
    private static readonly IBrush FilledLeafBrush = new SolidColorBrush(Color.FromRgb(127, 177, 107));

    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<AppleProgressBar, double>(nameof(Percent));

    static AppleProgressBar()
        => AffectsRender<AppleProgressBar>(PercentProperty);

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width)
            ? 220.0
            : Math.Max(0.0, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height)
            ? 14.0
            : Math.Max(12.0, availableSize.Height);
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0.0 || height <= 0.0)
            return;

        double safePercent = double.IsNaN(Percent) || double.IsInfinity(Percent)
            ? 0.0
            : Math.Clamp(Percent, 0.0, 100.0);
        int filledCount = (int)Math.Round(
            safePercent / 100.0 * SegmentCount,
            MidpointRounding.AwayFromZero);
        filledCount = Math.Clamp(filledCount, 0, SegmentCount);

        double slotWidth = width / SegmentCount;
        double appleWidth = Math.Max(1.0, Math.Min(slotWidth * 0.78, 9.0));
        double appleHeight = Math.Max(1.0, Math.Min(height * 0.82, 11.0));
        double bodyRadiusX = appleWidth * 0.42;
        double bodyRadiusY = appleHeight * 0.36;

        for (int index = 0; index < SegmentCount; index++)
        {
            bool filled = index < filledCount;
            double centerX = index * slotWidth + slotWidth * 0.5;
            double bodyCenterY = height * 0.61;
            double stemTopY = Math.Max(0.5, bodyCenterY - bodyRadiusY - appleHeight * 0.18);
            double stemBottomY = bodyCenterY - bodyRadiusY * 0.72;

            IBrush bodyBrush = filled ? FilledBodyBrush : EmptyBodyBrush;
            IPen bodyPen = filled ? FilledBodyPen : EmptyBodyPen;
            IPen stemPen = filled ? FilledStemPen : EmptyStemPen;
            IBrush leafBrush = filled ? FilledLeafBrush : EmptyLeafBrush;

            context.DrawEllipse(
                bodyBrush,
                bodyPen,
                new Point(centerX, bodyCenterY),
                bodyRadiusX,
                bodyRadiusY);
            context.DrawLine(
                stemPen,
                new Point(centerX, stemTopY),
                new Point(centerX, stemBottomY));
            context.DrawEllipse(
                leafBrush,
                null,
                new Point(centerX + appleWidth * 0.22, stemTopY + appleHeight * 0.04),
                Math.Max(0.6, appleWidth * 0.17),
                Math.Max(0.35, appleHeight * 0.08));
        }
    }
}
