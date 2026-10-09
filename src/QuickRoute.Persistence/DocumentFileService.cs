using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Opens and saves QuickRoute documents. New documents are always saved in the .qrtx package format;
  /// legacy .qrt documents, QuickRoute JPEG images and QuickRoute 1.0 XML documents can be opened.
  /// </summary>
  public sealed class DocumentFileService
  {
    private readonly IMapImageService mapImageService;

    public DocumentFileService(IMapImageService mapImageService)
    {
      this.mapImageService = mapImageService ?? throw new ArgumentNullException(nameof(mapImageService));
    }

    public IMapImageService MapImageService => mapImageService;

    public static QuickRouteFileFormat DetectFormat(string fileName)
    {
      using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      return DetectFormat(stream);
    }

    public static QuickRouteFileFormat DetectFormat(Stream stream)
    {
      var start = stream.Position;
      var buffer = new byte[300];
      var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
      stream.Position = start;
      var data = new ReadOnlySpan<byte>(buffer, 0, length);

      if (QrtxDocumentStore.HasPackageHeader(data)) return QuickRouteFileFormat.Qrtx;
      if (LegacyQrtReader.HasLegacyHeader(data)) return QuickRouteFileFormat.Qrt;
      if (length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return QuickRouteFileFormat.Jpeg;
      var text = System.Text.Encoding.UTF8.GetString(data);
      if (text.Contains("<QuickRoute>") && text.Contains("<Route>")) return QuickRouteFileFormat.Xml;
      return QuickRouteFileFormat.Unknown;
    }

    /// <summary>
    /// Opens a document. Returns null if the file is a JPEG image without QuickRoute data.
    /// </summary>
    public Document Open(string fileName, DocumentSettings defaultSettings = null)
    {
      using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      switch (DetectFormat(stream))
      {
        case QuickRouteFileFormat.Qrtx:
          return QrtxDocumentStore.Load(stream, fileName);
        case QuickRouteFileFormat.Qrt:
          return LegacyQrtReader.Read(stream, mapImageService, fileName);
        case QuickRouteFileFormat.Jpeg:
          return OpenFromJpeg(stream, fileName, defaultSettings ?? new DocumentSettings());
        case QuickRouteFileFormat.Xml:
          return OpenFromXml(stream, fileName, defaultSettings ?? new DocumentSettings());
        default:
          throw new InvalidDataException("The file is not a supported QuickRoute2 or QuickRoute document.");
      }
    }

    public void Save(Document document, string fileName)
    {
      QrtxDocumentStore.Save(document, fileName);
      document.FileName = fileName;
      document.FileFormat = QuickRouteFileFormat.Qrtx;
    }

    private Document OpenFromJpeg(Stream stream, string fileName, DocumentSettings settings)
    {
      var data = new byte[stream.Length];
      stream.ReadExactly(data, 0, data.Length);
      var ed = QuickRouteJpegExtensionData.FromStream(new MemoryStream(data));
      if (ed == null) return null;
      var document = new Document(mapImageService.CreateMap(data, fileName), settings) { Sessions = ed.Sessions };
      if (document.Sessions.Count > 0) document.ProjectionOrigin = document.Sessions[0].ProjectionOrigin;
      document.FileName = fileName;
      document.FileFormat = QuickRouteFileFormat.Jpeg;
      document.Initialize();
      return document;
    }

    /// <summary>
    /// Opens a document stored in the QuickRoute 1.0 XML file format. Documents can't be saved in this format.
    /// </summary>
    private Document OpenFromXml(Stream stream, string fileName, DocumentSettings settings)
    {
      var rs = new RouteSegment();
      var handles = new HandleCollection();
      Map map;
      var handleMarker = new DocumentSettings().DefaultSessionSettings.MarkerDrawers[MarkerType.Handle];

      using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true }))
      {
        reader.MoveToContent();
        reader.ReadStartElement("QuickRoute");
        reader.ReadStartElement("Route");
        while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
        {
          while (reader.NodeType != XmlNodeType.Element) reader.Read();
          var t = new Waypoint
          {
            Time = DateTime.Parse(reader.GetAttribute("time"), CultureInfo.InvariantCulture),
            LongLat = new LongLat(
              double.Parse(reader.GetAttribute("longitude"), CultureInfo.InvariantCulture),
              double.Parse(reader.GetAttribute("latitude"), CultureInfo.InvariantCulture)),
            Altitude = double.Parse(reader.GetAttribute("altitude"), CultureInfo.InvariantCulture),
            HeartRate = int.Parse(reader.GetAttribute("heartRate"), CultureInfo.InvariantCulture)
          };
          rs.Waypoints.Add(t);
        }
        reader.ReadEndElement();

        reader.ReadStartElement("Markers");
        while (reader.Name == "Handle")
        {
          reader.Read();
          var h = new Handle
          {
            ParameterizedLocation = new ParameterizedLocation(0, double.Parse(reader.GetAttribute("value"), CultureInfo.InvariantCulture))
          };
          reader.Read();
          var x = double.Parse(reader.GetAttribute("x"), CultureInfo.InvariantCulture);
          var y = double.Parse(reader.GetAttribute("y"), CultureInfo.InvariantCulture);
          h.Location = new PointD(x, y);
          reader.Read();
          h.TransformationMatrix = new GeneralMatrix(3, 3);
          h.MarkerDrawer = handleMarker;
          for (var row = 0; row < 3; row++)
          {
            for (var col = 0; col < 3; col++)
            {
              reader.Read();
              h.TransformationMatrix.SetElement(row, col, double.Parse(reader.GetAttribute("value"), CultureInfo.InvariantCulture));
            }
          }
          reader.Read();
          reader.ReadEndElement();
          reader.ReadEndElement();
          handles.Add(h);
        }
        reader.ReadEndElement();
        map = mapImageService.CreateMap(Convert.FromBase64String(reader.ReadElementContentAsString()), fileName);
      }

      var document = new Document(map, new Route(new List<RouteSegment> { rs }), new LapCollection(), null, settings);
      foreach (var h in handles)
      {
        document.Sessions[0].AddHandle(h);
      }
      document.FileName = fileName;
      document.FileFormat = QuickRouteFileFormat.Xml;
      document.Initialize();
      Document.UpdateDocumentToCurrentVersion(document);
      return document;
    }
  }
}
