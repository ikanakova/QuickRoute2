using System;
using System.Collections.Generic;
using System.IO;
using QuickRoute.BusinessEntities;
using QuickRoute.Rendering;
using SkiaSharp;

namespace QuickRoute.Tests
{
  internal static class TestDocuments
  {
    public static string TestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    public static byte[] CreateMapPng(int width = 320, int height = 240)
    {
      using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
      using (var canvas = new SKCanvas(bitmap))
      {
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Blue };
        canvas.DrawRect(SKRect.Create(10, 10, 30, 20), paint);
      }
      return SkiaUtil.Encode(bitmap, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// The same synthetic route that was used to create the legacy test documents.
    /// </summary>
    public static Route CreateSyntheticRoute()
    {
      var start = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc);
      var segment = new RouteSegment();
      for (var i = 0; i < 60; i++)
      {
        var longLat = new LongLat(15.0 + 0.0001 * i, 50.0 + 0.00005 * i + 0.00002 * Math.Sin(i / 5.0));
        segment.Waypoints.Add(new Waypoint(start.AddSeconds(10 * i), longLat, 300 + i * 0.5, 120 + i, null, null, null));
      }
      return new Route(new List<RouteSegment> { segment });
    }

    public static Document CreateSyntheticDocument()
    {
      var start = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc);
      var laps = new LapCollection
      {
        new Lap(start, LapType.Start),
        new Lap(start.AddSeconds(250), LapType.Lap, "Control 1"),
        new Lap(start.AddSeconds(590), LapType.Stop)
      };
      var map = new SkiaMapImageService().CreateMap(CreateMapPng());
      var document = new Document(map, CreateSyntheticRoute(), laps, new DocumentSettings());
      document.EnsureProjectionOrigin();
      return document;
    }
  }
}
