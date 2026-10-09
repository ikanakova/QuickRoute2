using System;
using System.Drawing;
using System.IO;
using QuickRoute.BusinessEntities;
using SkiaSharp;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Map image operations implemented with SkiaSharp.
  /// </summary>
  public sealed class SkiaMapImageService : IMapImageService
  {
    private const int CroppedJpegQuality = 90;

    public Map CreateMap(byte[] encodedImage, string source = null, MapSourceType sourceType = MapSourceType.FileSystem)
    {
      if (encodedImage == null || encodedImage.Length == 0) throw new ArgumentException("The image data is empty.", nameof(encodedImage));

      // exported QuickRoute images contain a border and a header; keep only the map part
      var extensionData = QuickRouteJpegExtensionData.FromStream(new MemoryStream(encodedImage, false));
      using var codec = SKCodec.Create(new MemoryStream(encodedImage, false)) ?? throw new InvalidDataException("The image format is not supported.");
      var info = codec.Info;
      if (extensionData != null && !extensionData.MapLocationAndSizeInPixels.IsEmpty &&
          extensionData.MapLocationAndSizeInPixels != new Rectangle(0, 0, info.Width, info.Height))
      {
        var mapBounds = extensionData.MapLocationAndSizeInPixels;
        using var full = SkiaUtil.Decode(encodedImage);
        using var cropped = new SKBitmap(mapBounds.Width, mapBounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(cropped))
        {
          canvas.DrawBitmap(full, -mapBounds.X, -mapBounds.Y);
        }
        return new Map(SkiaUtil.Encode(cropped, SKEncodedImageFormat.Jpeg, CroppedJpegQuality), new Size(mapBounds.Width, mapBounds.Height), source, sourceType);
      }
      return new Map(encodedImage, new Size(info.Width, info.Height), source, sourceType);
    }

    public Map RotateQuarterTurn(Map map, bool clockwise)
    {
      if (map == null) throw new ArgumentNullException(nameof(map));
      using var source = SkiaUtil.Decode(map.EncodedImage);
      using var rotated = new SKBitmap(source.Height, source.Width, source.ColorType, source.AlphaType);
      using (var canvas = new SKCanvas(rotated))
      {
        if (clockwise)
        {
          canvas.Translate(source.Height, 0);
          canvas.RotateDegrees(90);
        }
        else
        {
          canvas.Translate(0, source.Width);
          canvas.RotateDegrees(-90);
        }
        canvas.DrawBitmap(source, 0, 0);
      }
      // lossless encoding to avoid quality loss when rotating repeatedly
      return new Map(SkiaUtil.Encode(rotated, SKEncodedImageFormat.Png, 100), new Size(rotated.Width, rotated.Height));
    }
  }
}
