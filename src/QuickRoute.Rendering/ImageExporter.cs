using System;
using System.Drawing;
using System.IO;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Numeric;
using QuickRoute.Resources;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  public enum ExportImageFormat
  {
    Jpeg,
    Png
  }

  /// <summary>
  /// Creates the exported map image with a QuickRoute header, optional color range and embedded QuickRoute data (JPEG only).
  /// </summary>
  public class ImageExporter
  {
    private static readonly Lazy<SKBitmap> headerLogo = new Lazy<SKBitmap>(() =>
    {
      using var stream = typeof(ImageExporter).Assembly.GetManifestResourceStream("QuickRoute.Rendering.HeaderLogo.png");
      return stream == null ? null : SKBitmap.Decode(stream);
    });

    public ImageExporter(Document document) : this(document, document.Sessions)
    {
    }

    public ImageExporter(Document document, SessionCollection sessions)
    {
      Document = document ?? throw new ArgumentNullException(nameof(document));
      Sessions = sessions ?? document.Sessions;
      Properties = new ImageExporterProperties { SessionSettings = new SessionSettings() };
    }

    public Document Document { get; }

    public SessionCollection Sessions { get; }

    public ImageExporterProperties Properties { get; set; }

    /// <summary>
    /// The bounding rectangle of the whole image, available after export.
    /// </summary>
    public Rectangle ImageBounds { get; private set; }

    /// <summary>
    /// The bounding rectangle of the map part of the image, available after export.
    /// </summary>
    public Rectangle MapBounds { get; private set; }

    public void Export(Stream outputStream)
    {
      Document.EnsureProjectionOrigin();
      using var image = CreateImage();
      byte[] data;
      if (Properties.Format == ExportImageFormat.Jpeg)
      {
        data = SkiaUtil.Encode(image, SKEncodedImageFormat.Jpeg, Math.Max(1, Math.Min(100, (int)Math.Round(100 * Properties.JpegQuality))));
        data = JpegMetadataWriter.InsertExif(data, Strings.QuickRoute + " " + Document.GetVersionString(), GetMapCenter());
        var extensionData = QuickRouteJpegExtensionData.FromQuickRouteDocument(Document, Sessions, ImageBounds, MapBounds, Properties.PercentualSize);
        extensionData.EmbedDataInImage(new MemoryStream(data, false), outputStream);
      }
      else
      {
        data = SkiaUtil.Encode(image, SKEncodedImageFormat.Png, 100);
        outputStream.Write(data, 0, data.Length);
      }
    }

    private LongLat GetMapCenter()
    {
      var center = new LongLat();
      foreach (var corner in Document.GetMapCornersLongLat()) center += corner / 4;
      return center;
    }

    private SKBitmap CreateImage()
    {
      var sessionSettings = Properties.SessionSettings ?? Document.Sessions[0].Settings;
      var routeLineSettings = sessionSettings.RouteLineSettingsCollection[Properties.ColorCodingAttribute];
      const int border = Document.ExportImageBorderWidth;
      const int headerHeight = Document.ExportImageHeaderHeight;

      using var mapAndRouteImage = DocumentRenderer.CreateMapAndRouteImage(Document, Properties.ShowMap, Properties.PercentualSize, Sessions,
        Properties.ColorCodingAttribute, Properties.SecondaryColorCodingAttribute, Properties.RouteDrawingMode, Properties.SessionSettings);

      var image = new SKBitmap(mapAndRouteImage.Width + 2 * border, mapAndRouteImage.Height + headerHeight + 3 * border, SKColorType.Rgba8888, SKAlphaType.Premul);
      ImageBounds = new Rectangle(0, 0, image.Width, image.Height);
      MapBounds = new Rectangle(border, 2 * border + headerHeight, mapAndRouteImage.Width, mapAndRouteImage.Height);

      using var canvas = new SKCanvas(image);
      canvas.Clear(SKColors.Black);
      canvas.DrawBitmap(mapAndRouteImage, MapBounds.X, MapBounds.Y);

      // border
      using (var borderPaint = SkiaUtil.CreateStrokePaint(new SKColor(64, 64, 64), 1, false))
      {
        canvas.DrawRect(SKRect.Create(0.5f, 0.5f, image.Width - 1, image.Height - 1), borderPaint);
        canvas.DrawLine(0, headerHeight + border + 0.5f, image.Width, headerHeight + border + 0.5f, borderPaint);
      }

      // header background and gradient
      using (var black = SkiaUtil.CreateFillPaint(SKColors.Black, false))
      {
        canvas.DrawRect(SKRect.Create(border, border, image.Width - 2 * border, headerHeight), black);
      }
      using (var gradient = new SKPaint
             {
               Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 28),
                 new[] { new SKColor(64, 64, 64), new SKColor(24, 24, 24) }, SKShaderTileMode.Clamp)
             })
      {
        canvas.DrawRect(SKRect.Create(border, border, image.Width - 2 * border, 28), gradient);
      }

      // logo
      if (headerLogo.Value != null) canvas.DrawBitmap(headerLogo.Value, border + 16, border + 6);

      // application name and version
      var typeface = SkiaUtil.GetTypeface("Calibri");
      using (var caption = new SKPaint { Typeface = typeface, TextSize = 18.6f, IsAntialias = true, Color = new SKColor(255, 255, 255, 192) })
      using (var small = new SKPaint { Typeface = typeface, TextSize = 13.3f, IsAntialias = true, Color = new SKColor(255, 255, 255, 128) })
      {
        canvas.DrawTextTopLeft(Strings.QuickRoute, border + 74f, border + 5f, caption);
        canvas.DrawTextTopLeft(string.Format(Strings.Version, Document.GetVersionString()), border + 76f, border + 27f, small);
      }

      if (Properties.RouteDrawingMode == Document.RouteDrawingMode.Extended && Properties.ColorRangeProperties != null)
      {
        using var scalePaint = new SKPaint { Typeface = typeface, TextSize = 13.3f, IsAntialias = true, Color = new SKColor(255, 255, 255, 192) };
        DrawColorRange(canvas, image.Width, routeLineSettings, scalePaint);
      }
      return image;
    }

    private void DrawColorRange(SKCanvas canvas, int imageWidth, RouteLineSettings routeLineSettings, SKPaint scalePaint)
    {
      var crp = Properties.ColorRangeProperties;
      var colorRangeSize = new SKSizeI(Math.Max(100, Math.Min(300, imageWidth - 300)), 16);
      var labelMaxSize = scalePaint.MeasureTextSize(crp.NumericConverter.ToString((double?)crp.ScaleCreator.LastMarkerValue));
      var unitSize = scalePaint.MeasureTextSize(" " + crp.ScaleUnit);
      var rect = SKRectI.Create(
        imageWidth - Document.ExportImageBorderWidth - 16 - (int)unitSize.Width - (int)labelMaxSize.Width / 2 - colorRangeSize.Width, 20,
        colorRangeSize.Width, colorRangeSize.Height);

      RouteRenderer.FillCheckerboard(canvas, rect, 8);
      RouteRenderer.DrawGradient(canvas, routeLineSettings.ColorRange.Gradient, rect, 0, 1, Gradient.Direction.Horizontal, routeLineSettings.AlphaAdjustment);

      using (var markerPaint = SkiaUtil.CreateStrokePaint(new SKColor(0, 0, 0, 192), 1, false))
      {
        var lastX = 0;
        for (var value = crp.ScaleCreator.FirstMarkerValue; value <= crp.ScaleCreator.LastMarkerValue; value += crp.ScaleCreator.MarkerInterval)
        {
          var x = rect.Left + (int)((value - routeLineSettings.ColorRange.StartValue) /
                                    (routeLineSettings.ColorRange.EndValue - routeLineSettings.ColorRange.StartValue) * rect.Width);
          canvas.DrawLine(x + 0.5f, rect.Top, x + 0.5f, rect.Bottom, markerPaint);
          if (value == crp.ScaleCreator.FirstMarkerValue || x - lastX > labelMaxSize.Width)
          {
            var label = crp.NumericConverter.ToString((double?)value);
            canvas.DrawTextTopLeft(label, x - scalePaint.MeasureText(label) / 2, rect.Bottom + 2f, scalePaint);
            lastX = x;
          }
          if (crp.ScaleCreator.MarkerInterval <= 0) break;
        }
      }

      using (var borderPaint = SkiaUtil.CreateStrokePaint(new SKColor(128, 128, 128), 1, false))
      {
        canvas.DrawRect(SKRect.Create(rect.Left - 0.5f, rect.Top - 0.5f, rect.Width + 2, rect.Height + 1), borderPaint);
      }

      var captionText = crp.ScaleCaption + " ";
      canvas.DrawTextTopLeft(captionText, rect.Left - labelMaxSize.Width / 2 - scalePaint.MeasureText(captionText), rect.Bottom + 2f, scalePaint);
      canvas.DrawTextTopLeft(" " + crp.ScaleUnit, rect.Right + labelMaxSize.Width / 2, rect.Bottom + 2f, scalePaint);
    }
  }

  public class ImageExporterProperties
  {
    public ImageExporterProperties()
    {
      Format = ExportImageFormat.Jpeg;
      JpegQuality = 0.8;
      PercentualSize = 1;
      RouteDrawingMode = Document.RouteDrawingMode.Extended;
      ShowMap = true;
      ColorCodingAttribute = WaypointAttribute.Pace;
    }

    public SessionSettings SessionSettings { get; set; }
    public ExportImageFormat Format { get; set; }

    /// <summary>
    /// JPEG quality in the interval (0, 1].
    /// </summary>
    public double JpegQuality { get; set; }
    public double PercentualSize { get; set; }
    public Document.RouteDrawingMode RouteDrawingMode { get; set; }
    public bool ShowMap { get; set; }
    public WaypointAttribute ColorCodingAttribute { get; set; }
    public WaypointAttribute? SecondaryColorCodingAttribute { get; set; }
    public ColorRangeProperties ColorRangeProperties { get; set; }
  }

  public class ColorRangeProperties
  {
    public ScaleCreatorBase ScaleCreator { get; set; }
    public NumericConverter NumericConverter { get; set; }
    public string ScaleCaption { get; set; }
    public string ScaleUnit { get; set; }
  }
}
