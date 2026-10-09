using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Explicit JSON mapping of the QuickRoute document model. Only known data is written and read; no type information is stored.
  /// </summary>
  internal static class DocumentJson
  {
    public const string FormatName = "QuickRoute.Document";
    public const int CurrentVersion = 1;

    #region Writing

    public static void Write(Utf8JsonWriter w, Document document, string mapEntryName)
    {
      w.WriteStartObject();
      w.WriteString("format", FormatName);
      w.WriteNumber("version", CurrentVersion);
      w.WritePropertyName("map");
      w.WriteStartObject();
      w.WriteString("entry", mapEntryName);
      w.WriteNumber("width", document.Map.Width);
      w.WriteNumber("height", document.Map.Height);
      if (document.Map.Source != null) w.WriteString("source", document.Map.Source);
      w.WriteString("sourceType", document.Map.SourceType.ToString());
      w.WriteEndObject();
      if (document.StoredProperties != null)
      {
        w.WritePropertyName("properties");
        w.WriteStartObject();
        WriteNullableString(w, "name", document.StoredProperties.Name);
        w.WriteEndObject();
      }
      WriteLongLat(w, "projectionOrigin", document.ProjectionOrigin);
      w.WritePropertyName("settings");
      WriteDocumentSettings(w, document.Settings);
      w.WritePropertyName("sessions");
      w.WriteStartArray();
      foreach (var session in document.Sessions) WriteSession(w, session);
      w.WriteEndArray();
      w.WriteEndObject();
    }

    private static void WriteSession(Utf8JsonWriter w, Session session)
    {
      w.WriteStartObject();
      w.WritePropertyName("info");
      w.WriteStartObject();
      var person = session.SessionInfo.Person ?? new SessionPerson();
      WriteNullableString(w, "personName", person.Name);
      WriteNullableString(w, "personClub", person.Club);
      w.WriteNumber("personId", person.Id);
      WriteNullableString(w, "description", session.SessionInfo.Description);
      w.WriteEndObject();
      WriteLongLat(w, "projectionOrigin", session.ProjectionOrigin);
      WriteMatrix(w, "initialTransformationMatrix", session.InitialTransformationMatrix);

      w.WritePropertyName("route");
      w.WriteStartObject();
      w.WritePropertyName("segments");
      w.WriteStartArray();
      foreach (var segment in session.Route.Segments)
      {
        w.WriteStartObject();
        w.WritePropertyName("waypoints");
        w.WriteStartArray();
        foreach (var waypoint in segment.Waypoints) WriteWaypoint(w, waypoint);
        w.WriteEndArray();
        w.WriteEndObject();
      }
      w.WriteEndArray();
      w.WriteEndObject();

      w.WritePropertyName("laps");
      w.WriteStartArray();
      foreach (var lap in session.Laps)
      {
        w.WriteStartObject();
        w.WriteString("time", FormatTime(lap.Time));
        w.WriteString("type", lap.LapType.ToString());
        if (lap.Description != null) w.WriteString("description", lap.Description);
        w.WriteEndObject();
      }
      w.WriteEndArray();

      w.WritePropertyName("handles");
      w.WriteStartArray();
      foreach (var handle in session.AllHandles())
      {
        w.WriteStartObject();
        w.WriteNumber("segmentIndex", handle.ParameterizedLocation.SegmentIndex);
        w.WriteNumber("value", handle.ParameterizedLocation.Value);
        w.WriteNumber("x", handle.Location.X);
        w.WriteNumber("y", handle.Location.Y);
        w.WriteString("type", handle.Type.ToString());
        WriteMatrix(w, "transformationMatrix", handle.TransformationMatrix);
        if (handle.MarkerDrawer != null)
        {
          w.WritePropertyName("marker");
          WriteMarker(w, handle.MarkerDrawer);
        }
        w.WriteEndObject();
      }
      w.WriteEndArray();

      w.WritePropertyName("settings");
      WriteSessionSettings(w, session.Settings);
      w.WriteEndObject();
    }

    private static void WriteWaypoint(Utf8JsonWriter w, Waypoint waypoint)
    {
      w.WriteStartObject();
      w.WriteString("time", FormatTime(waypoint.Time));
      w.WriteNumber("lon", waypoint.LongLat.Longitude);
      w.WriteNumber("lat", waypoint.LongLat.Latitude);
      WriteNullableNumber(w, "altitude", waypoint.Altitude);
      WriteNullableNumber(w, "heartRate", waypoint.HeartRate);
      WriteNullableNumber(w, "cadence", waypoint.Cadence);
      WriteNullableNumber(w, "power", waypoint.Power);
      if (waypoint.MapReadingState.HasValue) w.WriteString("mapReadingState", waypoint.MapReadingState.Value.ToString());
      w.WriteEndObject();
    }

    private static void WriteDocumentSettings(Utf8JsonWriter w, DocumentSettings settings)
    {
      w.WriteStartObject();
      w.WritePropertyName("defaultSessionSettings");
      WriteSessionSettings(w, settings.DefaultSessionSettings);
      w.WritePropertyName("colorRangeIntervalSliderSettings");
      w.WriteStartObject();
      foreach (var item in Sorted(settings.ColorRangeIntervalSliderSettings))
      {
        w.WritePropertyName(item.Key.ToString());
        w.WriteStartObject();
        w.WriteNumber("min", item.Value.MinValue);
        w.WriteNumber("max", item.Value.MaxValue);
        w.WriteEndObject();
      }
      w.WriteEndObject();
      w.WritePropertyName("lapHistogramSettings");
      w.WriteStartObject();
      foreach (var item in Sorted(settings.LapHistogramSettings))
      {
        w.WritePropertyName(item.Key.ToString());
        w.WriteStartObject();
        w.WriteNumber("binWidth", item.Value.BinWidth);
        w.WriteEndObject();
      }
      w.WriteEndObject();
      w.WriteEndObject();
    }

    private static void WriteSessionSettings(Utf8JsonWriter w, SessionSettings settings)
    {
      w.WriteStartObject();
      w.WritePropertyName("smoothingIntervals");
      w.WriteStartObject();
      foreach (var item in Sorted(settings.SmoothingIntervals))
      {
        w.WritePropertyName(item.Key.ToString());
        w.WriteStartObject();
        w.WriteNumber("start", item.Value.Start);
        w.WriteNumber("end", item.Value.End);
        w.WriteEndObject();
      }
      w.WriteEndObject();
      w.WritePropertyName("routeLines");
      w.WriteStartObject();
      foreach (var item in Sorted(settings.RouteLineSettingsCollection))
      {
        w.WritePropertyName(item.Key.ToString());
        WriteRouteLineSettings(w, item.Value);
      }
      w.WriteEndObject();
      w.WritePropertyName("markers");
      w.WriteStartObject();
      foreach (var item in Sorted(settings.MarkerDrawers))
      {
        if (item.Value == null) continue;
        w.WritePropertyName(item.Key.ToString());
        WriteMarker(w, item.Value);
      }
      w.WriteEndObject();
      w.WriteNumber("circleTimeRadius", settings.CircleTimeRadius);
      w.WriteEndObject();
    }

    private static void WriteRouteLineSettings(Utf8JsonWriter w, RouteLineSettings rls)
    {
      w.WriteStartObject();
      w.WritePropertyName("colorRange");
      w.WriteStartObject();
      w.WriteNumber("start", rls.ColorRange.StartValue);
      w.WriteNumber("end", rls.ColorRange.EndValue);
      w.WritePropertyName("gradient");
      w.WriteStartObject();
      WriteNullableString(w, "name", rls.ColorRange.Gradient.Name);
      w.WritePropertyName("entries");
      w.WriteStartArray();
      foreach (var entry in rls.ColorRange.Gradient.ColorEntries)
      {
        w.WriteStartObject();
        w.WriteString("color", FormatColor(entry.Color));
        w.WriteNumber("location", entry.Location);
        w.WriteEndObject();
      }
      w.WriteEndArray();
      w.WriteEndObject();
      w.WriteEndObject();
      w.WriteNumber("width", rls.Width);
      w.WriteString("maskColor", FormatColor(rls.MaskColor));
      w.WriteNumber("maskWidth", rls.MaskWidth);
      w.WriteBoolean("maskVisible", rls.MaskVisible);
      w.WriteNumber("alphaAdjustment", rls.AlphaAdjustment);
      w.WriteString("monochromeColor", FormatColor(rls.MonochromeColor));
      w.WriteNumber("monochromeWidth", rls.MonochromeWidth);
      w.WriteEndObject();
    }

    private static void WriteMarker(Utf8JsonWriter w, IMarkerDrawer marker)
    {
      w.WriteStartObject();
      switch (marker)
      {
        case DiscDrawer disc:
          w.WriteString("kind", "disc");
          w.WriteString("color", FormatColor(disc.Color));
          w.WriteNumber("radius", disc.Radius);
          break;
        case DiscAndCircleDrawer discAndCircle:
          w.WriteString("kind", "discAndCircle");
          w.WriteString("innerColor", FormatColor(discAndCircle.InnerColor));
          w.WriteNumber("innerRadius", discAndCircle.InnerRadius);
          w.WriteString("outerColor", FormatColor(discAndCircle.OuterColor));
          w.WriteNumber("outerRadius", discAndCircle.OuterRadius);
          w.WriteNumber("outerLineWidth", discAndCircle.OuterLineWidth);
          break;
        case CircleDrawer circle:
          w.WriteString("kind", "circle");
          w.WriteString("color", FormatColor(circle.Color));
          w.WriteNumber("radius", circle.Radius);
          w.WriteNumber("lineWidth", circle.LineWidth);
          break;
        default:
          throw new NotSupportedException("Unknown marker type: " + marker.GetType().Name);
      }
      w.WriteEndObject();
    }

    private static void WriteMatrix(Utf8JsonWriter w, string name, GeneralMatrix matrix)
    {
      if (matrix == null) return;
      w.WritePropertyName(name);
      w.WriteStartArray();
      for (var row = 0; row < matrix.RowDimension; row++)
      {
        w.WriteStartArray();
        for (var column = 0; column < matrix.ColumnDimension; column++) w.WriteNumberValue(matrix.GetElement(row, column));
        w.WriteEndArray();
      }
      w.WriteEndArray();
    }

    private static void WriteLongLat(Utf8JsonWriter w, string name, LongLat longLat)
    {
      if (longLat == null) return;
      w.WritePropertyName(name);
      w.WriteStartObject();
      w.WriteNumber("lon", longLat.Longitude);
      w.WriteNumber("lat", longLat.Latitude);
      w.WriteEndObject();
    }

    private static void WriteNullableNumber(Utf8JsonWriter w, string name, double? value)
    {
      if (value.HasValue) w.WriteNumber(name, value.Value);
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string value)
    {
      if (value != null) w.WriteString(name, value);
    }

    private static IEnumerable<KeyValuePair<TKey, TValue>> Sorted<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : Enum
    {
      var items = new List<KeyValuePair<TKey, TValue>>(dictionary ?? new Dictionary<TKey, TValue>());
      items.Sort((a, b) => Convert.ToInt32(a.Key).CompareTo(Convert.ToInt32(b.Key)));
      return items;
    }

    #endregion

    #region Reading

    public static Document Read(JsonElement root, Func<string, Size, string, MapSourceType, Map> mapFactory)
    {
      if (!root.TryGetProperty("format", out var format) || format.GetString() != FormatName)
        throw new InvalidDataException("The file is not a QuickRoute2 document.");
      var version = root.GetProperty("version").GetInt32();
      if (version < 1 || version > CurrentVersion)
        throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, "Unsupported QuickRoute2 document version {0}.", version));

      var mapElement = root.GetProperty("map");
      var mapSize = new Size(mapElement.GetProperty("width").GetInt32(), mapElement.GetProperty("height").GetInt32());
      var map = mapFactory(
        mapElement.GetProperty("entry").GetString(),
        mapSize,
        OptionalString(mapElement, "source"),
        ParseEnum(OptionalString(mapElement, "sourceType"), MapSourceType.FileSystem));

      DocumentProperties properties = null;
      if (root.TryGetProperty("properties", out var propertiesElement))
      {
        properties = new DocumentProperties { Name = OptionalString(propertiesElement, "name") };
      }

      var settings = ReadDocumentSettings(root.GetProperty("settings"));
      var sessions = new SessionCollection();
      foreach (var sessionElement in root.GetProperty("sessions").EnumerateArray())
      {
        sessions.Add(ReadSession(sessionElement, mapSize));
      }
      return Document.FromParts(map, settings, sessions, ReadLongLat(root, "projectionOrigin"), properties);
    }

    private static Session ReadSession(JsonElement e, Size mapSize)
    {
      var segments = new List<RouteSegment>();
      foreach (var segmentElement in e.GetProperty("route").GetProperty("segments").EnumerateArray())
      {
        var segment = new RouteSegment();
        foreach (var waypointElement in segmentElement.GetProperty("waypoints").EnumerateArray())
        {
          segment.Waypoints.Add(new Waypoint(
            ParseTime(waypointElement.GetProperty("time").GetString()),
            new LongLat(waypointElement.GetProperty("lon").GetDouble(), waypointElement.GetProperty("lat").GetDouble()),
            OptionalDouble(waypointElement, "altitude"),
            OptionalDouble(waypointElement, "heartRate"),
            waypointElement.TryGetProperty("mapReadingState", out var mrs) ? ParseEnum<MapReadingState>(mrs.GetString(), MapReadingState.NotReading) : (MapReadingState?)null,
            OptionalDouble(waypointElement, "cadence"),
            OptionalDouble(waypointElement, "power")));
        }
        segments.Add(segment);
      }

      var laps = new LapCollection();
      foreach (var lapElement in e.GetProperty("laps").EnumerateArray())
      {
        laps.Add(new Lap(ParseTime(lapElement.GetProperty("time").GetString()),
          ParseEnum(lapElement.GetProperty("type").GetString(), LapType.Lap),
          OptionalString(lapElement, "description")));
      }

      var settings = ReadSessionSettings(e.GetProperty("settings"));
      var handles = new List<Handle>();
      foreach (var handleElement in e.GetProperty("handles").EnumerateArray())
      {
        handles.Add(new Handle(
          new ParameterizedLocation(handleElement.GetProperty("segmentIndex").GetInt32(), handleElement.GetProperty("value").GetDouble()),
          null,
          new PointD(handleElement.GetProperty("x").GetDouble(), handleElement.GetProperty("y").GetDouble()),
          ReadMatrix(handleElement, "transformationMatrix"),
          handleElement.TryGetProperty("marker", out var marker) ? ReadMarker(marker) : null,
          ParseEnum(handleElement.GetProperty("type").GetString(), Handle.HandleType.Handle)));
      }

      var info = e.GetProperty("info");
      var sessionInfo = new SessionInfo
      {
        Person = new SessionPerson
        {
          Name = OptionalString(info, "personName"),
          Club = OptionalString(info, "personClub"),
          Id = info.TryGetProperty("personId", out var id) ? id.GetUInt32() : 0
        },
        Description = OptionalString(info, "description") ?? ""
      };

      return SessionFactory.Create(new Route(segments), laps, mapSize, ReadMatrix(e, "initialTransformationMatrix"),
        ReadLongLat(e, "projectionOrigin"), settings, handles, sessionInfo);
    }

    private static DocumentSettings ReadDocumentSettings(JsonElement e)
    {
      var settings = new DocumentSettings
      {
        DefaultSessionSettings = ReadSessionSettings(e.GetProperty("defaultSessionSettings")),
        ColorRangeIntervalSliderSettings = new Dictionary<WaypointAttribute, ColorRangeIntervalSliderSettings>(),
        LapHistogramSettings = new Dictionary<WaypointAttribute, LapHistogramSettings>()
      };
      foreach (var item in e.GetProperty("colorRangeIntervalSliderSettings").EnumerateObject())
      {
        if (Enum.TryParse(item.Name, out WaypointAttribute attribute))
          settings.ColorRangeIntervalSliderSettings[attribute] = new ColorRangeIntervalSliderSettings(item.Value.GetProperty("min").GetDouble(), item.Value.GetProperty("max").GetDouble());
      }
      foreach (var item in e.GetProperty("lapHistogramSettings").EnumerateObject())
      {
        if (Enum.TryParse(item.Name, out WaypointAttribute attribute))
          settings.LapHistogramSettings[attribute] = new LapHistogramSettings(item.Value.GetProperty("binWidth").GetDouble());
      }
      return settings;
    }

    private static SessionSettings ReadSessionSettings(JsonElement e)
    {
      var settings = new SessionSettings
      {
        SmoothingIntervals = new Dictionary<WaypointAttribute, Interval>(),
        RouteLineSettingsCollection = new Dictionary<WaypointAttribute, RouteLineSettings>(),
        MarkerDrawers = new Dictionary<MarkerType, IMarkerDrawer>(),
        CircleTimeRadius = e.GetProperty("circleTimeRadius").GetDouble()
      };
      foreach (var item in e.GetProperty("smoothingIntervals").EnumerateObject())
      {
        if (Enum.TryParse(item.Name, out WaypointAttribute attribute))
          settings.SmoothingIntervals[attribute] = new Interval(item.Value.GetProperty("start").GetDouble(), item.Value.GetProperty("end").GetDouble());
      }
      foreach (var item in e.GetProperty("routeLines").EnumerateObject())
      {
        if (Enum.TryParse(item.Name, out WaypointAttribute attribute))
          settings.RouteLineSettingsCollection[attribute] = ReadRouteLineSettings(item.Value);
      }
      foreach (var item in e.GetProperty("markers").EnumerateObject())
      {
        if (Enum.TryParse(item.Name, out MarkerType markerType))
          settings.MarkerDrawers[markerType] = ReadMarker(item.Value);
      }
      return settings;
    }

    private static RouteLineSettings ReadRouteLineSettings(JsonElement e)
    {
      var colorRangeElement = e.GetProperty("colorRange");
      var gradientElement = colorRangeElement.GetProperty("gradient");
      var entries = new List<GradientColorEntry>();
      foreach (var entry in gradientElement.GetProperty("entries").EnumerateArray())
      {
        entries.Add(new GradientColorEntry(ParseColor(entry.GetProperty("color").GetString()), entry.GetProperty("location").GetDouble()));
      }
      var colorRange = new ColorRange(Gradient.FromEntries(OptionalString(gradientElement, "name"), entries),
        colorRangeElement.GetProperty("start").GetDouble(), colorRangeElement.GetProperty("end").GetDouble());
      return new RouteLineSettings(colorRange, e.GetProperty("width").GetDouble(), ParseColor(e.GetProperty("maskColor").GetString()),
        e.GetProperty("maskWidth").GetDouble(), e.GetProperty("maskVisible").GetBoolean(), e.GetProperty("alphaAdjustment").GetDouble())
      {
        MonochromeColor = ParseColor(e.GetProperty("monochromeColor").GetString()),
        MonochromeWidth = e.GetProperty("monochromeWidth").GetDouble()
      };
    }

    private static IMarkerDrawer ReadMarker(JsonElement e)
    {
      switch (e.GetProperty("kind").GetString())
      {
        case "disc":
          return new DiscDrawer(ParseColor(e.GetProperty("color").GetString()), e.GetProperty("radius").GetDouble());
        case "discAndCircle":
          return new DiscAndCircleDrawer(ParseColor(e.GetProperty("innerColor").GetString()), e.GetProperty("innerRadius").GetDouble(),
            ParseColor(e.GetProperty("outerColor").GetString()), e.GetProperty("outerRadius").GetDouble(), e.GetProperty("outerLineWidth").GetDouble());
        case "circle":
          return new CircleDrawer(ParseColor(e.GetProperty("color").GetString()), e.GetProperty("radius").GetDouble(), e.GetProperty("lineWidth").GetDouble());
        default:
          throw new InvalidDataException("Unknown marker kind.");
      }
    }

    private static GeneralMatrix ReadMatrix(JsonElement parent, string name)
    {
      if (!parent.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null) return null;
      var rows = new List<double[]>();
      foreach (var row in e.EnumerateArray())
      {
        var values = new List<double>();
        foreach (var value in row.EnumerateArray()) values.Add(value.GetDouble());
        rows.Add(values.ToArray());
      }
      return new GeneralMatrix(rows.ToArray());
    }

    private static LongLat ReadLongLat(JsonElement parent, string name)
    {
      if (!parent.TryGetProperty(name, out var e) || e.ValueKind == JsonValueKind.Null) return null;
      return new LongLat(e.GetProperty("lon").GetDouble(), e.GetProperty("lat").GetDouble());
    }

    private static string OptionalString(JsonElement parent, string name)
    {
      return parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }

    private static double? OptionalDouble(JsonElement parent, string name)
    {
      return parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : (double?)null;
    }

    private static T ParseEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
      return value != null && Enum.TryParse(value, out T result) ? result : defaultValue;
    }

    #endregion

    #region Value formats

    internal static string FormatTime(DateTime time)
    {
      return time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    internal static DateTime ParseTime(string value)
    {
      return DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    internal static string FormatColor(Color color)
    {
      return "#" + color.ToArgb().ToString("X8", CultureInfo.InvariantCulture);
    }

    internal static Color ParseColor(string value)
    {
      if (value == null || value.Length != 9 || value[0] != '#') throw new InvalidDataException("Invalid color value.");
      return Color.FromArgb(int.Parse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    #endregion
  }
}
