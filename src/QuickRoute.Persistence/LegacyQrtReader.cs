using System;
using System.Collections.Generic;
using System.Drawing;
using System.Formats.Nrbf;
using System.IO;
using System.Runtime.Serialization;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Reads documents saved by QuickRoute 2.x (.qrt, .NET binary serialization format) without instantiating any serialized types.
  /// The record graph is decoded with <see cref="NrbfDecoder"/> and known members are mapped explicitly into the document model.
  /// </summary>
  public static class LegacyQrtReader
  {
    private const string Ns = "QuickRoute.BusinessEntities.";

    private static readonly byte[] header =
      { 0x00, 0x01, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public static bool HasLegacyHeader(ReadOnlySpan<byte> data)
    {
      return data.Length >= header.Length && data.Slice(0, header.Length).SequenceEqual(header);
    }

    public static Document Read(Stream stream, IMapImageService mapImageService, string fileName = null)
    {
      if (mapImageService == null) throw new ArgumentNullException(nameof(mapImageService));
      ClassRecord root;
      try
      {
        root = NrbfDecoder.Decode(stream, new PayloadOptions(), leaveOpen: true) as ClassRecord;
      }
      catch (Exception ex) when (ex is SerializationException || ex is NotSupportedException || ex is EndOfStreamException || ex is FormatException)
      {
        throw new InvalidDataException("The file is not a valid QuickRoute document.", ex);
      }
      try
      {
        if (root == null || root.TypeName.FullName != Ns + "Document")
          throw new InvalidDataException("The file is not a valid QuickRoute document.");
        var document = ReadDocument(root, mapImageService, fileName);
        document.FileName = fileName;
        document.FileFormat = QuickRouteFileFormat.Qrt;
        document.Initialize();
        Document.UpdateDocumentToCurrentVersion(document);
        return document;
      }
      catch (Exception ex) when (ex is InvalidCastException || ex is KeyNotFoundException || ex is NullReferenceException || ex is ArgumentException)
      {
        throw new InvalidDataException("The QuickRoute document contains unexpected data.", ex);
      }
    }

    private static Document ReadDocument(ClassRecord record, IMapImageService mapImageService, string fileName)
    {
      var map = ReadMap(Class(record, "map"), mapImageService, fileName);
      var settings = ReadDocumentSettings(Class(record, "settings"));
      var sessions = new SessionCollection();
      foreach (var sessionRecord in ReadList(Class(Class(record, "sessions"), "sessions")))
      {
        sessions.Add(ReadSession(sessionRecord, map.Size));
      }
      DocumentProperties properties = null;
      var propertiesRecord = OptionalClass(record, "properties");
      if (propertiesRecord != null) properties = new DocumentProperties { Name = OptionalString(propertiesRecord, "<Name>k__BackingField") };
      return Document.FromParts(map, settings, sessions, ReadLongLat(OptionalClass(record, "projectionOrigin")), properties);
    }

    private static Map ReadMap(ClassRecord record, IMapImageService mapImageService, string fileName)
    {
      var source = OptionalString(record, "source");
      var sourceType = (MapSourceType)EnumValue(record, "sourceType");
      var storageType = (MapStorageType)EnumValue(record, "storageType");
      byte[] data;
      if (storageType == MapStorageType.Inline)
      {
        if (record.HasMember("rawData") && record.GetRawValue("rawData") is SZArrayRecord<byte> rawData)
        {
          // QuickRoute 2.3 and later: the original image file bytes
          data = rawData.GetArray();
        }
        else
        {
          // QuickRoute 2.2 and earlier: a serialized System.Drawing.Bitmap holding PNG data in its Data member
          var bitmap = Class(record, "image");
          if (bitmap.TypeName.FullName != "System.Drawing.Bitmap") throw new InvalidDataException("Unsupported map image storage.");
          data = ((SZArrayRecord<byte>)bitmap.GetRawValue("Data")).GetArray();
        }
      }
      else
      {
        if (sourceType != MapSourceType.FileSystem || string.IsNullOrEmpty(source))
          throw new NotSupportedException("Map images referenced by URL are not supported.");
        var path = source;
        if (!File.Exists(path) && fileName != null)
        {
          // the referenced map may have been moved together with the document
          var candidate = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? "", Path.GetFileName(source.Replace('\\', '/')));
          if (File.Exists(candidate)) path = candidate;
        }
        if (!File.Exists(path)) throw new FileNotFoundException("The referenced map image could not be found.", source);
        data = File.ReadAllBytes(path);
      }
      var map = mapImageService.CreateMap(data, source, sourceType);
      map.StorageType = storageType;
      return map;
    }

    private static Session ReadSession(ClassRecord record, Size mapSize)
    {
      var routeRecord = Class(record, "route");
      var segments = new List<RouteSegment>();
      foreach (var segmentRecord in ReadList(Class(routeRecord, "segments")))
      {
        var segment = new RouteSegment();
        foreach (var w in ReadList(Class(segmentRecord, "waypoints")))
        {
          var longLat = ReadLongLat(Class(w, "longLat"));
          var mapReadingState = w.GetRawValue("mapReadingState") is ClassRecord mrs ? (MapReadingState?)Convert.ToInt32(mrs.GetRawValue("value__")) : null;
          segment.Waypoints.Add(new Waypoint(ReadTime(w, "time"), longLat, OptionalDouble(w, "altitude"), OptionalDouble(w, "heartRate"),
            mapReadingState, OptionalDouble(w, "cadence"), OptionalDouble(w, "power")));
        }
        segments.Add(segment);
      }

      var laps = new LapCollection();
      foreach (var lapRecord in ReadOrderedBag(Class(record, "laps"), "laps"))
      {
        laps.Add(new Lap(ReadTime(lapRecord, "time"), (LapType)EnumValue(lapRecord, "lapType"), OptionalString(lapRecord, "description")));
      }

      var handles = new List<Handle>();
      foreach (var handleRecord in ReadOrderedBag(Class(record, "handles"), "handles"))
      {
        var pl = Class(handleRecord, "parameterizedLocation");
        var location = Class(handleRecord, "location");
        handles.Add(new Handle(
          new ParameterizedLocation(pl.GetInt32("segmentIndex"), pl.GetDouble("value")),
          null,
          new PointD(location.GetDouble("x"), location.GetDouble("y")),
          ReadMatrix(OptionalClass(handleRecord, "transformationMatrix")),
          ReadMarker(OptionalClass(handleRecord, "markerDrawer")),
          (Handle.HandleType)EnumValue(handleRecord, "type")));
      }

      SessionInfo sessionInfo = new SessionInfo();
      var infoRecord = OptionalClass(record, "sessionInfo");
      if (infoRecord != null)
      {
        var personRecord = OptionalClass(infoRecord, "person");
        sessionInfo.Description = OptionalString(infoRecord, "description") ?? "";
        if (personRecord != null)
        {
          sessionInfo.Person = new SessionPerson
          {
            Name = OptionalString(personRecord, "<Name>k__BackingField"),
            Club = OptionalString(personRecord, "<Club>k__BackingField"),
            Id = personRecord.HasMember("<Id>k__BackingField") ? Convert.ToUInt32(personRecord.GetRawValue("<Id>k__BackingField")) : 0
          };
        }
      }

      return SessionFactory.Create(new Route(segments), laps, mapSize, ReadMatrix(OptionalClass(record, "initialTransformationMatrix")),
        ReadLongLat(OptionalClass(record, "projectionOrigin")), ReadSessionSettings(Class(record, "settings")), handles, sessionInfo);
    }

    private static DocumentSettings ReadDocumentSettings(ClassRecord record)
    {
      var settings = new DocumentSettings();
      var sessionSettings = OptionalClass(record, "defaultSessionSettings");
      if (sessionSettings != null) settings.DefaultSessionSettings = ReadSessionSettings(sessionSettings);
      var sliders = ReadDictionary(OptionalClass(record, "colorRangeIntervalSliderSettings"));
      if (sliders != null)
      {
        settings.ColorRangeIntervalSliderSettings = new Dictionary<WaypointAttribute, ColorRangeIntervalSliderSettings>();
        foreach (var (key, value) in sliders)
          settings.ColorRangeIntervalSliderSettings[(WaypointAttribute)EnumRecordValue(key)] =
            new ColorRangeIntervalSliderSettings(((ClassRecord)value).GetDouble("minValue"), ((ClassRecord)value).GetDouble("maxValue"));
      }
      var histograms = ReadDictionary(OptionalClass(record, "lapHistogramSettings"));
      if (histograms != null)
      {
        settings.LapHistogramSettings = new Dictionary<WaypointAttribute, LapHistogramSettings>();
        foreach (var (key, value) in histograms)
          settings.LapHistogramSettings[(WaypointAttribute)EnumRecordValue(key)] = new LapHistogramSettings(((ClassRecord)value).GetDouble("binWidth"));
      }
      return settings;
    }

    private static SessionSettings ReadSessionSettings(ClassRecord record)
    {
      var settings = new SessionSettings();
      var smoothing = ReadDictionary(OptionalClass(record, "smoothingIntervals"));
      if (smoothing != null)
      {
        settings.SmoothingIntervals = new Dictionary<WaypointAttribute, Interval>();
        foreach (var (key, value) in smoothing)
          settings.SmoothingIntervals[(WaypointAttribute)EnumRecordValue(key)] = ReadInterval((ClassRecord)value);
      }
      else if (OptionalClass(record, "speedSmoothingInterval") is ClassRecord speedSmoothing)
      {
        // documents from before smoothing intervals were introduced only contain the speed smoothing interval
        var intervals = SessionSettings.CreateDefaultSmoothingIntervals();
        intervals[WaypointAttribute.Speed] = ReadInterval(speedSmoothing);
        intervals[WaypointAttribute.Pace] = ReadInterval(speedSmoothing);
        settings.SmoothingIntervals = intervals;
      }

      var routeLines = ReadDictionary(OptionalClass(record, "routeLineSettingsCollection"));
      if (routeLines != null)
      {
        settings.RouteLineSettingsCollection = new Dictionary<WaypointAttribute, RouteLineSettings>();
        foreach (var (key, value) in routeLines)
          settings.RouteLineSettingsCollection[(WaypointAttribute)EnumRecordValue(key)] = ReadRouteLineSettings((ClassRecord)value);
      }

      var markers = ReadDictionary(OptionalClass(record, "markerDrawers"));
      if (markers != null)
      {
        settings.MarkerDrawers = new Dictionary<MarkerType, IMarkerDrawer>();
        foreach (var (key, value) in markers)
        {
          var marker = ReadMarker(value as ClassRecord);
          if (marker != null) settings.MarkerDrawers[(MarkerType)EnumRecordValue(key)] = marker;
        }
      }
      if (record.HasMember("circleTimeRadius")) settings.CircleTimeRadius = record.GetDouble("circleTimeRadius");
      return settings;
    }

    private static RouteLineSettings ReadRouteLineSettings(ClassRecord record)
    {
      var colorRangeRecord = Class(record, "colorRange");
      var gradientRecord = Class(colorRangeRecord, "gradient");
      var colors = ((SZArrayRecord<int>)gradientRecord.GetRawValue("colors")).GetArray();
      var locations = ((SZArrayRecord<double>)gradientRecord.GetRawValue("locations")).GetArray();
      var entries = new List<GradientColorEntry>();
      for (var i = 0; i < Math.Min(colors.Length, locations.Length); i++) entries.Add(new GradientColorEntry(Color.FromArgb(colors[i]), locations[i]));
      var colorRange = new ColorRange(Gradient.FromEntries(OptionalString(gradientRecord, "name"), entries),
        colorRangeRecord.GetDouble("startValue"), colorRangeRecord.GetDouble("endValue"));
      var rls = new RouteLineSettings(colorRange, record.GetDouble("width"), Color.FromArgb(record.GetInt32("maskColor")),
        record.GetDouble("maskWidth"), record.GetBoolean("maskVisible"), record.GetDouble("alphaAdjustment"));
      if (record.HasMember("monochromeColor"))
      {
        rls.MonochromeColor = Color.FromArgb(record.GetInt32("monochromeColor"));
        rls.MonochromeWidth = record.GetDouble("monochromeWidth");
      }
      else
      {
        rls.MonochromeColor = Color.FromArgb(160, Color.Red);
        rls.MonochromeWidth = 3;
      }
      return rls;
    }

    private static IMarkerDrawer ReadMarker(ClassRecord record)
    {
      if (record == null) return null;
      switch (record.TypeName.FullName)
      {
        case Ns + "DiscDrawer":
          return new DiscDrawer(Color.FromArgb(record.GetInt32("color")), record.GetDouble("radius"));
        case Ns + "DiscAndCircleDrawer":
          return new DiscAndCircleDrawer(Color.FromArgb(record.GetInt32("innerColor")), record.GetDouble("innerRadius"),
            Color.FromArgb(record.GetInt32("outerColor")), record.GetDouble("outerRadius"), record.GetDouble("outerLineWidth"));
        case Ns + "CircleDrawer":
          return new CircleDrawer(Color.FromArgb(record.GetInt32("color")), record.GetDouble("radius"), record.GetDouble("lineWidth"));
        default:
          return null;
      }
    }

    private static GeneralMatrix ReadMatrix(ClassRecord record)
    {
      if (record == null) return null;
      var m = record.GetInt32("m");
      var n = record.GetInt32("n");
      var rowArray = (ArrayRecord[])((ArrayRecord)record.GetRawValue("a")).GetArray(typeof(double[][]));
      if (rowArray.Length != m) throw new InvalidDataException("Invalid matrix data.");
      var rows = new double[m][];
      for (var i = 0; i < m; i++)
      {
        rows[i] = ((SZArrayRecord<double>)rowArray[i]).GetArray();
        if (rows[i].Length != n) throw new InvalidDataException("Invalid matrix data.");
      }
      return new GeneralMatrix(rows, m, n);
    }

    private static Interval ReadInterval(ClassRecord record)
    {
      return new Interval(record.GetDouble("start"), record.GetDouble("end"));
    }

    private static LongLat ReadLongLat(ClassRecord record)
    {
      return record == null ? null : new LongLat(record.GetDouble("longitude"), record.GetDouble("latitude"));
    }

    private static DateTime ReadTime(ClassRecord record, string member)
    {
      var time = record.GetDateTime(member);
      // times are stored in UTC by QuickRoute 2.x
      return time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time;
    }

    #region Record helpers

    private static IEnumerable<ClassRecord> ReadList(ClassRecord list)
    {
      var size = list.GetInt32("_size");
      var items = ((SZArrayRecord<SerializationRecord>)list.GetRawValue("_items")).GetArray();
      for (var i = 0; i < size; i++) yield return (ClassRecord)items[i];
    }

    /// <summary>
    /// Reads the items of a PowerCollections OrderedBag (a red-black tree) held by the specified member, in sorted order.
    /// </summary>
    private static List<ClassRecord> ReadOrderedBag(ClassRecord owner, string member)
    {
      var result = new List<ClassRecord>();
      var tree = Class(Class(owner, member), "tree");
      var stack = new Stack<ClassRecord>();
      var node = OptionalClass(tree, "root");
      while (node != null || stack.Count > 0)
      {
        while (node != null)
        {
          stack.Push(node);
          node = OptionalClass(node, "left");
        }
        node = stack.Pop();
        result.Add(Class(node, "item"));
        node = OptionalClass(node, "right");
      }
      return result;
    }

    private static List<(ClassRecord Key, SerializationRecord Value)> ReadDictionary(ClassRecord dictionary)
    {
      if (dictionary == null) return null;
      var result = new List<(ClassRecord, SerializationRecord)>();
      if (!dictionary.HasMember("KeyValuePairs") || !(dictionary.GetRawValue("KeyValuePairs") is SZArrayRecord<SerializationRecord> pairs)) return result;
      foreach (var pair in pairs.GetArray())
      {
        if (pair is ClassRecord pairRecord) result.Add(((ClassRecord)pairRecord.GetRawValue("key"), pairRecord.GetRawValue("value") as SerializationRecord));
      }
      return result;
    }

    private static ClassRecord Class(ClassRecord record, string member)
    {
      return OptionalClass(record, member) ?? throw new InvalidDataException("Missing data: " + member);
    }

    private static ClassRecord OptionalClass(ClassRecord record, string member)
    {
      return record.HasMember(member) ? record.GetRawValue(member) as ClassRecord : null;
    }

    private static string OptionalString(ClassRecord record, string member)
    {
      return record.HasMember(member) ? record.GetRawValue(member) as string : null;
    }

    private static double? OptionalDouble(ClassRecord record, string member)
    {
      if (!record.HasMember(member)) return null;
      var value = record.GetRawValue(member);
      return value == null ? (double?)null : Convert.ToDouble(value);
    }

    private static int EnumValue(ClassRecord record, string member)
    {
      return EnumRecordValue(Class(record, member));
    }

    private static int EnumRecordValue(ClassRecord enumRecord)
    {
      return Convert.ToInt32(enumRecord.GetRawValue("value__"));
    }

    #endregion
  }
}
