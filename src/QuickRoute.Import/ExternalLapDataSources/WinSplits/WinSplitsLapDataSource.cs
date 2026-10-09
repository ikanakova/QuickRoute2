using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Xml;

namespace QuickRoute.BusinessEntities.ExternalLapDataSources.WinSplits
{
  public class WinSplitsLapDataSource : IExternalLapDataSource
  {
    private const string IofNamespace = "http://www.orienteering.org/datastandard/3.0";
    private string cachedEventId;
    private XmlDocument cachedClasses;

    public IEnumerable<Event> GetEvents(DateTime startDate, DateTime endDate, string country)
    {
      var translatedEvents = new List<Event>();
      foreach (var eventNode in GetEventNodes(startDate.Date, endDate.Date, country))
      {
        var namespaces = Namespaces(eventNode.OwnerDocument);
        var organisers = new List<string>();
        foreach (XmlNode organiser in eventNode.SelectNodes("iof:Organiser/iof:Name", namespaces)) organisers.Add(organiser.InnerText);
        translatedEvents.Add(new Event
        {
          DatabaseId = Value(eventNode, "iof:Id", namespaces),
          Name = Value(eventNode, "iof:Name", namespaces),
          Organiser = string.Join(", ", organisers.ToArray()),
          StartDate = DateTime.ParseExact(Value(eventNode, "iof:StartTime/iof:Date", namespaces), "yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
      }
      translatedEvents.Sort(delegate(Event first, Event second) { return first.StartDate.CompareTo(second.StartDate); });
      return translatedEvents;
    }

    public IEnumerable<KeyValuePair<string, string>> GetCountries(DateTime startDate, DateTime endDate)
    {
      var countries = new SortedDictionary<string, string>(StringComparer.Ordinal);
      foreach (var eventNode in GetEventNodes(startDate.Date, endDate.Date, null))
      {
        foreach (XmlNode country in eventNode.SelectNodes("iof:Organiser/iof:Country", Namespaces(eventNode.OwnerDocument)))
        {
          var code = country.Attributes["code"] == null ? "" : country.Attributes["code"].Value;
          if (!string.IsNullOrEmpty(code)) countries[code] = country.InnerText.Trim();
        }
      }
      foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
      {
        var region = new RegionInfo(culture.Name);
        if (countries.ContainsKey(region.ThreeLetterISORegionName) && string.IsNullOrEmpty(countries[region.ThreeLetterISORegionName]))
          countries[region.ThreeLetterISORegionName] = region.EnglishName;
      }
      foreach (var code in new List<string>(countries.Keys))
      {
        var name = QuickRoute.Resources.Strings.ResourceManager.GetString("CountryName_" + code);
        if (!string.IsNullOrEmpty(name)) countries[code] = name;
      }
      return countries;
    }

    public IEnumerable<Category> GetCategories(string eventId)
    {
      var document = GetClasses(eventId);
      var translatedCategories = new List<Category>();
      var namespaces = Namespaces(document);
      foreach (XmlNode category in document.SelectNodes("/iof:ClassList/iof:Class", namespaces))
      {
        translatedCategories.Add(new Category { Name = Value(category, "iof:Name", namespaces) });
      }
      return translatedCategories;
    }

    public IEnumerable<Runner> GetRunnersAndSplits(string eventId, int categoryIndex)
    {
      var translatedRunners = new List<Runner>();
      if (categoryIndex < 0) return translatedRunners;
      var classes = GetClasses(eventId);
      var classNodes = classes.SelectNodes("/iof:ClassList/iof:Class", Namespaces(classes));
      if (categoryIndex >= classNodes.Count) return translatedRunners;
      var classId = Value(classNodes[categoryIndex], "iof:Id", Namespaces(classes));
      var document = Request("events/" + Uri.EscapeDataString(eventId) + "/classes/" + Uri.EscapeDataString(classId) + "/resultlist/xml", "ResultList");
      var namespaces = Namespaces(document);
      foreach (XmlNode person in document.SelectNodes("/iof:ResultList/iof:ClassResult/iof:PersonResult", namespaces))
      {
        foreach (XmlNode result in person.SelectNodes("iof:Result", namespaces))
        {
          var runner = TranslateRunner(person, result, namespaces);
          if (runner != null) translatedRunners.Add(runner);
        }
      }
      return translatedRunners;
    }

    private static List<XmlNode> GetEventNodes(DateTime startDate, DateTime endDate, string country)
    {
      var events = new List<XmlNode>();
      if (startDate > endDate) return events;
      var document = Request("events?from=" + startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        "&to=" + endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        (string.IsNullOrEmpty(country) ? "" : "&country=" + Uri.EscapeDataString(country)), "EventList");
      var nodes = document.SelectNodes("/iof:EventList/iof:Event", Namespaces(document));
      if (nodes.Count >= 1000)
      {
        if (startDate == endDate) throw new InvalidOperationException("WinSplits: " + QuickRoute.Resources.Strings.ResourceManager.GetString("Error_TooManyEvents"));
        var middle = startDate.AddDays((endDate - startDate).Days / 2);
        events.AddRange(GetEventNodes(startDate, middle, country));
        events.AddRange(GetEventNodes(middle.AddDays(1), endDate, country));
      }
      else
      {
        foreach (XmlNode eventNode in nodes) events.Add(eventNode);
      }
      return events;
    }

    private XmlDocument GetClasses(string eventId)
    {
      if (cachedEventId != eventId)
      {
        var document = Request("events/" + Uri.EscapeDataString(eventId) + "/classes", "ClassList");
        cachedClasses = document;
        cachedEventId = eventId;
      }
      return cachedClasses;
    }

    private static XmlDocument Request(string path, string rootName)
    {
      var document = ExternalLapDataHttp.GetXmlDocument("https://obasen.orientering.se/winsplits/api/" + path);
      if (document.DocumentElement == null || document.DocumentElement.LocalName != rootName || document.DocumentElement.NamespaceURI != IofNamespace)
        throw new InvalidOperationException("WinSplits: " + QuickRoute.Resources.Strings.ResourceManager.GetString("Error_InvalidIofXml"));
      return document;
    }

    private static XmlNamespaceManager Namespaces(XmlDocument document)
    {
      var namespaces = new XmlNamespaceManager(document.NameTable);
      namespaces.AddNamespace("iof", IofNamespace);
      return namespaces;
    }

    private static string Value(XmlNode node, string path, XmlNamespaceManager namespaces)
    {
      var value = node.SelectSingleNode(path, namespaces);
      return value == null ? "" : value.InnerText.Trim();
    }

    private static DateTime? ParseDateTime(string value)
    {
      return string.IsNullOrEmpty(value) ? (DateTime?)null : XmlConvert.ToDateTime(value, XmlDateTimeSerializationMode.Local);
    }

    private static double? ParseSeconds(string value)
    {
      double seconds;
      return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
        seconds >= 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds) ? (double?)seconds : null;
    }

    private static Runner TranslateRunner(XmlNode person, XmlNode result, XmlNamespaceManager namespaces)
    {
      var start = ParseDateTime(Value(result, "iof:StartTime", namespaces));
      var finish = ParseDateTime(Value(result, "iof:FinishTime", namespaces));
      var duration = ParseSeconds(Value(result, "iof:Time", namespaces));
      if (!start.HasValue && finish.HasValue && duration.HasValue) start = finish.Value.AddSeconds(-duration.Value);
      if (!start.HasValue) return null;
      if (!finish.HasValue && duration.HasValue) finish = start.Value.AddSeconds(duration.Value);
      var splits = new List<DateTime>();
      foreach (XmlNode split in result.SelectNodes("iof:SplitTime", namespaces))
      {
        if (split.Attributes["status"] != null && split.Attributes["status"].Value != "OK") continue;
        var seconds = ParseSeconds(Value(split, "iof:Time", namespaces));
        if (seconds.HasValue) splits.Add(start.Value.AddSeconds(seconds.Value));
      }
      return new Runner
      {
        Name = (Value(person, "iof:Person/iof:Name/iof:Given", namespaces) + " " + Value(person, "iof:Person/iof:Name/iof:Family", namespaces)).Trim(),
        Club = Value(person, "iof:Organisation/iof:Name", namespaces),
        StartTime = start,
        FinishTime = finish,
        Splits = splits
      };
    }
  }
}