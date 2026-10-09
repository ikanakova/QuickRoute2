using System.Collections.Generic;
using System.Drawing;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// A renderer-independent path made of figures, filled using the non-zero winding rule.
  /// Consecutive commands in a figure are connected, mirroring the semantics of a GDI+ graphics path.
  /// </summary>
  public sealed class RoutePath
  {
    public List<RoutePathFigure> Figures { get; } = new List<RoutePathFigure>();

    internal RoutePathFigure CurrentFigure
    {
      get
      {
        if (Figures.Count == 0) Figures.Add(new RoutePathFigure());
        return Figures[Figures.Count - 1];
      }
    }

    public void AddLine(PointD start, PointD end)
    {
      CurrentFigure.Commands.Add(new RoutePathLine(start, end));
    }

    public void AddLine(PointF start, PointF end)
    {
      AddLine(new PointD(start.X, start.Y), new PointD(end.X, end.Y));
    }

    /// <summary>
    /// Adds an elliptical arc. Angles are in degrees, measured clockwise from the x axis.
    /// </summary>
    public void AddArc(RectangleD bounds, double startAngle, double sweepAngle)
    {
      CurrentFigure.Commands.Add(new RoutePathArc(bounds, startAngle, sweepAngle));
    }

    public void AddArc(RectangleF bounds, float startAngle, float sweepAngle)
    {
      AddArc(new RectangleD(bounds.X, bounds.Y, bounds.Width, bounds.Height), startAngle, sweepAngle);
    }

    /// <summary>
    /// Appends the figures of another path as separate figures.
    /// </summary>
    public void AddPath(RoutePath path)
    {
      foreach (var figure in path.Figures) Figures.Add(figure);
    }
  }

  public sealed class RoutePathFigure
  {
    public List<RoutePathCommand> Commands { get; } = new List<RoutePathCommand>();
  }

  public abstract class RoutePathCommand
  {
  }

  public sealed class RoutePathLine : RoutePathCommand
  {
    public RoutePathLine(PointD start, PointD end)
    {
      Start = start;
      End = end;
    }

    public PointD Start { get; }

    public PointD End { get; }
  }

  public sealed class RoutePathArc : RoutePathCommand
  {
    public RoutePathArc(RectangleD bounds, double startAngle, double sweepAngle)
    {
      Bounds = bounds;
      StartAngle = startAngle;
      SweepAngle = sweepAngle;
    }

    public RectangleD Bounds { get; }

    public double StartAngle { get; }

    public double SweepAngle { get; }
  }

  /// <summary>
  /// A single drawing operation produced when drawing a route: either a filled path or a stroked polyline.
  /// </summary>
  public sealed class RouteDrawingPrimitive
  {
    private RouteDrawingPrimitive(Color color)
    {
      Color = color;
    }

    public static RouteDrawingPrimitive Fill(RoutePath path, Color color)
    {
      return new RouteDrawingPrimitive(color) { Path = path };
    }

    public static RouteDrawingPrimitive Stroke(IList<PointD> points, Color color, double lineWidth)
    {
      return new RouteDrawingPrimitive(color) { Polyline = points, LineWidth = lineWidth };
    }

    public Color Color { get; }

    /// <summary>
    /// The path to fill, or null for stroked polylines.
    /// </summary>
    public RoutePath Path { get; private set; }

    /// <summary>
    /// The polyline to stroke with round joins, or null for filled paths.
    /// </summary>
    public IList<PointD> Polyline { get; private set; }

    public double LineWidth { get; private set; }
  }
}
