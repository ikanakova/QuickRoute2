using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using QuickRoute.BusinessEntities;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Conversions and shared helpers between the document model and SkiaSharp.
  /// </summary>
  public static class SkiaUtil
  {
    private static readonly ConditionalWeakTable<Map, SKImage> decodedMaps = new ConditionalWeakTable<Map, SKImage>();

    public static SKColor ToSKColor(this Color color) => new SKColor(color.R, color.G, color.B, color.A);

    public static SKPoint ToSKPoint(this PointD point) => new SKPoint((float)point.X, (float)point.Y);

    public static SKRect ToSKRect(this RectangleD rectangle) =>
      SKRect.Create((float)rectangle.Left, (float)rectangle.Top, (float)rectangle.Width, (float)rectangle.Height);

    public static SKRect ToSKRect(this Rectangle rectangle) => SKRect.Create(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    /// <summary>
    /// Gets the decoded image of a map. Decoded images are cached for as long as the map object is alive.
    /// </summary>
    public static SKImage GetImage(Map map)
    {
      if (map == null) throw new ArgumentNullException(nameof(map));
      return decodedMaps.GetValue(map, m =>
      {
        using var bitmap = Decode(m.EncodedImage);
        return SKImage.FromBitmap(bitmap);
      });
    }

    public static SKBitmap Decode(byte[] encodedImage)
    {
      var bitmap = SKBitmap.Decode(encodedImage);
      if (bitmap == null) throw new InvalidDataException("The image could not be decoded.");
      return bitmap;
    }

    public static SKPaint CreateFillPaint(SKColor color, bool antialias = true) =>
      new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = antialias };

    public static SKPaint CreateStrokePaint(SKColor color, float width, bool antialias = true) =>
      new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = antialias };

    public static SKTypeface DefaultTypeface { get; } = SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;

    public static SKTypeface GetTypeface(string preferredFamily)
    {
      var typeface = SKTypeface.FromFamilyName(preferredFamily);
      return typeface == null || !string.Equals(typeface.FamilyName, preferredFamily, StringComparison.OrdinalIgnoreCase) ? DefaultTypeface : typeface;
    }

    /// <summary>
    /// Draws text with its top-left corner at the specified position, like GDI+ DrawString.
    /// </summary>
    public static void DrawTextTopLeft(this SKCanvas canvas, string text, float x, float y, SKPaint paint)
    {
      if (string.IsNullOrEmpty(text)) return;
      canvas.DrawText(text, x, y - paint.FontMetrics.Ascent, paint);
    }

    public static SKSize MeasureTextSize(this SKPaint paint, string text)
    {
      var metrics = paint.FontMetrics;
      return new SKSize(string.IsNullOrEmpty(text) ? 0 : paint.MeasureText(text), metrics.Descent - metrics.Ascent);
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
      using var image = SKImage.FromBitmap(bitmap);
      using var data = image.Encode(format, quality);
      if (data == null) throw new InvalidOperationException("The image could not be encoded.");
      return data.ToArray();
    }
  }
}
