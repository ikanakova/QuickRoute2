using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Xml;

namespace QuickRoute.BusinessEntities.ExternalLapDataSources.CzechSplits
{
  public class CzechSplitsLapDataSource : IExternalLapDataSource
  {
    private string cachedEventId;
    private XmlNode cachedEvent;

    public IEnumerable<Event> GetEvents(DateTime startDate, DateTime endDate, string country)
    {
      return GetEvents(startDate, endDate, country, null);
    }

    public IEnumerable<KeyValuePair<string, string>> GetRegions()
    {
      var response = Request("getList", "&list=region");
      var regions = new List<KeyValuePair<string, string>>();
      foreach (XmlNode regionNode in response.SelectNodes("ID"))
      {
        regions.Add(new KeyValuePair<string, string>(Value(regionNode, "ID"), Value(regionNode, "Description")));
      }
      return regions;
    }

    public IEnumerable<Event> GetEvents(DateTime startDate, DateTime endDate, string country, string region)
    {
      var response = Request("getEventList", "&all=1&datefrom=" + startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        "&dateto=" + endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        (string.IsNullOrEmpty(region) ? "" : "&rg=" + Uri.EscapeDataString(region)));
      var translatedEvents = new List<Event>();
      foreach (XmlNode eventNode in response.SelectNodes("Event"))
      {
        translatedEvents.Add(new Event
        {
          DatabaseId = Value(eventNode, "ID"),
          Name = Value(eventNode, "Name"),
          Organiser = (Value(eventNode, "Org1/Name") + " " + Value(eventNode, "Org2/Name")).Trim(),
          StartDate = DateTime.ParseExact(Value(eventNode, "Date"), "yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
      }
      translatedEvents.Sort(delegate(Event first, Event second) { return first.StartDate.CompareTo(second.StartDate); });
      return translatedEvents;
    }

    public IEnumerable<Category> GetCategories(string eventId)
    {
      var eventNode = GetEvent(eventId);
      var translatedCategories = new List<Category>();
      foreach (XmlNode categoryNode in eventNode.SelectNodes("Classes/Class"))
      {
        translatedCategories.Add(new Category { Name = Value(categoryNode, "Name") });
      }
      return translatedCategories;
    }

    public IEnumerable<Runner> GetRunnersAndSplits(string eventId, int categoryIndex)
    {
      var translatedRunners = new List<Runner>();
      if (categoryIndex < 0) return translatedRunners;
      var eventNode = GetEvent(eventId);
      var categories = eventNode.SelectNodes("Classes/Class");
      if (categoryIndex >= categories.Count) return translatedRunners;
      var response = Request("getSplits", "&classid=" + Uri.EscapeDataString(Value(categories[categoryIndex], "ID")));
      var eventDate = DateTime.ParseExact(Value(eventNode, "Date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
      foreach (XmlNode runnerNode in response.SelectNodes("Splits/*"))
      {
        var runner = TranslateRunner(runnerNode, eventDate);
        if (runner != null) translatedRunners.Add(runner);
      }
      return translatedRunners;
    }

    private XmlNode GetEvent(string eventId)
    {
      if (cachedEventId != eventId)
      {
        var eventNode = Request("getEvent", "&id=" + Uri.EscapeDataString(eventId));
        cachedEvent = eventNode;
        cachedEventId = eventId;
      }
      return cachedEvent;
    }

    private static XmlNode Request(string method, string parameters)
    {
      var document = ExternalLapDataHttp.GetXmlDocument("https://oris.ceskyorientak.cz/API/?format=xml2&method=" + method + parameters);
      var status = document.SelectSingleNode("/ORIS/Status");
      if (status == null || status.InnerText != "OK")
      {
        throw new InvalidOperationException("ORIS: " + (status == null ? QuickRoute.Resources.Strings.ResourceManager.GetString("Error_InvalidApiResponse") : status.InnerText));
      }
      var data = document.SelectSingleNode("/ORIS/Data");
      if (data == null) throw new InvalidOperationException("ORIS: " + QuickRoute.Resources.Strings.ResourceManager.GetString("Error_MissingApiData"));
      return data;
    }

    private static string Value(XmlNode node, string path)
    {
      var value = node.SelectSingleNode(path);
      return value == null ? "" : value.InnerText.Trim();
    }

    private static TimeSpan? ParseTime(string value)
    {
      var parts = value.Split(':');
      if (parts.Length != 2 && parts.Length != 3) return null;
      int hours = 0;
      int minutes;
      int seconds;
      if (parts.Length == 3 && !int.TryParse(parts[0], out hours)) return null;
      if (!int.TryParse(parts[parts.Length - 2], out minutes) ||
          !int.TryParse(parts[parts.Length - 1], out seconds)) return null;
      if (hours < 0 || minutes < 0 || seconds < 0 || seconds >= 60 || (parts.Length == 3 && minutes >= 60)) return null;
      return TimeSpan.FromSeconds((double)hours * 3600 + (double)minutes * 60 + seconds);
    }

    private static Runner TranslateRunner(XmlNode runnerNode, DateTime eventDate)
    {
      var start = ParseTime(Value(runnerNode, "StartTime"));
      var finish = ParseTime(Value(runnerNode, "FinishTime"));
      var duration = ParseTime(Value(runnerNode, "ResTime"));
      if (!start.HasValue && finish.HasValue && duration.HasValue)
      {
        start = finish.Value - duration.Value;
        if (start.Value < TimeSpan.Zero) start = start.Value.Add(TimeSpan.FromDays(1));
      }
      if (!start.HasValue) return null;
      var startTime = eventDate.Add(start.Value);
      DateTime? finishTime = null;
      if (finish.HasValue)
      {
        finishTime = eventDate.Add(finish.Value);
        if (finishTime.Value < startTime) finishTime = finishTime.Value.AddDays(1);
      }
      else if (duration.HasValue)
      {
        finishTime = startTime.Add(duration.Value);
      }
      var orderedSplits = new SortedDictionary<int, DateTime>();
      foreach (XmlNode splitNode in runnerNode.ChildNodes)
      {
        int controlIndex;
        if (!splitNode.Name.StartsWith("TotalTime", StringComparison.Ordinal) ||
            !int.TryParse(splitNode.Name.Substring("TotalTime".Length), out controlIndex) || controlIndex == 999) continue;
        var elapsed = ParseTime(splitNode.InnerText.Trim());
        if (elapsed.HasValue) orderedSplits[controlIndex] = startTime.Add(elapsed.Value);
      }
      var splits = new List<DateTime>();
      foreach (var split in orderedSplits.Values)
      {
        splits.Add(split);
      }
      return new Runner
      {
        Name = Value(runnerNode, "ResName"),
        Club = Value(runnerNode, "ResClub"),
        Splits = splits,
        StartTime = startTime,
        FinishTime = finishTime
      };
    }
  }
}