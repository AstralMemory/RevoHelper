using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Controls;

public sealed class FanCurveChart : FrameworkElement
{
    private const double PlotPadding = 26;
    private const int AxisDivisions = 5;
    private const double MinimumVisibleTemperature = 40;
    private IReadOnlyList<FanCurvePointViewModel> points = [];
    private INotifyCollectionChanged? observedCollection;
    private FanCurvePointViewModel? draggedPoint;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<FanCurvePointViewModel>),
        typeof(FanCurveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(FanCurveChart),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SurfaceBrushProperty = DependencyProperty.Register(
        nameof(SurfaceBrush), typeof(Brush), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(213, 221, 232)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(226, 232, 240)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisTextBrushProperty = DependencyProperty.Register(
        nameof(AxisTextBrush), typeof(Brush), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(112, 128, 149)), FrameworkPropertyMetadataOptions.AffectsRender));

    public FanCurveChart()
    {
        SetResourceReference(SurfaceBrushProperty, "ChartSurfaceBrush");
        SetResourceReference(BorderBrushProperty, "ChartBorderBrush");
        SetResourceReference(GridBrushProperty, "ChartGridBrush");
        SetResourceReference(AxisTextBrushProperty, "ChartAxisBrush");
    }

    public IEnumerable<FanCurvePointViewModel>? ItemsSource
    {
        get => (IEnumerable<FanCurvePointViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush SurfaceBrush
    {
        get => (Brush)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public Brush BorderBrush
    {
        get => (Brush)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush AxisTextBrush
    {
        get => (Brush)GetValue(AxisTextBrushProperty);
        set => SetValue(AxisTextBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRoundedRectangle(
            SurfaceBrush,
            new Pen(BorderBrush, 1),
            new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)),
            10,
            10);

        if (ActualWidth <= PlotPadding * 2 || ActualHeight <= PlotPadding * 2)
        {
            return;
        }

        var temperatureTicks = FanCurveChartMath.CreateTemperatureTicks(
            points.Select(point => (double)point.UpTemperatureC), AxisDivisions, MinimumVisibleTemperature);
        if (temperatureTicks.Count == 0)
        {
            return;
        }

        DrawGrid(drawingContext, temperatureTicks);
        if (points.Count == 0)
        {
            return;
        }

        var projected = ProjectPoints();
        var accent = AccentBrush ?? Brushes.MediumPurple;
        var fill = accent.Clone();
        fill.Opacity = 0.13;

        var bottom = ActualHeight - PlotPadding;
        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new(projected[0].X, bottom), true, true);
            context.LineTo(projected[0], true, false);
            foreach (var point in projected.Skip(1))
            {
                context.LineTo(point, true, false);
            }

            context.LineTo(new(projected[^1].X, bottom), true, false);
        }

        area.Freeze();
        drawingContext.DrawGeometry(fill, null, area);

        var curve = new StreamGeometry();
        using (var context = curve.Open())
        {
            context.BeginFigure(projected[0], false, false);
            foreach (var point in projected.Skip(1))
            {
                context.LineTo(point, true, false);
            }
        }

        curve.Freeze();
        var curvePen = new Pen(accent, 2);
        drawingContext.DrawGeometry(null, curvePen, curve);

        for (var index = 0; index < projected.Count; index++)
        {
            var fillBrush = ReferenceEquals(points[index], draggedPoint) ? accent : SurfaceBrush;
            drawingContext.DrawEllipse(fillBrush, new Pen(accent, 1.8), projected[index], 4.2, 4.2);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (points.Count == 0)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var projected = ProjectPoints();
        var nearestIndex = FanCurveChartMath.NearestPointIndex(projected, pointer);
        if (nearestIndex < 0)
        {
            return;
        }

        draggedPoint = points[nearestIndex];
        CaptureMouse();
        Cursor = Cursors.SizeNS;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (draggedPoint is null || e.LeftButton is not MouseButtonState.Pressed)
        {
            return;
        }

        var index = Enumerable.Range(0, points.Count)
            .FirstOrDefault(candidate => ReferenceEquals(points[candidate], draggedPoint), -1);
        if (index < 0)
        {
            return;
        }

        var proposed = FanCurveChartMath.DutyFromY(e.GetPosition(this).Y, ActualHeight, PlotPadding);
        int? previous = index > 0 ? points[index - 1].DutyPercent : null;
        int? next = index + 1 < points.Count ? points[index + 1].DutyPercent : null;
        draggedPoint.DutyPercent = FanCurveChartMath.ClampDuty(proposed, previous, next);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (draggedPoint is null)
        {
            return;
        }

        draggedPoint = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        e.Handled = true;
        InvalidateVisual();
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((FanCurveChart)dependencyObject).ObserveItems(args.NewValue as IEnumerable<FanCurvePointViewModel>);

    private void ObserveItems(IEnumerable<FanCurvePointViewModel>? source)
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= CollectionChanged;
        }

        foreach (var point in points)
        {
            point.PropertyChanged -= PointChanged;
        }

        points = source?.ToArray() ?? [];
        observedCollection = source as INotifyCollectionChanged;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += CollectionChanged;
        }

        foreach (var point in points)
        {
            point.PropertyChanged += PointChanged;
        }

        InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => ObserveItems(ItemsSource);

    private void PointChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FanCurvePointViewModel.DutyPercent))
        {
            InvalidateVisual();
        }
    }

    private IReadOnlyList<Point> ProjectPoints()
    {
        var temperatureTicks = FanCurveChartMath.CreateTemperatureTicks(
            points.Select(point => (double)point.UpTemperatureC), AxisDivisions, MinimumVisibleTemperature);
        if (temperatureTicks.Count == 0)
        {
            return [];
        }

        var minimumTemperature = temperatureTicks[0];
        var maximumTemperature = temperatureTicks[^1];
        return points.Select(point => FanCurveChartMath.Project(
                point.UpTemperatureC,
                point.DutyPercent,
                minimumTemperature,
                maximumTemperature,
                ActualWidth,
                ActualHeight,
                PlotPadding))
            .ToArray();
    }

    private void DrawGrid(DrawingContext drawingContext, IReadOnlyList<double> temperatureTicks)
    {
        var gridPen = new Pen(GridBrush, 0.8);
        var left = PlotPadding;
        var right = ActualWidth - PlotPadding;
        var top = PlotPadding;
        var bottom = ActualHeight - PlotPadding;
        for (var index = 0; index < temperatureTicks.Count; index++)
        {
            var x = left + ((right - left) * index / (temperatureTicks.Count - 1d));
            var y = top + ((bottom - top) * index / (temperatureTicks.Count - 1d));
            drawingContext.DrawLine(gridPen, new(x, top), new(x, bottom));
            drawingContext.DrawLine(gridPen, new(left, y), new(right, y));

            var temperatureText = FormatAxisText(temperatureTicks[index], "℃");
            drawingContext.DrawText(
                temperatureText,
                new Point(Math.Clamp(x - (temperatureText.Width / 2), 0, Math.Max(0, ActualWidth - temperatureText.Width)), bottom + 3));

            var dutyText = FormatAxisText(100 - (index * 20), "%");
            drawingContext.DrawText(dutyText, new Point(2, Math.Clamp(y - (dutyText.Height / 2), 0, ActualHeight - dutyText.Height)));
        }
    }

    private FormattedText FormatAxisText(double value, string suffix) => new(
        $"{Math.Round(value):0}{suffix}",
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI"),
        UiFontExtension.Grow(7.5),
        AxisTextBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
