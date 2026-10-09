using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using QuickRoute.BusinessEntities;
using QuickRoute.Rendering;
using SkiaSharp;

namespace QuickRoute.App.Controls
{
  /// <summary>
  /// Shows a <see cref="LineGraph"/> rendered with the shared Skia renderer and reports hovered x values.
  /// </summary>
  public class LineGraphControl : Control
  {
    private LineGraph graph;

    public LineGraph Graph
    {
      get => graph;
      set
      {
        graph = value;
        InvalidateVisual();
      }
    }

    /// <summary>
    /// Raised with the x axis value under the pointer, or null when leaving the graph area.
    /// </summary>
    public event EventHandler<double?> GraphHover;

    public event EventHandler<double> GraphClick;

    public override void Render(DrawingContext context)
    {
      context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
      if (graph?.Session == null || graph.Points == null) return;
      var size = Bounds.Size;
      using var recorder = new SKPictureRecorder();
      var canvas = recorder.BeginRecording(SKRect.Create(0, 0, (float)size.Width, (float)size.Height));
      LineGraphRenderer.Draw(canvas, graph, SKRect.Create(4, 4, (float)Math.Max(1, size.Width - 8), (float)Math.Max(1, size.Height - 8)));
      var picture = recorder.EndRecording();
      context.Custom(new SkiaDrawOperation(new Rect(size), c =>
      {
        c.DrawPicture(picture);
        picture.Dispose();
      }));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
      base.OnPointerMoved(e);
      GraphHover?.Invoke(this, GetXValue(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
      base.OnPointerExited(e);
      GraphHover?.Invoke(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
      base.OnPointerPressed(e);
      var value = GetXValue(e.GetPosition(this));
      if (value.HasValue) GraphClick?.Invoke(this, value.Value);
    }

    private double? GetXValue(Point p)
    {
      if (graph?.Session == null) return null;
      var r = graph.GraphDrawingRectangle;
      if (r.Width <= 0 || p.X < r.Left || p.X > r.Right || p.Y < r.Top || p.Y > r.Bottom) return null;
      return graph.XToValue((int)p.X);
    }
  }
}
