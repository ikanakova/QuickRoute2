using System;
using QuickRoute.BusinessEntities;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Lays out and draws a <see cref="LineGraph"/> with axes, grid, gradient filling and lap lines.
  /// </summary>
  public static class LineGraphRenderer
  {
    public static void Draw(SKCanvas canvas, LineGraph graph, SKRect drawingRectangle)
    {
      if (graph.YAxisNumericConverter == null || graph.YAxisScaleCreator == null || graph.Session == null) return;
      graph.ConfigureXAxis();

      using var font = new SKPaint { Typeface = SkiaUtil.DefaultTypeface, TextSize = 11f, IsAntialias = true, Color = SKColors.Black };
      var xAxisCaptionSize = font.MeasureTextSize(graph.XAxisCaption);
      var yAxisCaptionSize = font.MeasureTextSize(graph.YAxisCaption);

      var maxXAxisLabelSize = SKSize.Empty;
      for (var i = 0; i < graph.XAxisScaleCreator.NoOfMarkers; i++)
      {
        var size = font.MeasureTextSize(graph.XAxisNumericConverter.ToString(graph.XAxisScaleCreator.MarkerValue(i)));
        maxXAxisLabelSize = new SKSize(Math.Max(maxXAxisLabelSize.Width, size.Width), Math.Max(maxXAxisLabelSize.Height, size.Height));
      }
      var maxYAxisLabelSize = SKSize.Empty;
      for (var i = 0; i < graph.YAxisScaleCreator.NoOfMarkers; i++)
      {
        var size = font.MeasureTextSize(graph.YAxisNumericConverter.ToString(graph.YAxisScaleCreator.MarkerValue(i)));
        maxYAxisLabelSize = new SKSize(Math.Max(maxYAxisLabelSize.Width, size.Width), Math.Max(maxYAxisLabelSize.Height, size.Height));
      }

      var leftMargin = Math.Max(1.5f * yAxisCaptionSize.Height + maxYAxisLabelSize.Width, maxXAxisLabelSize.Width / 2);
      var width = (int)(drawingRectangle.Width - leftMargin - maxXAxisLabelSize.Width / 2 - 1);
      var height = (int)(drawingRectangle.Height - maxYAxisLabelSize.Height / 2 - maxXAxisLabelSize.Height - xAxisCaptionSize.Height - 1);
      graph.GraphDrawingRectangle = new System.Drawing.Rectangle(
        (int)(drawingRectangle.Left + leftMargin),
        (int)(drawingRectangle.Top + maxYAxisLabelSize.Height / 2),
        Math.Max(width, 1),
        Math.Max(height, 1));
      var r = graph.GraphDrawingRectangle;

      using (var background = SkiaUtil.CreateFillPaint(SKColors.White, false))
      {
        canvas.DrawRect(SKRect.Create(r.X, r.Y, r.Width, r.Height), background);
      }

      using (var grid = SkiaUtil.CreateStrokePaint(new SKColor(128, 128, 128, 128), 1, false))
      {
        for (var i = 0; i < graph.XAxisScaleCreator.NoOfMarkers; i++)
        {
          var value = graph.XAxisScaleCreator.MarkerValue(i);
          var x = (int)Math.Round(graph.ValueToX(value));
          var label = graph.XAxisNumericConverter.ToString(value);
          canvas.DrawTextTopLeft(label, x - font.MeasureText(label) / 2, r.Bottom, font);
          canvas.DrawLine(x + 0.5f, r.Top, x + 0.5f, r.Bottom, grid);
        }
        for (var i = 0; i < graph.YAxisScaleCreator.NoOfMarkers; i++)
        {
          var value = graph.YAxisScaleCreator.MarkerValue(i);
          var y = (int)Math.Round(graph.ValueToY(value));
          var label = graph.YAxisNumericConverter.ToString(value);
          canvas.DrawTextTopLeft(label, r.Left - font.MeasureText(label), y - maxYAxisLabelSize.Height / 2, font);
          canvas.DrawLine(r.Left, y + 0.5f, r.Right, y + 0.5f, grid);
        }
      }

      // axis captions
      canvas.DrawTextTopLeft(graph.XAxisCaption, r.Left + (r.Width - xAxisCaptionSize.Width) / 2, r.Bottom + xAxisCaptionSize.Height, font);
      var yCaptionX = r.Left - 3 * yAxisCaptionSize.Height;
      var yCaptionY = r.Bottom - (r.Height - yAxisCaptionSize.Width) / 2;
      canvas.Save();
      canvas.RotateDegrees(-90, yCaptionX, yCaptionY);
      canvas.DrawTextTopLeft(graph.YAxisCaption, yCaptionX, yCaptionY, font);
      canvas.Restore();

      // gradient filling below the line
      var rls = graph.Session.Settings.RouteLineSettingsCollection[graph.YAxisAttribute];
      var polygons = new System.Collections.Generic.List<SKPath>();
      if (graph.Points != null)
      {
        foreach (var points in graph.Points)
        {
          if (points.Count == 0) continue;
          var polygon = new SKPath();
          polygon.MoveTo(graph.ValueToX(points[0].X), r.Bottom);
          foreach (var p in points) polygon.LineTo(graph.ValueToX(p.X), graph.ValueToY(p.Y));
          polygon.LineTo(graph.ValueToX(points[points.Count - 1].X), r.Bottom);
          polygons.Add(polygon);

          var interval = rls.ColorRange.EndValue - rls.ColorRange.StartValue;
          if (interval == 0) interval = 0.00001;
          canvas.Save();
          canvas.ClipPath(polygon, SKClipOperation.Intersect, true);
          RouteRenderer.DrawGradient(canvas, rls.ColorRange.Gradient, SKRectI.Create(r.X, r.Y, r.Width, r.Height),
            (graph.YAxisMinValue - rls.ColorRange.StartValue) / interval,
            1 + (graph.YAxisMaxValue - rls.ColorRange.EndValue) / interval,
            Gradient.Direction.ReverseVertical);
          canvas.Restore();
        }
      }

      canvas.Save();
      canvas.ClipRect(SKRect.Create(r.X, r.Y, r.Width, r.Height));
      using (var lapPaint = SkiaUtil.CreateStrokePaint(new SKColor(0, 0, 255, 128), 1.5f))
      {
        foreach (var lap in graph.Session.Laps)
        {
          var x = (int)Math.Round(graph.ValueToX(graph.GetLapXValue(lap)));
          canvas.DrawLine(x, r.Top, x, r.Bottom, lapPaint);
        }
      }
      using (var linePaint = SkiaUtil.CreateStrokePaint(new SKColor(0, 0, 0, 192), 1.5f))
      {
        foreach (var polygon in polygons)
        {
          canvas.DrawPath(polygon, linePaint);
          polygon.Dispose();
        }
      }
      canvas.Restore();
      using (var border = SkiaUtil.CreateStrokePaint(SKColors.Black, 1, false))
      {
        canvas.DrawRect(SKRect.Create(r.X + 0.5f, r.Y + 0.5f, r.Width, r.Height), border);
      }
    }
  }
}
