using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using QuickRoute.BusinessEntities;
using QuickRoute.Resources;

namespace QuickRoute.Rendering
{
  public enum KmlExportMapType
  {
    None,
    Map,
    MapAndRoute
  }

  public enum KmlExportRouteType
  {
    None,
    Monochrome,
    ColorCoded
  }

  public enum KmlRouteAdaptationStyle
  {
    NoAdaption,
    AdaptToSessionMapImage,
    AdaptToDocumentMapImage
  }

  public class KmlProperties
  {
    public KmlExportMapType MapType { get; set; } = KmlExportMapType.MapAndRoute;
    public KmlExportRouteType RouteType { get; set; } = KmlExportRouteType.None;

    /// <summary>
    /// Whether the route waypoint coordinates should be slightly transformed to exactly adapt to the map image rather than to reality.
    /// </summary>
    public KmlRouteAdaptationStyle RouteAdaptionStyle { get; set; } = KmlRouteAdaptationStyle.NoAdaption;

    /// <summary>
    /// The number of possible colors when the route line is color coded.
    /// </summary>
    public int NoOfColorCodedSteps { get; set; } = 32;
  }

  /// <summary>
  /// Creates KMZ files (map ground overlay and route lines) from a QuickRoute document.
  /// </summary>
  public class KmzExporter
  {
    private static readonly DateTimeOffset entryTimestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Document document;
    private readonly ImageExporter imageExporter;
    private readonly Dictionary<string, string> lineStyles = new Dictionary<string, string>();

    public KmzExporter(Document document, ImageExporter imageExporter, KmlProperties properties)
    {
      this.document = document ?? throw new ArgumentNullException(nameof(document));
      this.imageExporter = imageExporter ?? new ImageExporter(document);
      Properties = properties ?? new KmlProperties();
    }

    public KmlProperties Properties { get; }

