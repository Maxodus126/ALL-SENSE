using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AllSense.App;

/// <summary>
/// Gauge lingkaran responsif: cincin mengikuti ukuran kontrol (80/92/150 dst),
/// tebal stroke & font disesuaikan otomatis.
/// </summary>
public partial class RingGauge : UserControl
{
    public RingGauge()
    {
        InitializeComponent();
        Redraw();
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnChanged));

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RingGauge),
            new PropertyMetadata("%", OnChanged));

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>Angka yang ditampilkan (mis. °C). Kosong = ikut Value (%).</summary>
    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(RingGauge),
            new PropertyMetadata(null, OnChanged));

    public string? DisplayText
    {
        get => (string?)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RingGauge)d).Redraw();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }

    private void Redraw()
    {
        if (ProgressPath is null) return;

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double stroke = Math.Clamp(Math.Min(w, h) * 0.125, 7.0, 15.0);
        double r = Math.Min(w, h) / 2.0 - stroke / 2.0;
        double cx = w / 2.0, cy = h / 2.0;

        TrackEllipse.Width = TrackEllipse.Height = r * 2;
        TrackEllipse.StrokeThickness = stroke;

        ProgressPath.Stroke = Accent ?? Brushes.Transparent;
        ProgressPath.StrokeThickness = stroke;

        double frac = Math.Clamp(Value, 0, 100) / 100.0;
        if (frac >= 0.999)
        {
            ProgressPath.Data = new EllipseGeometry(new Point(cx, cy), r, r);
        }
        else
        {
            double endDeg = -90 + frac * 360.0;
            double rad = endDeg * Math.PI / 180.0;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
            bool large = frac > 0.5;
            var fig = new PathFigure(start,
                new PathSegment[] { new ArcSegment(end, new Size(r, r), 0, large, SweepDirection.Clockwise, true) },
                false);
            ProgressPath.Data = new PathGeometry(new[] { fig });
        }

        UnitText.Text = Unit;
        ValueText.Text = string.IsNullOrEmpty(DisplayText) ? Math.Round(Value).ToString("0") : DisplayText!;
        ValueText.FontSize = Math.Clamp(Math.Min(w, h) * 0.24, 15.0, 32.0);
    }
}
