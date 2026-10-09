using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QuickRoute.App.Dialogs;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Numeric;
using QuickRoute.BusinessEntities.RouteProperties;
using QuickRoute.Rendering;
using SkiaSharp;

namespace QuickRoute.App
{
  public partial class MainWindow
  {
    private static readonly WaypointAttribute[] ColorCodingAttributes =
    {
      WaypointAttribute.Pace, WaypointAttribute.Speed, WaypointAttribute.HeartRate, WaypointAttribute.Altitude,
      WaypointAttribute.DirectionDeviationToNextLap, WaypointAttribute.MapReadingDuration, WaypointAttribute.Cadence, WaypointAttribute.Power
    };

    private WaypointAttribute SelectedColorCodingAttribute =>
      ColorCodingAttributeBox.SelectedItem is WaypointAttributeString s && s.WaypointAttribute.HasValue ? s.WaypointAttribute.Value : WaypointAttribute.Pace;

    private WaypointAttribute? SelectedSecondaryColorCodingAttribute =>
      SecondaryColorCodingAttributeBox.SelectedItem is WaypointAttributeString s ? s.WaypointAttribute : null;

    private RouteLineSettings CurrentRouteLineSettings => CurrentSession?.Settings.RouteLineSettingsCollection[SelectedColorCodingAttribute];