    public void Export(Stream outputStream)
    {
      document.EnsureProjectionOrigin();
      byte[] overlay = null;
      string overlayName = null;
      if (Properties.MapType != KmlExportMapType.None)
      {
        imageExporter.Properties.RouteDrawingMode = Properties.MapType == KmlExportMapType.Map ? Document.RouteDrawingMode.None : Document.RouteDrawingMode.Extended;
        using var imageStream = new MemoryStream();
        imageExporter.Export(imageStream);
        overlay = imageStream.ToArray();
        overlayName = "o0" + (imageExporter.Properties.Format == ExportImageFormat.Png ? ".png" : ".jpg");
      }

      using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);
      var kmlEntry = archive.CreateEntry("doc.kml", CompressionLevel.Optimal);
      kmlEntry.LastWriteTime = entryTimestamp;
      using (var kmlStream = kmlEntry.Open())
      using (var writer = XmlWriter.Create(kmlStream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "  " }))
      {
        WriteKml(writer, overlayName);
      }
      if (overlay != null)
      {
        var overlayEntry = archive.CreateEntry(overlayName, CompressionLevel.NoCompression);
        overlayEntry.LastWriteTime = entryTimestamp;
        using var entryStream = overlayEntry.Open();
        entryStream.Write(overlay, 0, overlay.Length);
      }
    }

    private void WriteKml(XmlWriter writer, string overlayName)
    {
      var fp = CultureInfo.InvariantCulture;
      writer.WriteStartDocument();
      writer.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
      writer.WriteStartElement("Document");
      writer.WriteElementString("name", document.Properties.Name ?? Strings.QuickRoute);
      writer.WriteElementString("open", "0");

      var sessions = imageExporter.Sessions;
      var lines = new List<(Session Session, List<(List<LongLat> Points, string StyleId)> Lines)>();
      if (Properties.RouteType != KmlExportRouteType.None)
      {
        foreach (var session in sessions) lines.Add((session, CreateRouteLines(session)));
        foreach (var style in lineStyles)
        {
          var parts = style.Key.Split('|');
          writer.WriteStartElement("Style");
          writer.WriteAttributeString("id", style.Value);
          writer.WriteStartElement("LineStyle");
          writer.WriteElementString("color", parts[0]);
          writer.WriteElementString("width", parts[1]);
          writer.WriteEndElement();
          writer.WriteEndElement();
        }
      }

      if (overlayName != null) WriteGroundOverlay(writer, overlayName, fp);

      foreach (var (session, sessionLines) in lines)
      {
        writer.WriteStartElement("Folder");
        var personName = session.SessionInfo.Person?.ToString();
        writer.WriteElementString("name", string.IsNullOrEmpty(personName) ? Strings.Route : personName);
        writer.WriteElementString("open", "0");
        foreach (var (points, styleId) in sessionLines)
        {
          writer.WriteStartElement("Placemark");
          writer.WriteElementString("styleUrl", "#" + styleId);
          writer.WriteStartElement("LineString");
          writer.WriteElementString("extrude", "1");
          writer.WriteElementString("tessellate", "1");
          var coordinates = new StringBuilder();
          foreach (var p in points)
            coordinates.Append(p.Longitude.ToString(fp)).Append(',').Append(p.Latitude.ToString(fp)).Append(' ');
          writer.WriteElementString("coordinates", coordinates.ToString().TrimEnd());
          writer.WriteEndElement();
          writer.WriteEndElement();
        }
        writer.WriteEndElement();
      }

      writer.WriteEndElement(); // Document
      writer.WriteEndElement(); // kml
      writer.WriteEndDocument();
    }

    private void WriteGroundOverlay(XmlWriter writer, string overlayName, IFormatProvider fp)
    {
      var corners = document.GetImageCornersLongLat(imageExporter.ImageBounds, imageExporter.MapBounds, imageExporter.Properties.PercentualSize);
      var rotationD = GetImageRotationD(corners);
      var rotated = GetOrthogonallyRotatedCorners(corners, rotationD);

      writer.WriteStartElement("GroundOverlay");
      writer.WriteElementString("name", Strings.Map);
      writer.WriteStartElement("Icon");
      writer.WriteElementString("href", overlayName);
      writer.WriteEndElement();
      writer.WriteStartElement("LatLonBox");
      writer.WriteElementString("north", Math.Max(rotated[1].Latitude, rotated[2].Latitude).ToString(fp));
      writer.WriteElementString("south", Math.Min(rotated[0].Latitude, rotated[3].Latitude).ToString(fp));
      writer.WriteElementString("east", Math.Max(rotated[2].Longitude, rotated[3].Longitude).ToString(fp));
      writer.WriteElementString("west", Math.Min(rotated[0].Longitude, rotated[1].Longitude).ToString(fp));
      writer.WriteElementString("rotation", rotationD.ToString(fp));
      writer.WriteEndElement();
      writer.WriteEndElement();
    }

    private List<(List<LongLat>, string)> CreateRouteLines(Session session)
    {
      var result = new List<(List<LongLat>, string)>();
      var route = GetRouteForSession(session);
      var sessionSettings = imageExporter.Properties.SessionSettings ?? session.Settings;
      var cca = imageExporter.Properties.ColorCodingAttribute;
      var rls = sessionSettings.RouteLineSettingsCollection[cca];
      for (var segmentIndex = 0; segmentIndex < route.Segments.Count; segmentIndex++)
      {
        var waypoints = route.Segments[segmentIndex].Waypoints;
        if (Properties.RouteType == KmlExportRouteType.Monochrome)
        {
          var points = new List<LongLat>();
          foreach (var waypoint in waypoints) points.Add(waypoint.LongLat);
          result.Add((points, GetLineStyleId(rls.MonochromeColor, rls.MonochromeWidth)));
          continue;
        }

        List<LongLat> current = null;
        string currentStyle = null;
        for (var i = 0; i < waypoints.Count; i++)
        {
          var value = route.GetAttributeFromParameterizedLocation(cca, new ParameterizedLocation(segmentIndex, i));
          var style = GetLineStyleId(rls.ColorRange.GetColor(value ?? 0, Properties.NoOfColorCodedSteps), rls.Width);
          if (current == null)
          {
            current = new List<LongLat> { waypoints[i].LongLat };
            currentStyle = style;
            continue;
          }
          current.Add(waypoints[i].LongLat);
          if (style != currentStyle)
          {
            result.Add((current, currentStyle));
            current = new List<LongLat> { waypoints[i].LongLat };
            currentStyle = style;
          }
        }
        if (current != null && current.Count > 1) result.Add((current, currentStyle));
      }
      return result;
    }

    private Route GetRouteForSession(Session session)
    {
      if (Properties.RouteAdaptionStyle != KmlRouteAdaptationStyle.NoAdaption && Properties.MapType != KmlExportMapType.None)
      {
        var baseSession = document.Sessions.Count == 0 || Properties.RouteAdaptionStyle == KmlRouteAdaptationStyle.AdaptToSessionMapImage
          ? session
          : document.Sessions[0];
        return session.CreateRouteAdaptedToSingleTransformationMatrix(baseSession);
      }
      return session.Route;
    }

    private string GetLineStyleId(Color color, double width)
    {
      // KML colors are written as aabbggrr
      var key = string.Format(CultureInfo.InvariantCulture, "{0:x2}{1:x2}{2:x2}{3:x2}|{4}", color.A, color.B, color.G, color.R, width);
      if (!lineStyles.TryGetValue(key, out var id))
      {
        id = "r" + lineStyles.Count.ToString(CultureInfo.InvariantCulture);
        lineStyles.Add(key, id);
      }
      return id;
    }

    private LongLat[] GetOrthogonallyRotatedCorners(LongLat[] corners, double rotationD)
    {
      var rotationR = LinearAlgebraUtil.ToRadians(rotationD);
      var origin = document.ProjectionOrigin;
      var center = corners[0] / 4 + corners[1] / 4 + corners[2] / 4 + corners[3] / 4;
      var projectedCenter = center.Project(origin);
      var result = new LongLat[4];
      for (var i = 0; i < 4; i++)
      {
        result[i] = LongLat.Deproject(LinearAlgebraUtil.Rotate(corners[i].Project(origin), projectedCenter, rotationR), origin);
      }
      return result;
    }

    private double GetImageRotationD(LongLat[] corners)
    {
      var sw = corners[0].Project(document.ProjectionOrigin);
      var se = corners[3].Project(document.ProjectionOrigin);
      return LinearAlgebraUtil.GetAngleD(se - sw);
    }
  }
}
