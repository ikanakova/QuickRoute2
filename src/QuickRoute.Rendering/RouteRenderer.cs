using System.Collections.Generic;
using QuickRoute.BusinessEntities;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Draws routes, markers and gradients on a Skia canvas. Used both for screen output and for image export.
  /// </summary>
  public static class RouteRenderer
  {
    public static void DrawPrimitives(SKCanvas canvas, IEnumerable<RouteDrawingPrimitive> primitives)
    {
      using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
      using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Butt };
      foreach (var primitive in primitives)
      {
        if (primitive.Path != null)
        {
          using var path = ToSKPath(primitive.Path);
          fill.Color = primitive.Color.ToSKColor();
          canvas.DrawPath(path, fill);
        }
        else if (primitive.Polyline != null && primitive.Polyline.Count > 1)
        {
          using var path = new SKPath();
          path.MoveTo(primitive.Polyline[0].ToSKPoint());
          for (var i = 1; i < primitive.Polyline.Count; i++) path.LineTo(primitive.Polyline[i].ToSKPoint());
          stroke.Color = primitive.Color.ToSKColor();
          stroke.StrokeWidth = (float)primitive.LineWidth;
          canvas.DrawPath(path, stroke);
        }
      }
    }

    /// <summary>
    /// Converts a route path to a Skia path; figures are connected and implicitly closed like GDI+ graphics paths when filled.
    /// </summary>
    public static SKPath ToSKPath(RoutePath routePath)
    {
      var path = new SKPath { FillType = SKPathFillType.Winding };
      foreach (var figure in routePath.Figures)
      {
        var started = false;
        foreach (var command in figure.Commands)
        {
          switch (command)
          {
            case RoutePathLine line:
              if (!started) path.MoveTo(line.Start.ToSKPoint());
              else path.LineTo(line.Start.ToSKPoint());
              path.LineTo(line.End.ToSKPoint());
              break;
            case RoutePathArc arc:
              path.ArcTo(arc.Bounds.ToSKRect(), (float)arc.StartAngle, (float)arc.SweepAngle, !started);
              break;
          }
          started = true;
        }
        if (started) path.Close();
      }
      return path;
    }

    public static void DrawMarker(SKCanvas canvas, IMarkerDrawer marker, PointD p, double zoom)
    {
      switch (marker)
      {
        case DiscDrawer disc:
          using (var paint = SkiaUtil.CreateFillPaint(disc.Color.ToSKColor()))
          {
            canvas.DrawCircle((float)p.X, (float)p.Y, (float)(zoom * disc.Radius), paint);
          }
          break;
        case DiscAndCircleDrawer discAndCircle:
          using (var fill = SkiaUtil.CreateFillPaint(discAndCircle.InnerColor.ToSKColor()))
          using (var stroke = SkiaUtil.CreateStrokePaint(discAndCircle.OuterColor.ToSKColor(), (float)(zoom * discAndCircle.OuterLineWidth)))
          {
            canvas.DrawCircle((float)p.X, (float)p.Y, (float)(zoom * discAndCircle.InnerRadius), fill);
            canvas.DrawCircle((float)p.X, (float)p.Y, (float)(zoom * (discAndCircle.OuterRadius - discAndCircle.OuterLineWidth / 2)), stroke);
          }
          break;
        case CircleDrawer circle:
          using (var stroke = SkiaUtil.CreateStrokePaint(circle.Color.ToSKColor(), (float)(zoom * circle.LineWidth)))
          {
            canvas.DrawCircle((float)p.X, (float)p.Y, (float)(zoom * (circle.Radius - circle.LineWidth / 2)), stroke);
          }
          break;
      }
    }

    /// <summary>
    /// Fills a rectangle with the gradient between the specified locations (0..1).
    /// </summary>
    public static void DrawGradient(SKCanvas canvas, Gradient gradient, SKRectI rect, double startLocation, double endLocation, Gradient.Direction direction, double alphaAdjustment = 0)
    {
      using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false };
      switch (direction)
      {
        case Gradient.Direction.Horizontal:
        case Gradient.Direction.ReverseHorizontal:
          for (var x = rect.Left; x <= rect.Right; x++)
          {
            var t = (x - rect.Left) / (double)(rect.Right - rect.Left) * (endLocation - startLocation);
            var location = direction == Gradient.Direction.Horizontal ? startLocation + t : endLocation - t;
            paint.Color = GraphicsUtil.AlphaAdjustColor(gradient.GetColor(location), alphaAdjustment).ToSKColor();
            canvas.DrawRect(SKRect.Create(x, rect.Top, 1, rect.Height), paint);
          }
          break;
        default:
          for (var y = rect.Top; y <= rect.Bottom; y++)
          {
            var t = (y - rect.Top) / (double)(rect.Bottom - rect.Top) * (endLocation - startLocation);
            var location = direction == Gradient.Direction.Vertical ? startLocation + t : endLocation - t;
            paint.Color = GraphicsUtil.AlphaAdjustColor(gradient.GetColor(location), alphaAdjustment).ToSKColor();
            canvas.DrawRect(SKRect.Create(rect.Left, y, rect.Width, 1), paint);
          }
          break;
      }
    }

    public static void FillCheckerboard(SKCanvas canvas, SKRectI rect, int boxSize)
    {
      using var light = SkiaUtil.CreateFillPaint(new SKColor(211, 211, 211), false);
      using var white = SkiaUtil.CreateFillPaint(SKColors.White, false);
      var rowIndex = 0;
      for (var y = rect.Top; y <= rect.Bottom; y += boxSize, rowIndex++)
      {
        var index = rowIndex % 2;
        for (var x = rect.Left; x <= rect.Right; x += boxSize, index = (index + 1) % 2)
        {
          canvas.DrawRect(SKRect.Create(x, y, System.Math.Min(boxSize, rect.Right - x), System.Math.Min(boxSize, rect.Bottom - y)), index == 0 ? light : white);
        }
      }
    }
  }
}
