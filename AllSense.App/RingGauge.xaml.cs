using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AllSense.App;

/// <summary>Gauge lingkaran statis (tanpa animasi/glow): track Panel2 + busur aksen + nilai/pusat.</summary>
public partial class RingGauge : UserControl
{
    private const double R = 35;
    private const double CX = 40;
    private const double CY = 40;

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

    private void Redraw()
    {
        if (ProgressPath is null) return;
        double frac = Math.Clamp(Value, 0, 100) / 100.0;
        ProgressPath.Stroke = Accent ?? Brushes.Transparent;
        UnitText.Text = Unit;
        ValueText.Text = string.IsNullOrEmpty(DisplayText) ? Math.Round(Value).ToString("0") : DisplayText!;

        if (frac >= 0.999)
        {
            ProgressPath.Data = new EllipseGeometry(new Point(CX, CY), R, R);
            return;
        }

        double endDeg = -90 + frac * 360.0;
        double rad = endDeg * Math.PI / 180.0;
        var start = new Point(CX, CY - R);
        var end = new Point(CX + R * Math.Cos(rad), CY + R * Math.Sin(rad));
        bool large = frac > 0.5;
        var fig = new PathFigure(start,
            new PathSegment[] { new ArcSegment(end, new Size(R, R), 0, large, SweepDirection.Clockwise, true) },
            false);
        ProgressPath.Data = new PathGeometry(new[] { fig });
    }
}
