using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using QuickRoute.BusinessEntities;
using QuickRoute.Persistence;
using QuickRoute.Rendering;
using Xunit;

namespace QuickRoute.Tests
{
  public class PersistenceTests
  {
    private static readonly DocumentFileService fileService = new DocumentFileService(new SkiaMapImageService());

    // Values calculated by QuickRoute 2.5 (.NET Framework) for the synthetic legacy documents.
    private const double ExpectedHandleX = 176.97379964811009;
    private const double ExpectedHandleY = 108.38708739814206;
    private static readonly double[] expectedInitialMatrix = { 0.55070852370692913, 0, 0, 0, -0.55070852370692913, 0, 160.00354981065863, 120.00229248484186, 1 };
    private static readonly double[] expectedHandleMatrix = { 0.55070852370692913, 0, 0, 0, -0.55070852370692913, 0, 175.00354981065863, 110.00229248484186, 1 };
    private const double ExpectedAdjustedFirstX = 58.755195951417349;
    private const double ExpectedAdjustedFirstY = 199.99999999999997;
    private const double ExpectedAdjustedLastX = 291.24480404858269;
    private const double ExpectedAdjustedLastY = 20.000000000000014;
    private const double ExpectedDistance = 534.09633108971366;

    [Theory]
    [InlineData("synthetic-rawdata.qrt")]
    [InlineData("synthetic-bitmap.qrt")]
    public void LegacyQrtDocumentsAreImportedWithoutBinaryFormatter(string fileName)
    {
      var document = fileService.Open(TestDocuments.TestDataPath(fileName));
      AssertMatchesLegacyExpectations(document);
      Assert.Equal(QuickRouteFileFormat.Qrt, document.FileFormat);
    }

    [Fact]
    public void LegacyDocumentSurvivesRoundTripThroughNewFormat()
    {
      var legacy = fileService.Open(TestDocuments.TestDataPath("synthetic-rawdata.qrt"));
      var reloaded = RoundTrip(legacy);
      AssertMatchesLegacyExpectations(reloaded);
      Assert.Equal(QuickRouteFileFormat.Qrtx, reloaded.FileFormat);
      Assert.Equal(legacy.Map.EncodedImage, reloaded.Map.EncodedImage);

      var pace = reloaded.Sessions[0].Settings.RouteLineSettingsCollection[WaypointAttribute.Pace];
      var originalPace = legacy.Sessions[0].Settings.RouteLineSettingsCollection[WaypointAttribute.Pace];
      Assert.Equal(originalPace.MaskColor, pace.MaskColor);
      Assert.Equal(originalPace.ColorRange.StartValue, pace.ColorRange.StartValue);
      Assert.Equal(originalPace.ColorRange.Gradient.GetColor(0.3), pace.ColorRange.Gradient.GetColor(0.3));
      Assert.Equal(legacy.Settings.LapHistogramSettings.Count, reloaded.Settings.LapHistogramSettings.Count);
    }

    [Fact]
    public void SavingIsDeterministic()
    {
      var document = fileService.Open(TestDocuments.TestDataPath("synthetic-rawdata.qrt"));
      Assert.Equal(Save(document), Save(document));
    }

    [Fact]
    public void SavedPackageContainsVersionedJsonAndOriginalMapImage()
    {
      var document = fileService.Open(TestDocuments.TestDataPath("synthetic-rawdata.qrt"));
      using var stream = new MemoryStream(Save(document));
      using var archive = new System.IO.Compression.ZipArchive(stream);
      Assert.Equal(new[] { "document.json", "map.png" }, archive.Entries.Select(e => e.FullName).ToArray());
      using var reader = new StreamReader(archive.GetEntry("document.json").Open());
      var json = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
      Assert.Equal("QuickRoute.Document", json.RootElement.GetProperty("format").GetString());
      Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void UnsupportedFutureVersionIsRejected()
    {
      var document = fileService.Open(TestDocuments.TestDataPath("synthetic-rawdata.qrt"));
      var bytes = Save(document);
      using var input = new MemoryStream();
      input.Write(bytes);
      using (var archive = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Update, true))
      {
        var entry = archive.GetEntry("document.json");
        string text;
        using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("document.json").Open());
        writer.Write(text.Replace("\"version\": 1", "\"version\": 999"));
      }
      input.Position = 0;
      Assert.Throws<InvalidDataException>(() => QrtxDocumentStore.Load(input));
    }

    [Fact]
    public void CorruptLegacyFileIsRejected()
    {
      var bytes = File.ReadAllBytes(TestDocuments.TestDataPath("synthetic-rawdata.qrt"));
      Array.Resize(ref bytes, bytes.Length / 2);
      Assert.ThrowsAny<Exception>(() => LegacyQrtReader.Read(new MemoryStream(bytes), new SkiaMapImageService()));
    }

    private static void AssertMatchesLegacyExpectations(Document document)
    {
      Assert.Equal(320, document.Map.Width);
      Assert.Equal(240, document.Map.Height);
      Assert.Equal("Synthetic test document", document.Properties.Name);
      var session = Assert.Single(document.Sessions);
      Assert.Equal(60, session.Route.Segments[0].Waypoints.Count);
      Assert.Equal(new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc), session.Route.FirstWaypoint.Time);
      Assert.Equal(DateTimeKind.Utc, session.Route.FirstWaypoint.Time.Kind);
      Assert.Equal(3, session.Laps.Count);
      Assert.Equal("Control 1", session.Laps[1].Description);
      Assert.Equal("Example Runner", session.SessionInfo.Person.Name);
      Assert.Equal("Synthetic route", session.SessionInfo.Description);

      var handle = Assert.Single(session.Handles);
      Assert.Equal(ExpectedHandleX, handle.Location.X, 9);
      Assert.Equal(ExpectedHandleY, handle.Location.Y, 9);
      AssertMatrix(expectedInitialMatrix, session.InitialTransformationMatrix);
      AssertMatrix(expectedHandleMatrix, handle.TransformationMatrix);

      var adjusted = session.AdjustedRoute.Segments[0].Waypoints;
      Assert.Equal(ExpectedAdjustedFirstX, adjusted[0].Location.X, 9);
      Assert.Equal(ExpectedAdjustedFirstY, adjusted[0].Location.Y, 9);
      Assert.Equal(ExpectedAdjustedLastX, adjusted[adjusted.Count - 1].Location.X, 9);
      Assert.Equal(ExpectedAdjustedLastY, adjusted[adjusted.Count - 1].Location.Y, 9);
      Assert.Equal(ExpectedDistance, session.Route.Segments[0].LastWaypoint.Attributes[WaypointAttribute.Distance].Value, 9);
    }

    private static void AssertMatrix(double[] expectedColumnPacked, GeneralMatrix matrix)
    {
      var actual = matrix.ColumnPackedCopy;
      Assert.Equal(expectedColumnPacked.Length, actual.Length);
      for (var i = 0; i < actual.Length; i++) Assert.Equal(expectedColumnPacked[i], actual[i], 9);
    }

    private static byte[] Save(Document document)
    {
      using var stream = new MemoryStream();
      QrtxDocumentStore.Save(document, stream);
      return stream.ToArray();
    }

    private static Document RoundTrip(Document document)
    {
      using var stream = new MemoryStream(Save(document));
      return QrtxDocumentStore.Load(stream, "round-trip" + QrtxDocumentStore.FileExtension);
    }
  }
}
