using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// The container object for a QuickRoute document, holding the map, the sessions, and the document settings.
  /// </summary>
  public class Document
  {
    public const int ExportImageHeaderHeight = 64;
    public const int ExportImageBorderWidth = 1;

    private Map map;
    private DocumentSettings settings;
    private SessionCollection sessions = new SessionCollection();
    private LongLat projectionOrigin;
    private DocumentProperties properties;

    public event EventHandler<EventArgs> MapChanged;

    #region Constructors

    /// <summary>
    /// Creates a new document using the specified map and document settings. No sessions are added.
    /// </summary>
    public Document(Map map, DocumentSettings settings)
    {
      Map = map;
      Settings = settings;
    }

    /// <summary>
    /// Creating a new document using the specified map, route, laps, and document settings, and adding one new session with the specified route and laps.
    /// </summary>
    public Document(Map map, Route route, LapCollection laps, DocumentSettings settings)
      : this(map, route, laps, null, settings)
    {
    }

    /// <summary>
    /// Creating a new document using the specified map, route, laps, initial transformation matrix and document settings, and adding one new session with the specified route and laps.
    /// </summary>
    public Document(Map map, Route route, LapCollection laps, GeneralMatrix initialTransformationMatrix, DocumentSettings settings)
      : this(map, route, laps, initialTransformationMatrix, null, settings)
    {
    }

    /// <summary>
    /// Creating a new document using the specified map, route, laps, initial transformation matrix, projection origin and document settings, and adding one new session with the specified route and laps.
    /// </summary>
    public Document(Map map, Route route, LapCollection laps, GeneralMatrix initialTransformationMatrix, LongLat projectionOrigin, DocumentSettings settings)
    {
      Map = map;
      sessions.Add(new Session(route, laps, map.Size, initialTransformationMatrix, projectionOrigin, settings.DefaultSessionSettings));
      this.settings = settings;
      UpdateDocumentToCurrentVersion(this);
    }

    /// <summary>
    /// Creates a document from already loaded parts, e.g. when reading a stored document. Settings are not applied to the sessions.
    /// </summary>
    public static Document FromParts(Map map, DocumentSettings settings, SessionCollection sessions, LongLat projectionOrigin, DocumentProperties properties)
    {
      var document = new Document(map, new DocumentSettings())
      {
        sessions = sessions ?? new SessionCollection(),
        projectionOrigin = projectionOrigin,
        properties = properties
      };
      document.settings = settings ?? new DocumentSettings();
      return document;
    }

    #endregion

    #region Public properties

    /// <summary>
    /// The document's map.
    /// </summary>
    public Map Map
    {
      get { return map; }
      set
      {
        map = value;
        if (MapChanged != null) MapChanged(this, new EventArgs());
      }
    }

    /// <summary>
    /// The settings of the document.
    /// </summary>
    public DocumentSettings Settings
    {
      get { return settings; }
      set
      {
        settings = value;
        // apply to all sessions
        foreach (Session s in sessions)
        {
          s.Settings = settings.DefaultSessionSettings.Copy();
        }
      }
    }

    /// <summary>
    /// The sessions included in the document.
    /// </summary>
    public SessionCollection Sessions
    {
      get { return sessions; }
      set { sessions = value; }
    }

    /// <summary>
    /// The longitude and latitude of the location that acts as the origin for the projection from 3D (earth) to 2D (map).
    /// </summary>
    public LongLat ProjectionOrigin
    {
      get { return projectionOrigin; }
      set { projectionOrigin = value; }
    }

    public string FileName { get; set; }

    /// <summary>
    /// The explicitly stored document properties, or null if none have been set.
    /// </summary>
    public DocumentProperties StoredProperties => properties;

    public DocumentProperties Properties
    {
      get 
      { 
        if(properties == null)
        {
          // create standard properties
          var dp = new DocumentProperties { Name = Path.GetFileNameWithoutExtension(FileName) };
          if(dp.Name == null && Sessions.Count > 0 && Sessions[0].Route.FirstWaypoint != null)
          {
            var dt = Sessions[0].Route.FirstWaypoint.Time.ToLocalTime();
            dp.Name = dt.ToLongDateString() + " " + dt.ToShortTimeString(); 
          }
          return dp;
        }
        return properties; 
      }
      set { properties = value; }
    }

    public QuickRouteFileFormat FileFormat { get; set; }

    #endregion

    #region Public methods

    public void Initialize()
    {
      foreach (var s in sessions)
      {
        s.Initialize(true);
      }
    }

    /// <summary>
    /// Uses the projection origin of the first session when the document has none, as the original map canvas did.
    /// </summary>
    public void EnsureProjectionOrigin()
    {
      if (projectionOrigin == null && sessions.Count > 0) projectionOrigin = sessions[0].ProjectionOrigin;
    }

    public Size GetMapImageSize(double zoomValue)
    {
      return new Size((int)(zoomValue * Map.Width), (int)(zoomValue * Map.Height));
    }

    public Size CalculateImageForExportSize(double zoomValue)
    {
      var size = GetMapImageSize(zoomValue);
      return new Size(size.Width + 2 * ExportImageBorderWidth,
                      size.Height + 3 * ExportImageBorderWidth + ExportImageHeaderHeight);
    }

    public LongLat[] GetImageCornersLongLat(Rectangle imageBounds, Rectangle mapBounds, double zoomValue)
    {
      var swPixel = new PointD(0, Map.Height) +
                    new PointD(imageBounds.Left - mapBounds.Left,
                               imageBounds.Bottom - mapBounds.Bottom) /
                    zoomValue;
      var nePixel = new PointD(Map.Width, 0) +
                    new PointD(imageBounds.Right - mapBounds.Right,
                               imageBounds.Top - mapBounds.Top) /
                    zoomValue;
      var sePixel = new PointD(nePixel.X, swPixel.Y);
      var nwPixel = new PointD(swPixel.X, nePixel.Y);

      var inverseAverageTransformationMatrix = Sessions.CalculateAverageTransformation().TransformationMatrix.Inverse();

      return new[] 
               { 
                 GetLongLatForMapImagePosition(swPixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(nwPixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(nePixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(sePixel, inverseAverageTransformationMatrix)
               };
    }

    public LongLat[] GetMapCornersLongLat()
    {
      var swPixel = new PointD(0, Map.Height);
      var nePixel = new PointD(Map.Width, 0);
      var sePixel = new PointD(nePixel.X, swPixel.Y);
      var nwPixel = new PointD(swPixel.X, nePixel.Y);

      var inverseAverageTransformationMatrix = Sessions.CalculateAverageTransformation().TransformationMatrix.Inverse();

      return new[] 
               { 
                 GetLongLatForMapImagePosition(swPixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(nwPixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(nePixel, inverseAverageTransformationMatrix),
                 GetLongLatForMapImagePosition(sePixel, inverseAverageTransformationMatrix)
               };
    }

    /// <summary>
    /// Converts a map image pixel coordinate to a longitude and latitude coordinate
    /// </summary>
    /// <param name="mapImagePosition">Map pixel coordinate, referring to unzoomed map without any borders and image header</param>
    /// <param name="averageTransformationMatrixInverse">The inverse of the average transformation matrix of the sessions.</param>
    public LongLat GetLongLatForMapImagePosition(PointD mapImagePosition, GeneralMatrix averageTransformationMatrixInverse)
    {
      var projectedPosition = LinearAlgebraUtil.Transform(mapImagePosition, averageTransformationMatrixInverse);
      return LongLat.Deproject(projectedPosition, ProjectionOrigin);
    }

    /// <summary>
    /// Brings documents created by older QuickRoute versions up to date with settings introduced later.
    /// </summary>
    public static void UpdateDocumentToCurrentVersion(Document doc)
    {
      var defaultRLS = SessionSettings.CreateDefaultRouteLineSettingsCollection();

      // ensure MonochromeColor is non-invisible
      foreach (Session s in doc.sessions)
      {
        foreach (var rls in s.Settings.RouteLineSettingsCollection.Values)
        {
          if (rls.MonochromeColor == Color.FromArgb(0, 0, 0, 0))
          {
            rls.MonochromeColor = rls.ColorRange.Gradient.GetColor(1);
            rls.MonochromeWidth = rls.Width;
          }
        }
      }

      // add some speed waypoint attribute settings, introduced in QR 2.1
      if (!doc.Settings.ColorRangeIntervalSliderSettings.ContainsKey(WaypointAttribute.Speed))
      {
        var defaultCRISS = DocumentSettings.CreateDefaultColorRangeIntervalSliderSettings();
        doc.Settings.ColorRangeIntervalSliderSettings.Add(WaypointAttribute.Speed, defaultCRISS[WaypointAttribute.Speed]);
      }

      if (!doc.Settings.LapHistogramSettings.ContainsKey(WaypointAttribute.Speed))
      {
        var defaultLHS = DocumentSettings.CreateDefaultLapHistogramSettings();
        doc.Settings.LapHistogramSettings.Add(WaypointAttribute.Speed, defaultLHS[WaypointAttribute.Speed]);
      }

      foreach (Session s in doc.sessions)
      {
        if (!s.Settings.RouteLineSettingsCollection.ContainsKey(WaypointAttribute.Speed))
        {
          s.Settings.RouteLineSettingsCollection.Add(WaypointAttribute.Speed, defaultRLS[WaypointAttribute.Speed]);
        }
      }

      // add some direction waypoint attribute settings, introduced in QR 2.1-4
      if (!doc.Settings.ColorRangeIntervalSliderSettings.ContainsKey(WaypointAttribute.DirectionDeviationToNextLap))
      {
        var defaultCRISS = DocumentSettings.CreateDefaultColorRangeIntervalSliderSettings();
        doc.Settings.ColorRangeIntervalSliderSettings.Add(WaypointAttribute.DirectionDeviationToNextLap, defaultCRISS[WaypointAttribute.DirectionDeviationToNextLap]);
      }

      if (!doc.Settings.LapHistogramSettings.ContainsKey(WaypointAttribute.DirectionDeviationToNextLap))
      {
        var defaultLHS = DocumentSettings.CreateDefaultLapHistogramSettings();
        doc.Settings.LapHistogramSettings.Add(WaypointAttribute.DirectionDeviationToNextLap, defaultLHS[WaypointAttribute.DirectionDeviationToNextLap]);
      }

      foreach (Session s in doc.sessions)
      {
        if (!s.Settings.RouteLineSettingsCollection.ContainsKey(WaypointAttribute.DirectionDeviationToNextLap))
        {
          s.Settings.RouteLineSettingsCollection.Add(WaypointAttribute.DirectionDeviationToNextLap, defaultRLS[WaypointAttribute.DirectionDeviationToNextLap]);
        }
      }

      // add circle time radius, introduced in QR 2.4
      foreach (var s in doc.sessions)
      {
        if (s.Settings.CircleTimeRadius == 0) s.Settings.CircleTimeRadius = 45;
      }

      // add map reading duration settings, introduced in QR 2.4
      // add cadence and power, introduced September 2014
      var attributes = new[] { WaypointAttribute.MapReadingDuration, WaypointAttribute.Cadence, WaypointAttribute.Power };

      foreach (var attribute in attributes)
      {
        if (!doc.Settings.ColorRangeIntervalSliderSettings.ContainsKey(attribute))
        {
          var defaultCRISS = DocumentSettings.CreateDefaultColorRangeIntervalSliderSettings();
          doc.Settings.ColorRangeIntervalSliderSettings.Add(attribute, defaultCRISS[attribute]);
        }

        if (!doc.Settings.LapHistogramSettings.ContainsKey(attribute))
        {
          var defaultLHS = DocumentSettings.CreateDefaultLapHistogramSettings();
          doc.Settings.LapHistogramSettings.Add(attribute, defaultLHS[attribute]);
        }

        if (!doc.Settings.DefaultSessionSettings.SmoothingIntervals.ContainsKey(attribute))
        {
          doc.Settings.DefaultSessionSettings.SmoothingIntervals[attribute] = new Interval(0, 0);
        }
      }

      foreach (Session s in doc.sessions)
      {
        foreach (var attribute in attributes)
        {
          if (!s.Settings.RouteLineSettingsCollection.ContainsKey(attribute))
          {
            s.Settings.RouteLineSettingsCollection.Add(attribute, defaultRLS[attribute]);
          }
        }
      }
    }

    /// <summary>
    /// Gets the frame (in zoomed pixel coordinates) surrounding the specified legs of a session.
    /// </summary>
    public static RectangleD GetFrame(double zoomValue, Session sessionToDraw, IList<int> legsToDraw, double frameWidth, WaypointAttribute colorCodingAttribute, RouteDrawingMode mode)
    {
      var vertices = sessionToDraw.GetAdjustedWaypointLocations(legsToDraw);
      var rls = sessionToDraw.Settings.RouteLineSettingsCollection[colorCodingAttribute];
      double lineRadius = 0;
      switch(mode)
      {
        case RouteDrawingMode.Simple:
          lineRadius = rls.MonochromeWidth/2;
          break;
        case RouteDrawingMode.Extended:
          lineRadius = (rls.Width + (rls.MaskVisible ? 2 * rls.MaskWidth : 0)) / 2;
          break;
        case RouteDrawingMode.None:
          lineRadius = 0;
          break;
      }

      double minX = zoomValue * (vertices[0].X - lineRadius);
      double maxX = zoomValue * (vertices[0].X + lineRadius);
      double minY = zoomValue * (vertices[0].Y - lineRadius);
      double maxY = zoomValue * (vertices[0].Y + lineRadius);

      for (int i = 0; i < vertices.Count; i++)
      {
        minX = Math.Min(minX, zoomValue * (vertices[i].X - lineRadius));
        maxX = Math.Max(maxX, zoomValue * (vertices[i].X + lineRadius));
        minY = Math.Min(minY, zoomValue * (vertices[i].Y - lineRadius));
        maxY = Math.Max(maxY, zoomValue * (vertices[i].Y + lineRadius));
      }

      double maxWidth = Math.Max(maxX - minX, maxY - minY);
      return new RectangleD(new PointD(minX - frameWidth * maxWidth, minY - frameWidth * maxWidth),
                            new SizeD(maxX - minX + 2 * frameWidth * maxWidth, maxY - minY + 2 * frameWidth * maxWidth));
    }

    /// <summary>
    /// Shrinks the frame so that it lies within an image of the specified size.
    /// </summary>
    public static void AdjustFrameToImage(RectangleD frame, Size imageSize)
    {
      if (frame.Left < 0)
      {
        frame.Width += frame.Left;
        frame.Left = 0;
      }
      if (frame.Top < 0)
      {
        frame.Height += frame.Top;
        frame.Top = 0;
      }
      if (frame.Right > imageSize.Width)
      {
        frame.Width -= frame.Right - imageSize.Width;
      }
      if (frame.Bottom > imageSize.Height)
      {
        frame.Height -= frame.Bottom - imageSize.Height;
      }
    }

    public static string GetVersionString()
    {
      Version version = Assembly.GetExecutingAssembly().GetName(false).Version;
      return version.ToString(3);
    }

    #endregion

    #region Public enums

    public enum RouteDrawingMode
    {
      Simple,
      Extended,
      None
    }

    #endregion
  }

  public class DocumentProperties
  {
    public string Name { get; set; }
  }

  /// <summary>
  /// Class for storing an interval, used e g for smoothing when calculating speeds.
  /// </summary>
  public class Interval
  {
    private double start;
    private double end;

    public Interval(double start, double end)
    {
      if (end < start) throw new Exception("End value must be less than start value.");
      this.start = start;
      this.end = end;
    }

    /// <summary>
    /// Copy constructor.
    /// </summary>
    /// <param name="i"></param>
    public Interval(Interval i)
    {
      start = i.start;
      end = i.end;
    }

    /// <summary>
    /// The start (relative to the origin) of the interval)
    /// </summary>
    public double Start
    {
      get { return start; }
      set
      {
        if (end < value) throw new Exception("Start value must be greater than end value.");
        start = value;
      }
    }

    /// <summary>
    /// The end (relative to the origin) of the interval)
    /// </summary>
    public double End
    {
      get { return end; }
      set
      {
        if (value < start) throw new Exception("End value must be less than start value.");
        end = value;
      }
    }

    /// <summary>
    /// Gets the length of the interval.
    /// </summary>
    public double Length
    {
      get { return end - start; }
    }

    public override bool Equals(object obj)
    {
      if (ReferenceEquals(null, obj)) return false;
      if (ReferenceEquals(this, obj)) return true;
      if (obj.GetType() != typeof (Interval)) return false;
      return Equals((Interval) obj);
    }

    public bool Equals(Interval obj)
    {
      if (ReferenceEquals(null, obj)) return false;
      if (ReferenceEquals(this, obj)) return true;
      return obj.start == start && obj.end == end;
    }

    public override int GetHashCode()
    {
      unchecked
      {
        return (start.GetHashCode()*397) ^ end.GetHashCode();
      }
    }
  }
}
