using System;
using System.Collections.Generic;
using QuickRoute.BusinessEntities.Numeric;
using System.Drawing;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// The data and coordinate mapping of a line graph. Layout and drawing are performed by the rendering layer.
  /// </summary>
  public class LineGraph
  {
    private List<List<LineGraphPoint>> pointsList;
    private Session session;
    public ParameterizedLocation StartPL { get; set; }
    public ParameterizedLocation EndPL { get; set; }

    private DomainAttribute xAxisAttribute;
    private double xAxisMinValue;
    private double xAxisMaxValue;
    private NumericConverter xAxisNumericConverter;
    private ScaleCreatorBase xAxisScaleCreator;
    private string xAxisCaption;

    private WaypointAttribute yAxisAttribute;
    private double yAxisMinValue;
    private double yAxisMaxValue;
    private NumericConverter yAxisNumericConverter;
    private ScaleCreatorBase yAxisScaleCreator;
    private string yAxisCaption;

    private Rectangle graphDrawingRectangle;

    /// <summary>
    /// The calculated line points, one list per route segment.
    /// </summary>
    public IReadOnlyList<List<LineGraphPoint>> Points => pointsList;

    public string XAxisCaption
    {
      get { return xAxisCaption; }
      set { xAxisCaption = value; }
    }

    public string YAxisCaption
    {
      get { return yAxisCaption; }
      set { yAxisCaption = value; }
    }

    public Session Session
    {
      get { return session; }
      set { session = value; }
    }

    public DomainAttribute XAxisAttribute
    {
      get { return xAxisAttribute; }
      set { xAxisAttribute = value; }
    }

    public WaypointAttribute YAxisAttribute
    {
      get { return yAxisAttribute; }
      set { yAxisAttribute = value; }
    }

    public double XAxisMinValue
    {
      get { return xAxisMinValue; }
      set { xAxisMinValue = value; }
    }

    public double XAxisMaxValue
    {
      get { return xAxisMaxValue; }
      set { xAxisMaxValue = value; }
    }

    public NumericConverter XAxisNumericConverter
    {
      get { return xAxisNumericConverter; }
      set { xAxisNumericConverter = value; }
    }

    public ScaleCreatorBase XAxisScaleCreator
    {
      get { return xAxisScaleCreator; }
      set { xAxisScaleCreator = value; }
    }

    public double YAxisMinValue
    {
      get { return yAxisMinValue; }
      set { yAxisMinValue = value; }
    }

    public double YAxisMaxValue
    {
      get { return yAxisMaxValue; }
      set { yAxisMaxValue = value; }
    }

    public NumericConverter YAxisNumericConverter
    {
      get { return yAxisNumericConverter; }
      set { yAxisNumericConverter = value; }
    }

    public ScaleCreatorBase YAxisScaleCreator
    {
      get { return yAxisScaleCreator; }
      set { yAxisScaleCreator = value; }
    }

    public Rectangle GraphDrawingRectangle
    {
      get { return graphDrawingRectangle; }
      set { graphDrawingRectangle = value; }
    }

    public void Calculate()
    {
      pointsList = new List<List<LineGraphPoint>>();
      DateTime startTime = session.Route.GetTimeFromParameterizedLocation(StartPL);
      DateTime endTime = session.Route.GetTimeFromParameterizedLocation(EndPL);

      foreach (RouteSegment rs in session.Route.Segments)
      {
        var points = new List<LineGraphPoint>();
        for (var i = 0; i < rs.Waypoints.Count; i++)
        {
          var previousWaypoint = i > 0 ? rs.Waypoints[i - 1] : null;
          var thisWaypoint = rs.Waypoints[i];
          var nextWaypoint = i < rs.Waypoints.Count - 1 ? rs.Waypoints[i + 1] : null;

          if (thisWaypoint.Time >= startTime && thisWaypoint.Time <= endTime)
          {
            var p = new LineGraphPoint
                      {
                        X = GetXValue(thisWaypoint),
                        Y = GetYValue(thisWaypoint),
                        Type = (i == 0 ? LineGraphPointType.Start
                                       : (i == rs.Waypoints.Count - 1 ? LineGraphPointType.End : LineGraphPointType.Intermediate))
                      };

            // handle null values 
            List<LineGraphPoint> beforePoints = null;
            List<LineGraphPoint> afterPoints = null;
            if (previousWaypoint != null)
            {
              if (YAxisAttributeIsMapReadingAttribute && thisWaypoint.MapReadingState == MapReadingState.StartReading)
              {
                beforePoints = new List<LineGraphPoint>()
                                 {
                                   new LineGraphPoint() {X = p.X, Y = 0, Type = LineGraphPointType.Intermediate},
                                 };
              }
              else if (!previousWaypoint.Attributes[yAxisAttribute].HasValue && thisWaypoint.Attributes[yAxisAttribute].HasValue)
              {
                beforePoints = new List<LineGraphPoint>() 
                { 
                  new LineGraphPoint() {X = (GetXValue(previousWaypoint) + p.X)/2, Y = 0, Type = LineGraphPointType.Intermediate},
                  new LineGraphPoint() {X = (GetXValue(previousWaypoint) + p.X)/2, Y = p.Y, Type = LineGraphPointType.Intermediate}
                };
              }
            }
            if (nextWaypoint != null)
            {
              if (YAxisAttributeIsMapReadingAttribute && thisWaypoint.MapReadingState == MapReadingState.EndReading)
              {
                afterPoints = new List<LineGraphPoint>()
                                 {
                                   new LineGraphPoint() {X = p.X, Y = 0, Type = LineGraphPointType.Intermediate},
                                 };
              }
              else if (!nextWaypoint.Attributes[yAxisAttribute].HasValue && thisWaypoint.Attributes[yAxisAttribute].HasValue)
              {
                afterPoints = new List<LineGraphPoint>() 
                { 
                  new LineGraphPoint() {X = (GetXValue(nextWaypoint) + p.X)/2, Y = p.Y, Type = LineGraphPointType.Intermediate},
                  new LineGraphPoint() {X = (GetXValue(nextWaypoint) + p.X)/2, Y = 0, Type = LineGraphPointType.Intermediate}
                };
              }
            }
            if (beforePoints != null) points.AddRange(beforePoints);
            points.Add(p);
            if (afterPoints != null) points.AddRange(afterPoints);
          }
        }
        if (points.Count > 0)
        {
          points[0].Type = LineGraphPointType.Start;
          points[points.Count - 1].Type = LineGraphPointType.End;
          pointsList.Add(points);
        }
      }

      bool first = true;
      foreach (var points in pointsList)
      {
        foreach (var p in points)
        {
          if (first || p.X < xAxisMinValue) xAxisMinValue = p.X;
          if (first || p.X > xAxisMaxValue) xAxisMaxValue = p.X;
          //if (first || p.Y < yAxisMinValue) yAxisMinValue = p.Y;
          //if (first || p.Y > yAxisMaxValue) yAxisMaxValue = p.Y;
          first = false;
        }
      }
    }

    private double GetYValue(Waypoint w)
    {
      switch (yAxisAttribute)
      {
        case WaypointAttribute.Speed:
          return w.Attributes[WaypointAttribute.Speed].Value;
        case WaypointAttribute.HeartRate:
          return w.Attributes[WaypointAttribute.HeartRate].GetValueOrDefault(0);
        case WaypointAttribute.Altitude:
          return w.Attributes[WaypointAttribute.Altitude].GetValueOrDefault(0);
        case WaypointAttribute.DirectionDeviationToNextLap:
          return w.Attributes[WaypointAttribute.DirectionDeviationToNextLap].Value;
        case WaypointAttribute.MapReadingDuration:
          return w.Attributes[WaypointAttribute.MapReadingDuration].GetValueOrDefault(0);
        case WaypointAttribute.Cadence:
          return w.Attributes[WaypointAttribute.Cadence].GetValueOrDefault(0);
        case WaypointAttribute.Power:
          return w.Attributes[WaypointAttribute.Power].GetValueOrDefault(0);
        case WaypointAttribute.Pace:
        default:
          return ConvertUtil.ToPace(w.Attributes[WaypointAttribute.Speed].Value).TotalSeconds;
      }
    }

    private double GetXValue(Waypoint w)
    {
      switch (xAxisAttribute)
      {
        case DomainAttribute.ElapsedTime:
          return w.Attributes[WaypointAttribute.ElapsedTime].Value;
        case DomainAttribute.Distance:
          return w.Attributes[WaypointAttribute.Distance].Value;
        case DomainAttribute.TimeOfDay:
        default:
          return (double)w.Time.ToLocalTime().Ticks / TimeSpan.TicksPerSecond;
      }
    }

    /// <summary>
    /// Creates the x axis numeric converter and scale creator matching the x axis attribute.
    /// </summary>
    public void ConfigureXAxis()
    {
      switch (xAxisAttribute)
      {
        case DomainAttribute.TimeOfDay:
          xAxisNumericConverter = new TimeConverter(TimeConverter.TimeConverterType.TimeOfDay);
          XAxisScaleCreator = new TimeScaleCreator(xAxisMinValue, xAxisMaxValue, 15, false);
          break;
        case DomainAttribute.ElapsedTime:
          xAxisNumericConverter = new TimeConverter(TimeConverter.TimeConverterType.ElapsedTime);
          XAxisScaleCreator = new TimeScaleCreator(xAxisMinValue, xAxisMaxValue, 15, false);
          break;
        case DomainAttribute.Distance:
          xAxisNumericConverter = new NumericConverter();
          XAxisScaleCreator = new DoubleScaleCreator(xAxisMinValue, xAxisMaxValue, 15, false);
          break;
      }
    }

    /// <summary>
    /// Gets the x axis value of the specified lap.
    /// </summary>
    public double GetLapXValue(Lap lap)
    {
      switch (xAxisAttribute)
      {
        case DomainAttribute.ElapsedTime:
          return session.Route.GetAttributeFromParameterizedLocation(
            WaypointAttribute.ElapsedTime,
            session.Route.GetParameterizedLocationFromTime(lap.Time)).Value;
        case DomainAttribute.Distance:
          return session.Route.GetAttributeFromParameterizedLocation(
            WaypointAttribute.Distance,
            session.Route.GetParameterizedLocationFromTime(lap.Time)).Value;
        case DomainAttribute.TimeOfDay:
        default:
          return (double)lap.Time.ToLocalTime().Ticks / TimeSpan.TicksPerSecond;
      }
    }

    public float ValueToX(double value)
    {
      if (xAxisMaxValue == xAxisMinValue) return 0;
      return GraphDrawingRectangle.Left +
             (float)((value - xAxisMinValue) / (xAxisMaxValue - xAxisMinValue)) * GraphDrawingRectangle.Width;
    }

    public float ValueToY(double value)
    {
      if (yAxisMaxValue == yAxisMinValue) return 0;
      return GraphDrawingRectangle.Bottom -
             (float)((value - yAxisMinValue) / (yAxisMaxValue - yAxisMinValue)) * GraphDrawingRectangle.Height;
    }

    public double XToValue(int x)
    {
      if (GraphDrawingRectangle.Width == 0) return 0;
      return xAxisMinValue +
             ((double)x - GraphDrawingRectangle.Left) / GraphDrawingRectangle.Width * (xAxisMaxValue - xAxisMinValue);
    }

    public double YToValue(int y)
    {
      if (GraphDrawingRectangle.Height == 0) return yAxisMinValue;
      return yAxisMinValue +
             ((double)GraphDrawingRectangle.Bottom - y) / GraphDrawingRectangle.Height * (yAxisMaxValue - yAxisMinValue);
    }

    public double GetYValueFromXValue(double xValue)
    {
      if (pointsList == null) return 0;
      LineGraphPoint p0 = null;
      LineGraphPoint p1 = null;
      foreach (var points in pointsList)
      {
        foreach (var p in points)
        {
          if (p.X <= xValue && (p0 == null || p.X >= p0.X)) p0 = p;
          if (p.X >= xValue && (p1 == null || p.X < p1.X)) p1 = p;
        }
      }
      if (p0 == null || p1 == null) return 0;
      if (p0.X == p1.X) return p0.Y;
      return p0.Y + (xValue - p0.X) / (p1.X - p0.X) * (p1.Y - p0.Y);
    }

    private bool YAxisAttributeIsMapReadingAttribute
    {
      get { return YAxisAttribute == WaypointAttribute.MapReadingDuration; }
    }
  }

  public class LineGraphPoint : PointD
  {
    public LineGraphPointType Type { get; set; }
  }

  public enum LineGraphPointType
  {
    Start,
    Intermediate,
    End
  }

  public enum DomainAttribute
  {
    TimeOfDay,
    ElapsedTime,
    Distance
  }

}
