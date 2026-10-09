using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Importers.GPX;
using QuickRoute.BusinessEntities.Importers.TCX;
using QuickRoute.Persistence;
using QuickRoute.Rendering;
using SkiaSharp;
using Xunit;

namespace QuickRoute.Tests
{
  public class ImportExportTests : IDisposable
  {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "QuickRouteTests-" + Guid.NewGuid().ToString("N"));

    public ImportExportTests()
    {
      Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
      Directory.Delete(directory, true);
    }

    [Fact]
    public void GpxFileIsImported()
    {
      var fileName = Path.Combine(directory, "route.gpx");
      File.WriteAllText(fileName,
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<gpx version=\"1.1\" creator=\"test\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><trkseg>" +
        "<trkpt lat=\"50.0\" lon=\"15.0\"><ele>300</ele><time>2020-05-17T10:00:00Z</time></trkpt>" +
        "<trkpt lat=\"50.001\" lon=\"15.001\"><ele>302</ele><time>2020-05-17T10:00:30Z</time></trkpt>" +
        "<trkpt lat=\"50.002\" lon=\"15.002\"><ele>305</ele><time>2020-05-17T10:01:00Z</time></trkpt>" +
        "</trkseg></trk></gpx>");

      var importer = new GPXImporter { FileName = fileName };
      Assert.Empty(importer.GetImportChoices());
      importer.Import();

      Assert.True(importer.ImportResult.Succeeded);
      var waypoints = importer.ImportResult.Route.Segments[0].Waypoints;
      Assert.Equal(3, waypoints.Count);
      Assert.Equal(15.001, waypoints[1].LongLat.Longitude, 9);
      Assert.Equal(302, waypoints[1].Altitude);
      Assert.Equal(new DateTime(2020, 5, 17, 10, 0, 30, DateTimeKind.Utc), waypoints[1].Time.ToUniversalTime());
    }

    [Fact]
    public void TcxActivitiesAreOfferedAsChoices()
    {
      var fileName = Path.Combine(directory, "route.tcx");
      File.WriteAllText(fileName,
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<TrainingCenterDatabase xmlns=\"http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2\"><Activities>" +
        "<Activity Sport=\"Running\"><Id>2020-05-17T10:00:00Z</Id><Lap StartTime=\"2020-05-17T10:00:00Z\"><TotalTimeSeconds>30</TotalTimeSeconds><Track>" +
        "<Trackpoint><Time>2020-05-17T10:00:00Z</Time><Position><LatitudeDegrees>50.0</LatitudeDegrees><LongitudeDegrees>15.0</LongitudeDegrees></Position></Trackpoint>" +
        "<Trackpoint><Time>2020-05-17T10:00:30Z</Time><Position><LatitudeDegrees>50.001</LatitudeDegrees><LongitudeDegrees>15.001</LongitudeDegrees></Position></Trackpoint>" +
        "</Track></Lap></Activity></Activities></TrainingCenterDatabase>");

      var importer = new TCXImporter { FileName = fileName };
      var choice = Assert.Single(importer.GetImportChoices());
      importer.SelectedChoice = choice;
      importer.Import();

      Assert.True(importer.ImportResult.Succeeded);
      Assert.Equal(2, importer.ImportResult.Route.Segments.Sum(s => s.Waypoints.Count));
    }

