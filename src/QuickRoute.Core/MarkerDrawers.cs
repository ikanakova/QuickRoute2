using System.Drawing;
using QuickRoute.BusinessEntities.GlobalizedProperties;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// Describes the appearance of a marker. Drawing is performed by the rendering layer.
  /// </summary>
  public interface IMarkerDrawer
  {
    IMarkerDrawer Copy();
  }

  public class DiscDrawer : GlobalizedObject, IMarkerDrawer
  {
    public DiscDrawer(Color color, double radius)
    {
      Color = color;
      Radius = radius;
    }

    public Color Color { get; set; }

    public double Radius { get; set; }

    public IMarkerDrawer Copy() => new DiscDrawer(Color, Radius);
  }

  public class DiscAndCircleDrawer : GlobalizedObject, IMarkerDrawer
  {
    public DiscAndCircleDrawer(Color innerColor, double innerRadius, Color outerColor, double outerRadius, double outerLineWidth)
    {
      InnerColor = innerColor;
      InnerRadius = innerRadius;
      OuterColor = outerColor;
      OuterRadius = outerRadius;
      OuterLineWidth = outerLineWidth;
    }

    public Color InnerColor { get; set; }

    public double InnerRadius { get; set; }

    public double OuterRadius { get; set; }

    public Color OuterColor { get; set; }

    public double OuterLineWidth { get; set; }

    public IMarkerDrawer Copy() => new DiscAndCircleDrawer(InnerColor, InnerRadius, OuterColor, OuterRadius, OuterLineWidth);
  }

  public class CircleDrawer : GlobalizedObject, IMarkerDrawer
  {
    public CircleDrawer(Color color, double radius, double lineWidth)
    {
      Color = color;
      Radius = radius;
      LineWidth = lineWidth;
    }

    public Color Color { get; set; }

    public double Radius { get; set; }

    public double LineWidth { get; set; }

    public IMarkerDrawer Copy() => new CircleDrawer(Color, Radius, LineWidth);
  }
}
