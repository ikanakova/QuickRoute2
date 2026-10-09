using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities.ExternalLapDataSources;
using QuickRoute.BusinessEntities.ExternalLapDataSources.CzechSplits;
using QuickRoute.BusinessEntities.ExternalLapDataSources.WinSplits;

namespace QuickRoute.App.Dialogs
{
  /// <summary>
  /// Retrieves split times of a runner from WinSplits or ORIS. Network calls run off the UI thread.
  /// </summary>
  public class ExternalLapsDialog : FormDialog
  {
    private readonly AppSettings settings;
    private readonly List<KeyValuePair<string, IExternalLapDataSource>> sources = ExternalLapDataSourceFactory.GetExternalLapDataSources().ToList();
    private readonly ComboBox sourceBox;
    private readonly CalendarDatePicker fromDate;
    private readonly CalendarDatePicker toDate;
    private readonly ComboBox regionBox = CreateComboBox(Array.Empty<string>(), -1);
    private readonly ComboBox eventBox = CreateComboBox(Array.Empty<string>(), -1);
    private readonly ComboBox classBox = CreateComboBox(Array.Empty<string>(), -1);
    private readonly ComboBox runnerBox = CreateComboBox(Array.Empty<string>(), -1);
    private readonly TextBlock status = new TextBlock { Opacity = 0.7 };
    private List<KeyValuePair<string, string>> regions = new List<KeyValuePair<string, string>>();
    private List<Event> events = new List<Event>();
    private List<Runner> runners = new List<Runner>();
    private int requestVersion;

    public ExternalLapsDialog(DateTime date, AppSettings settings) : base(Loc.Plain("Menu_ToolsAddLaps").Replace("...", ""))
    {
      this.settings = settings;
      sourceBox = AddRow(Loc.Plain("ExternalLaps_Source"), CreateComboBox(sources.Select(s => s.Key), Math.Min(Math.Max(0, settings.ExternalLapDataSourceIndex), sources.Count - 1)));
      fromDate = AddRow(Loc.Plain("ExternalLaps_From"), new CalendarDatePicker { SelectedDate = date });
      toDate = AddRow(Loc.Plain("ExternalLaps_To"), new CalendarDatePicker { SelectedDate = date });
      AddRow(Loc.Plain("ExternalLaps_Region"), regionBox);
      AddRow(Loc.Plain("ExternalLaps_Event"), eventBox);
      AddRow(Loc.Plain("ExternalLaps_Class"), classBox);
      AddRow(Loc.Plain("ExternalLaps_Runner"), runnerBox);
      AddRow(null, status);
      OkEnabled = false;

      sourceBox.SelectionChanged += async (_, _) => await LoadRegionsAsync();
      fromDate.SelectedDateChanged += async (_, _) => await LoadRegionsAsync();
      toDate.SelectedDateChanged += async (_, _) => await LoadRegionsAsync();
      regionBox.SelectionChanged += async (_, _) => await LoadEventsAsync();
      eventBox.SelectionChanged += async (_, _) => await LoadClassesAsync();
      classBox.SelectionChanged += async (_, _) => await LoadRunnersAsync();
      runnerBox.SelectionChanged += (_, _) => OkEnabled = runnerBox.SelectedIndex >= 0;
      Opened += async (_, _) => await LoadRegionsAsync();
    }

    private IExternalLapDataSource Source => sources[Math.Max(0, sourceBox.SelectedIndex)].Value;

    public new async Task<Runner> ShowAsync(Window owner)
    {
      if (!await base.ShowAsync(owner) || runnerBox.SelectedIndex < 0) return null;
      settings.ExternalLapDataSourceIndex = sourceBox.SelectedIndex;
      return runners[runnerBox.SelectedIndex];
    }

    private DateTime From => (fromDate.SelectedDate ?? DateTime.Today).Date;

    private DateTime To => (toDate.SelectedDate ?? From).Date;

    private async Task LoadRegionsAsync()
    {
      var source = Source;
      var from = From;
      var to = To;
      await RunAsync(() =>
      {
        var list = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(null, Loc.Plain("ExternalLaps_All")) };
        if (source is CzechSplitsLapDataSource oris) list.AddRange(oris.GetRegions());
        else if (source is WinSplitsLapDataSource winSplits) list.AddRange(winSplits.GetCountries(from, to));
        return list;
      }, list =>
      {
        regions = list;
        regionBox.ItemsSource = regions.Select(r => r.Key == null ? r.Value : r.Key + " - " + r.Value).ToList();
        regionBox.SelectedIndex = -1;
        Clear(eventBox, classBox, runnerBox);
      });
    }

    private async Task LoadEventsAsync()
    {
      if (regionBox.SelectedIndex < 0) return;
      var source = Source;
      var region = regions[regionBox.SelectedIndex].Key;
      var from = From;
      var to = To;
      await RunAsync(() => source is CzechSplitsLapDataSource oris
          ? oris.GetEvents(from, to, null, region).ToList()
          : source.GetEvents(from, to, region).ToList(),
        list =>
        {
          events = list;
          eventBox.ItemsSource = events.Select(e => e.StartDate.ToShortDateString() + " " + e.Name).ToList();
          Clear(classBox, runnerBox);
        });
    }

    private async Task LoadClassesAsync()
    {
      if (eventBox.SelectedIndex < 0) return;
      var source = Source;
      var eventId = events[eventBox.SelectedIndex].DatabaseId;
      await RunAsync(() => source.GetCategories(eventId).Select(c => c.Name).ToList(), list =>
      {
        classBox.ItemsSource = list;
        classBox.SelectedIndex = -1;
        Clear(runnerBox);
      });
    }

    private async Task LoadRunnersAsync()
    {
      if (eventBox.SelectedIndex < 0 || classBox.SelectedIndex < 0) return;
      var source = Source;
      var eventId = events[eventBox.SelectedIndex].DatabaseId;
      var classIndex = classBox.SelectedIndex;
      await RunAsync(() => source.GetRunnersAndSplits(eventId, classIndex).ToList(), list =>
      {
        runners = list;
        runnerBox.ItemsSource = runners.Select(r => string.IsNullOrEmpty(r.Club) ? r.Name : r.Name + ", " + r.Club).ToList();
        runnerBox.SelectedIndex = -1;
        OkEnabled = false;
      });
    }

    private async Task RunAsync<T>(Func<T> load, Action<T> apply)
    {
      var version = ++requestVersion;
      status.Text = Loc.Plain("ExternalLaps_Loading");
      try
      {
        var result = await Task.Run(load);
        if (version != requestVersion) return;
        apply(result);
        status.Text = "";
      }
      catch (Exception ex)
      {
        if (version != requestVersion) return;
        status.Text = Loc.Plain("Error_ExternalLaps") + " " + ex.Message;
      }
    }

    private void Clear(params ComboBox[] boxes)
    {
      foreach (var box in boxes)
      {
        box.ItemsSource = Array.Empty<string>();
        box.SelectedIndex = -1;
      }
      OkEnabled = false;
    }
  }
}
