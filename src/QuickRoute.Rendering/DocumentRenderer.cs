using System;
using System.Collections.Generic;
using QuickRoute.BusinessEntities;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Renders map images and routes of a document.
  /// </summary>
  public static class DocumentRenderer
  {
    public static readonly SKPaint HighQualityImagePaint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };

    /// <summary>
    /// Draws the map scaled by the zoom factor with its top-left corner at the canvas origin.
    /// </summary>
    public static void DrawMap(SKCanvas canvas, Document document, double zoom)
    {
      var image = SkiaUtil.GetImage(document.Map);
      var size = document.GetMapImageSize(zoom);
      canvas.DrawImage(image, SKRect.Create(0, 0, image.Width, image.Height), SKRect.Create(0, 0, size.Width, size.Height), HighQualityImagePaint);
    }

    public static void DrawRoutes(SKCanvas canvas, IEnumerable<Session> sessions, double zoom, Document.RouteDrawingMode mode,
      WaypointAttribute colorCodingAttribute, WaypointAttribute? secondaryColorCodingAttribute, SessionSettings sessionSettings)
    {
      foreach (var session in sessions)
      {
        RouteRenderer.DrawPrimitives(canvas, session.CreateRouteDrawing(zoom, mode, colorCodingAttribute, secondaryColorCodingAttribute, sessionSettings));
      }
    }

    public static SKBitmap CreateMapAndRouteImage(Document document, bool showMap, double zoom, IEnumerable<Session> sessions,
      WaypointAttribute colorCodingAttribute, WaypointAttribute? secondaryColorCodingAttribute, Document.RouteDrawingMode mode, SessionSettings sessionSettings)
    {
      var size = document.GetMapImageSize(zoom);
      var bitmap = new SKBitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
      using var canvas = new SKCanvas(bitmap);
      canvas.Clear(SKColors.Transparent);
      if (showMap) DrawMap(canvas, document, zoom);
      if (mode != Document.RouteDrawingMode.None)
      {
        canvas.ClipRect(SKRect.Create(0, 0, size.Width, size.Height));
        DrawRoutes(canvas, sessions, zoom, mode, colorCodingAttribute, secondaryColorCodingAttribute, sessionSettings);
      }
      return bitmap;
    }

    /// <summary>
    /// Creates an image of the specified legs of a session, cropped to the route with the specified relative frame width.
    /// </summary>
    public static SKBitmap CreateMapAndRouteImage(Document document, double zoom, Session session, List<int> legsToDraw, double frameWidth,
      WaypointAttribute colorCodingAttribute, WaypointAttribute? secondaryColorCodingAttribute, Document.RouteDrawingMode mode, SessionSettings sessionSettings)
    {
      var frame = Document.GetFrame(zoom, session, legsToDraw, frameWidth, colorCodingAttribute, mode);
      using var wholeImage = CreateMapAndRouteImage(document, true, zoom, new[] { session }, colorCodingAttribute, secondaryColorCodingAttribute, mode, sessionSettings);
      Document.AdjustFrameToImage(frame, new System.Drawing.Size(wholeImage.Width, wholeImage.Height));
      var cropped = new SKBitmap(
        Math.Max(1, Convert.ToInt32(Math.Ceiling(frame.Right) - Math.Floor(frame.Left))),
        Math.Max(1, Convert.ToInt32(Math.Ceiling(frame.Bottom) - Math.Floor(frame.Top))),
        SKColorType.Rgba8888, SKAlphaType.Premul);
      using var canvas = new SKCanvas(cropped);
      canvas.Clear(SKColors.Transparent);
      canvas.DrawBitmap(wholeImage, -Convert.ToInt32(Math.Floor(frame.Left)), -Convert.ToInt32(Math.Floor(frame.Top)));
      return cropped;
    }

    /// <summary>
    /// Draws lap markers of a session in the same way as the interactive map view.
    /// </summary>
    public static void DrawLapMarkers(SKCanvas canvas, Session session, double zoom)
    {
      if (!session.Settings.MarkerDrawers.TryGetValue(MarkerType.Lap, out var marker)) return;
      foreach (var lap in session.Laps)
      {
        if (lap.LapType == LapType.Lap || (lap.LapType == LapType.Stop && session.Route.Segments.Count > 1))
        {
          var pl = session.Route.GetParameterizedLocationFromTime(lap.Time);
          var p = session.AdjustedRoute.GetLocationFromParameterizedLocation(pl);
          RouteRenderer.DrawMarker(canvas, marker, zoom * p, zoom);
        }
      }
    }
  }
}