    private void WireRouteAppearanceEvents()
    {
      ColorCodingAttributeBox.SelectionChanged += (_, _) => OnColorCodingChanged();
      SecondaryColorCodingAttributeBox.SelectionChanged += (_, _) => OnColorCodingChanged();
      ColorRangeStartBox.LostFocus += (_, _) => ApplyColorRangeValue(start: true);
      ColorRangeEndBox.LostFocus += (_, _) => ApplyColorRangeValue(start: false);
      ColorRangeStartBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyColorRangeValue(start: true); };
      ColorRangeEndBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyColorRangeValue(start: false); };
      ColorRangeIntervalButton.Click += async (_, _) => await EditColorRangeIntervalAsync();
      AutoAdjustColorRangeButton.Click += (_, _) =>
      {
        settings.AutoAdjustColorRangeInterval = AutoAdjustColorRangeButton.IsChecked == true;
        if (settings.AutoAdjustColorRangeInterval) PerformColorRangeIntervalAutoAdjustment(SelectedColorCodingAttribute);
      };
      TransparencySlider.ValueChanged += (_, _) =>
      {
        if (UpdatingUi || CurrentRouteLineSettings == null) return;
        CurrentRouteLineSettings.AlphaAdjustment = TransparencySlider.Value / 10;
        AlphaAdjustSelectedLap();
        UpdateColorRangePreview();
      };
      LineWidthBox.ValueChanged += (_, _) =>
      {
        if (UpdatingUi || CurrentRouteLineSettings == null || !LineWidthBox.Value.HasValue) return;
        if (Canvas.MouseTool == Controls.MouseTool.AdjustRoute) CurrentRouteLineSettings.MonochromeWidth = (double)LineWidthBox.Value.Value;
        else CurrentRouteLineSettings.Width = (double)LineWidthBox.Value.Value;
        RouteAppearanceChanged();
      };
      MaskVisibleButton.Click += (_, _) =>
      {
        if (CurrentRouteLineSettings == null) return;
        CurrentRouteLineSettings.MaskVisible = MaskVisibleButton.IsChecked == true;
        RouteAppearanceChanged();
      };
      MaskWidthBox.ValueChanged += (_, _) =>
      {
        if (UpdatingUi || CurrentRouteLineSettings == null || !MaskWidthBox.Value.HasValue) return;
        CurrentRouteLineSettings.MaskWidth = (double)MaskWidthBox.Value.Value;
        RouteAppearanceChanged();
      };
      MaskColorButton.Click += async (_, _) => await EditMaskColorAsync();
      SmoothingIntervalBox.LostFocus += (_, _) => ApplySmoothingInterval();
      SmoothingIntervalBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplySmoothingInterval(); };
      CircleTimeRadiusBox.LostFocus += (_, _) => ApplyCircleTimeRadius();
      CircleTimeRadiusBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyCircleTimeRadius(); };
    }

    private void OnColorCodingChanged()
    {
      if (UpdatingUi) return;
      Canvas.ColorCodingAttribute = SelectedColorCodingAttribute;
      Canvas.SecondaryColorCodingAttribute = SelectedSecondaryColorCodingAttribute;
      UpdateUi();
      CreateLineGraph();
    }

    private void RouteAppearanceChanged()
    {
      documentChanged = true;
      Canvas.InvalidateRoute();
      UpdateColorRangePreview();
      UpdateTitleOnly();
    }

    private void UpdateTitleOnly()
    {
      if (document == null) return;
      Title = (document.FileName != null ? System.IO.Path.GetFileName(document.FileName) : Loc.S("Untitled")) + (documentChanged ? "*" : "") + " - " + Loc.S("QuickRoute");
    }

    private void UpdateRouteAppearanceControls()
    {
      if (ColorCodingAttributeBox.SelectedIndex < 0) ColorCodingAttributeBox.SelectedIndex = 0;
      if (SecondaryColorCodingAttributeBox.SelectedIndex < 0) SecondaryColorCodingAttributeBox.SelectedIndex = 0;
      var rls = CurrentRouteLineSettings;
      if (rls == null) return;
      var converter = GetNumericConverter(SelectedColorCodingAttribute);
      ColorRangeStartBox.Text = converter.ToString(rls.ColorRange.StartValue);
      ColorRangeEndBox.Text = converter.ToString(rls.ColorRange.EndValue);
      TransparencySlider.Value = Math.Round(rls.AlphaAdjustment * 10);
      var adjusting = Canvas.MouseTool == Controls.MouseTool.AdjustRoute;
      LineWidthBox.Value = (decimal)(adjusting ? rls.MonochromeWidth : rls.Width);
      MaskVisibleButton.IsChecked = rls.MaskVisible;
      MaskWidthBox.Value = (decimal)rls.MaskWidth;
      var maskColor = adjusting ? rls.MonochromeColor : rls.MaskColor;
      MaskColorSwatch.Background = new SolidColorBrush(Color.FromArgb(maskColor.A, maskColor.R, maskColor.G, maskColor.B));
      MaskVisibleButton.IsEnabled = MaskWidthBox.IsEnabled = !adjusting;
      AutoAdjustColorRangeButton.IsChecked = settings.AutoAdjustColorRangeInterval;
      var smoothing = document.Settings.DefaultSessionSettings.SmoothingIntervals;
      SmoothingIntervalBox.Text = smoothing.TryGetValue(SelectedColorCodingAttribute, out var interval)
        ? new TimeConverter(TimeConverter.TimeConverterType.ElapsedTime).ToString(interval.Length)
        : "";
      CircleTimeRadiusBox.Text = document.Settings.DefaultSessionSettings.CircleTimeRadius.ToString(CultureInfo.CurrentCulture);
      UpdateColorRangePreview();
    }

    private void UpdateColorRangePreview()
    {
      var rls = CurrentRouteLineSettings;
      if (rls == null) return;
      using var bitmap = new SKBitmap(160, 18, SKColorType.Bgra8888, SKAlphaType.Premul);
      using (var canvas = new SKCanvas(bitmap))
      {
        RouteRenderer.FillCheckerboard(canvas, new SKRectI(0, 0, 160, 18), 6);
        var slider = document.Settings.ColorRangeIntervalSliderSettings[SelectedColorCodingAttribute];
        var span = slider.MaxValue - slider.MinValue;
        var start = span == 0 ? 0 : (slider.MinValue - rls.ColorRange.StartValue) / Math.Max(1e-9, rls.ColorRange.EndValue - rls.ColorRange.StartValue);
        var end = span == 0 ? 1 : (slider.MaxValue - rls.ColorRange.StartValue) / Math.Max(1e-9, rls.ColorRange.EndValue - rls.ColorRange.StartValue);
        RouteRenderer.DrawGradient(canvas, rls.ColorRange.Gradient, new SKRectI(0, 0, 159, 17), start, end, Gradient.Direction.Horizontal, rls.AlphaAdjustment);
      }
      using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
      ColorRangePreview.Source = new Bitmap(data.AsStream());
    }

    private void ApplyColorRangeValue(bool start)
    {
      var rls = CurrentRouteLineSettings;
      if (rls == null || UpdatingUi) return;
      var converter = GetNumericConverter(SelectedColorCodingAttribute);
      var value = converter.ToNumeric((start ? ColorRangeStartBox : ColorRangeEndBox).Text);
      if (value.HasValue)
      {
        var slider = document.Settings.ColorRangeIntervalSliderSettings[SelectedColorCodingAttribute];
        if (start && value.Value < slider.MinValue) UpdateColorRangeInterval(SelectedColorCodingAttribute, value.Value, slider.MaxValue, false);
        if (!start && value.Value > slider.MaxValue) UpdateColorRangeInterval(SelectedColorCodingAttribute, slider.MinValue, value.Value, false);
        var clamped = Math.Max(Math.Min(value.Value, slider.MaxValue), slider.MinValue);
        if (start) rls.ColorRange.StartValue = clamped;
        else rls.ColorRange.EndValue = clamped;
        documentChanged = true;
        Canvas.InvalidateRoute();
        CreateLineGraph();
      }
      UpdateUi();
    }

    private async System.Threading.Tasks.Task EditColorRangeIntervalAsync()
    {
      if (document == null) return;
      var slider = document.Settings.ColorRangeIntervalSliderSettings[SelectedColorCodingAttribute];
      var converter = GetNumericConverter(SelectedColorCodingAttribute);
      var dialog = new FormDialog(Loc.Plain("Tip_ColorRangeInterval"));
      var startBox = dialog.AddRow(Loc.Plain("Tip_ColorRangeStart"), new TextBox { Text = converter.ToString(slider.MinValue) });
      var endBox = dialog.AddRow(Loc.Plain("Tip_ColorRangeEnd"), new TextBox { Text = converter.ToString(slider.MaxValue) });
      dialog.Validate = () =>
      {
        var s = converter.ToNumeric(startBox.Text);
        var e = converter.ToNumeric(endBox.Text);
        return s.HasValue && e.HasValue && e.Value > s.Value;
      };
      if (!await dialog.ShowAsync(this)) return;
      UpdateColorRangeInterval(SelectedColorCodingAttribute, converter.ToNumeric(startBox.Text).Value, converter.ToNumeric(endBox.Text).Value, false);
      documentChanged = true;
      Canvas.InvalidateRoute();
      CreateLineGraph();
      UpdateUi();
    }

    private async System.Threading.Tasks.Task EditMaskColorAsync()
    {
      var rls = CurrentRouteLineSettings;
      if (rls == null) return;
      var adjusting = Canvas.MouseTool == Controls.MouseTool.AdjustRoute;
      var current = adjusting ? rls.MonochromeColor : rls.MaskColor;
      var picker = new ColorView
      {
        Color = Color.FromArgb(current.A, current.R, current.G, current.B),
        IsAlphaEnabled = true,
        IsColorPaletteVisible = false
      };
      var dialog = new FormDialog(Loc.Plain("Tip_MaskColor"));
      dialog.AddRow(null, picker);
      if (!await dialog.ShowAsync(this)) return;
      var c = picker.Color;
      var color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
      if (adjusting) rls.MonochromeColor = color;
      else rls.MaskColor = color;
      RouteAppearanceChanged();
      UpdateUi();
    }

    private void ApplySmoothingInterval()
    {
      if (document == null || UpdatingUi) return;
      var tc = new TimeConverter(TimeConverter.TimeConverterType.ElapsedTime);
      var length = tc.ToNumeric(SmoothingIntervalBox.Text);
      if (length.HasValue)
      {
        var intervals = document.Settings.DefaultSessionSettings.SmoothingIntervals;
        intervals[SelectedColorCodingAttribute] = new Interval(-length.Value / 2, length.Value / 2);
        // speed and pace always use the same smoothing
        if (SelectedColorCodingAttribute == WaypointAttribute.Speed) intervals[WaypointAttribute.Pace] = new Interval(intervals[WaypointAttribute.Speed]);
        if (SelectedColorCodingAttribute == WaypointAttribute.Pace) intervals[WaypointAttribute.Speed] = new Interval(intervals[WaypointAttribute.Pace]);
        foreach (var s in document.Sessions) s.Route.SmoothingIntervals = intervals;
        documentChanged = true;
        Canvas.InvalidateRoute();
        RefreshAll();
      }
      UpdateUi();
    }

    private void ApplyCircleTimeRadius()
    {
      if (document == null || UpdatingUi) return;
      if (double.TryParse(CircleTimeRadiusBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value > 0)
      {
        document.Settings.DefaultSessionSettings.CircleTimeRadius = value;
        documentChanged = true;
        RefreshAll();
      }
      UpdateUi();
    }

    private void InitColorRangeIntervals()
    {
      if (!settings.AutoAdjustColorRangeInterval) return;
      foreach (var wa in new[] { WaypointAttribute.Pace, WaypointAttribute.Speed, WaypointAttribute.HeartRate, WaypointAttribute.Altitude,
                 WaypointAttribute.DirectionDeviationToNextLap, WaypointAttribute.MapReadingDuration })
      {
        PerformColorRangeIntervalAutoAdjustment(wa);
      }
    }

    private void PerformColorRangeIntervalAutoAdjustment(WaypointAttribute wa)
    {
      if (CurrentSession == null) return;
      var values = CurrentSession.Route.GetOrderedValues(wa, GetAutoAdjustColorRangeIntervalPercentages(wa), new TimeSpan(0, 0, 1));
      if (values.Count > 1 && values[0].HasValue && values[1].HasValue)
      {
        var span = values[1].Value - values[0].Value;
        var min = Math.Max(0, values[0].Value - 0.1 * span);
        var max = values[1].Value + 0.1 * span;
        if (wa == WaypointAttribute.Pace) max = Math.Min(30 * 60, max);
        if (wa == WaypointAttribute.DirectionDeviationToNextLap || wa == WaypointAttribute.MapReadingDuration) min = 0;
        UpdateColorRangeInterval(wa, min, max, true);
        Canvas.InvalidateRoute();
        CreateLineGraph();
        if (!UpdatingUi) UpdateUi();
      }
    }

    private static List<double> GetAutoAdjustColorRangeIntervalPercentages(WaypointAttribute attribute)
    {
      switch (attribute)
      {
        case WaypointAttribute.Altitude:
          return new List<double> { 0, 1 };
        case WaypointAttribute.HeartRate:
          return new List<double> { 0.1, 1 };
        case WaypointAttribute.DirectionDeviationToNextLap:
          return new List<double> { 0, 0.9 };
        case WaypointAttribute.MapReadingDuration:
          return new List<double> { 0, 1.1 };
        default:
          return new List<double> { 0.1, 0.9 };
      }
    }

    private void UpdateColorRangeInterval(WaypointAttribute attribute, double minValue, double maxValue, bool setSlidersToMinAndMax)
    {
      // prevent too high paces
      if (attribute == WaypointAttribute.Pace && minValue > 30 * 60) minValue = 30 * 60;
      if (attribute == WaypointAttribute.Pace && maxValue > 30 * 60) maxValue = 30 * 60;
      var sliderSettings = document.Settings.ColorRangeIntervalSliderSettings[attribute];
      sliderSettings.MinValue = minValue;
      sliderSettings.MaxValue = maxValue;
      foreach (var session in document.Sessions)
      {
        var rls = session.Settings.RouteLineSettingsCollection[attribute];
        if (setSlidersToMinAndMax)
        {
          rls.ColorRange.StartValue = sliderSettings.MinValue;
          rls.ColorRange.EndValue = sliderSettings.MaxValue;
        }
        else
        {
          rls.ColorRange.StartValue = Math.Min(Math.Max(rls.ColorRange.StartValue, sliderSettings.MinValue), sliderSettings.MaxValue);
          rls.ColorRange.EndValue = Math.Min(Math.Max(rls.ColorRange.EndValue, sliderSettings.MinValue), sliderSettings.MaxValue);
        }
      }
    }

    private ColorRangeProperties GetCurrentColorRangeProperties()
    {
      var rls = CurrentRouteLineSettings;
      if (rls == null) return null;
      var wa = SelectedColorCodingAttribute;
      return new ColorRangeProperties
      {
        NumericConverter = GetNumericConverter(wa),
        ScaleCreator = CreateScaleCreator(wa, rls.ColorRange.StartValue, rls.ColorRange.EndValue, 10),
        ScaleCaption = new WaypointAttributeString(wa).Name,
        ScaleUnit = new WaypointAttributeString(wa).Unit
      };
    }

    private static ScaleCreatorBase CreateScaleCreator(WaypointAttribute wa, double min, double max, int markers)
    {
      return wa == WaypointAttribute.Pace || wa == WaypointAttribute.MapReadingDuration
        ? new TimeScaleCreator(min, max, markers, false)
        : (ScaleCreatorBase)new DoubleScaleCreator(min, max, markers, false);
    }

    internal static NumericConverter GetNumericConverter(WaypointAttribute wa)
    {
      switch (wa)
      {
        case WaypointAttribute.Pace:
        case WaypointAttribute.MapReadingDuration:
          return new TimeConverter(TimeConverter.TimeConverterType.ElapsedTime);
        case WaypointAttribute.Speed:
          return new NumericConverter();
        case WaypointAttribute.DirectionDeviationToNextLap:
          return new NumericConverter { NoOfDecimals = 0 };
        default:
          return new IntConverter();
      }
    }
  }
}