    [Fact]
    public void QuarterTurnRotationRotatesOnlyTheMapImage()
    {
      var service = new SkiaMapImageService();
      var document = TestDocuments.CreateSyntheticDocument();
      var handlesBefore = document.Sessions[0].Handles.Length;
      var firstLocation = document.Sessions[0].AdjustedRoute.Segments[0].Waypoints[0].Location;
      var original = document.Map;

      var action = new QuickRoute.BusinessEntities.Actions.RotateMapAction(-Math.PI / 2, document, service);
      action.Execute();
      Assert.Equal(original.Height, document.Map.Width);
      Assert.Equal(original.Width, document.Map.Height);
      using (var rotated = SkiaUtil.Decode(document.Map.EncodedImage))
      {
        // the blue rectangle near the top-left corner moves to the top-right corner when rotating clockwise
        Assert.Equal(SKColors.Blue, rotated.GetPixel(original.Height - 1 - 15, 20));
      }
      Assert.Equal(handlesBefore, document.Sessions[0].Handles.Length);
      Assert.Equal(firstLocation.X, document.Sessions[0].AdjustedRoute.Segments[0].Waypoints[0].Location.X);

      action.Undo();
      Assert.Same(original, document.Map);

      var counterclockwise = service.RotateQuarterTurn(original, false);
      using var ccw = SkiaUtil.Decode(counterclockwise.EncodedImage);
      Assert.Equal(SKColors.Blue, ccw.GetPixel(20, original.Width - 1 - 15));
    }

    [Fact]
    public void ExportedJpegContainsQuickRouteDataAndCanBeOpened()
    {
      var document = TestDocuments.CreateSyntheticDocument();
      var fileName = Path.Combine(directory, "export.jpg");
      var exporter = new ImageExporter(document) { Properties = { SessionSettings = document.Sessions[0].Settings, PercentualSize = 1.5 } };
      using (var stream = File.Create(fileName)) exporter.Export(stream);

      using (var bitmap = SKBitmap.Decode(fileName))
      {
        Assert.Equal(exporter.ImageBounds.Width, bitmap.Width);
        Assert.Equal((int)(1.5 * 240) + Document.ExportImageHeaderHeight + 3 * Document.ExportImageBorderWidth, bitmap.Height);
      }

      var reopened = new DocumentFileService(new SkiaMapImageService()).Open(fileName);
      Assert.NotNull(reopened);
      Assert.Equal(QuickRouteFileFormat.Jpeg, reopened.FileFormat);
      Assert.Equal(exporter.MapBounds.Width, reopened.Map.Width);
      Assert.Equal(exporter.MapBounds.Height, reopened.Map.Height);
      Assert.Equal(60, reopened.Sessions[0].Route.Segments[0].Waypoints.Count);
    }

    [Fact]
    public void PngExportHasNoQuickRouteData()
    {
      var document = TestDocuments.CreateSyntheticDocument();
      var exporter = new ImageExporter(document) { Properties = { Format = ExportImageFormat.Png } };
      using var stream = new MemoryStream();
      exporter.Export(stream);
      Assert.Equal(0x89, stream.ToArray()[0]);
    }

    [Fact]
    public void KmzContainsGroundOverlayAndRouteLines()
    {
      var document = TestDocuments.CreateSyntheticDocument();
      var properties = new KmlProperties { RouteType = KmlExportRouteType.ColorCoded };
      using var stream = new MemoryStream();
      new KmzExporter(document, new ImageExporter(document), properties).Export(stream);

      stream.Position = 0;
      using var archive = new ZipArchive(stream);
      Assert.NotNull(archive.GetEntry("o0.jpg"));
      using var reader = new StreamReader(archive.GetEntry("doc.kml").Open());
      var kml = reader.ReadToEnd();
      Assert.Contains("<GroundOverlay>", kml);
      Assert.Contains("<LatLonBox>", kml);
      Assert.Contains("<LineString>", kml);
    }

    [Fact]
    public void GpxExportCanBeImportedAgain()
    {
      var document = TestDocuments.CreateSyntheticDocument();
      var fileName = Path.Combine(directory, "export.gpx");
      using (var stream = File.Create(fileName)) new QuickRoute.BusinessEntities.Exporters.GpxExporter(document.Sessions[0], stream).Export();
      var importer = new GPXImporter { FileName = fileName };
      importer.Import();
      Assert.True(importer.ImportResult.Succeeded);
      Assert.Equal(60, importer.ImportResult.Route.Segments[0].Waypoints.Count);
    }
  }
}
