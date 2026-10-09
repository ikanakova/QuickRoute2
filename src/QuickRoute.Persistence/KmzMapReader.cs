using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Reads the first ground overlay (map image and position) of a KMZ file.
  /// </summary>
  public static class KmzMapReader
  {
    private const long MaxEntryLength = 1024L * 1024 * 1024;

    public sealed class KmzMap
    {
      public Map Map { get; set; }

      public Transformation Transformation { get; set; }
    }

    public static KmzMap Read(Stream stream, IMapImageService mapImageService)
    {
      using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
      string imageFileName = null;
      LongLatBox longLatBox = null;
      foreach (var entry in archive.Entries)
      {
        if (!string.Equals(Path.GetExtension(entry.FullName), ".kml", StringComparison.OrdinalIgnoreCase)) continue;
        using var kmlStream = entry.Open();
        ParseKml(kmlStream, out imageFileName, out longLatBox);
        if (imageFileName != null) break;
      }
      if (imageFileName == null) return null;

      var imageEntry = archive.GetEntry(imageFileName.Replace('\\', '/'));
      if (imageEntry == null || imageEntry.Length > MaxEntryLength) return null;
      byte[] data;
      using (var imageStream = imageEntry.Open())
      using (var memory = new MemoryStream())
      {
        imageStream.CopyTo(memory);
        data = memory.ToArray();
      }

      var extensionData = QuickRouteJpegExtensionData.FromStream(new MemoryStream(data, false));
      var map = mapImageService.CreateMap(data);
      if (extensionData?.Sessions != null && extensionData.Sessions.Count > 0)
      {
        // QuickRoute image: use the embedded transformation
        return new KmzMap { Map = map, Transformation = extensionData.Sessions.CalculateAverageTransformation() };
      }
      return new KmzMap { Map = map, Transformation = longLatBox == null ? new Transformation() : new Transformation(longLatBox, map.Size) };
    }

    private static void ParseKml(Stream stream, out string imageFileName, out LongLatBox longLatBox)
    {
      imageFileName = null;
      longLatBox = null;
      var doc = new XmlDocument { XmlResolver = null };
      using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
      {
        doc.Load(reader);
      }
      var nsmgr = new XmlNamespaceManager(doc.NameTable);
      var ns = "";
      if (doc.DocumentElement != null && !string.IsNullOrEmpty(doc.DocumentElement.NamespaceURI))
      {
        nsmgr.AddNamespace("ns", doc.DocumentElement.NamespaceURI);
        ns = "ns:";
      }
      var groundOverlay = doc.SelectSingleNode(string.Format("//{0}GroundOverlay", ns), nsmgr);
      if (groundOverlay == null) return;
      imageFileName = groundOverlay.SelectSingleNode(string.Format("{0}Icon/{0}href", ns), nsmgr)?.InnerText;
      var box = groundOverlay.SelectSingleNode(string.Format("{0}LatLonBox", ns), nsmgr);
      if (box == null) return;
      double Value(string name) =>
        Convert.ToDouble(box.SelectSingleNode(string.Format("{0}{1}", ns, name), nsmgr)?.InnerText ?? "0", CultureInfo.InvariantCulture);
      longLatBox = new LongLatBox
      {
        North = Value("north"),
        South = Value("south"),
        West = Value("west"),
        East = Value("east"),
        Rotation = Value("rotation") / 180.0 * Math.PI
      };
    }
  }
}
