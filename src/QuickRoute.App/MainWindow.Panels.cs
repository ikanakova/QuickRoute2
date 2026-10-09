using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using QuickRoute.App.Controls;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.RouteProperties;
using QuickRoute.Resources;

namespace QuickRoute.App
{
  public partial class MainWindow
  {
    private static readonly Type[] lapPropertyTypes =
    {
      typeof(LapNumber), typeof(QuickRoute.BusinessEntities.RouteProperties.ElapsedTime), typeof(StraightLineDistance), typeof(RouteDistance),
      typeof(AverageStraightLinePace), typeof(AverageRoutePace), typeof(RouteToStraightLine), typeof(AverageHeartRate), typeof(Ascent), typeof(Descent)
    };

    private static readonly Type[] momentaneousInfoPropertyTypes =
    {
      typeof(LapNumber), typeof(Time), typeof(QuickRoute.BusinessEntities.RouteProperties.ElapsedTime), typeof(ElapsedTimeFromStart),
      typeof(CircleTimeBackward), typeof(CircleTimeForward), typeof(StraightLineDistance), typeof(RouteDistance), typeof(RouteDistanceFromStart),
      typeof(Pace), typeof(Speed), typeof(QuickRoute.BusinessEntities.RouteProperties.HeartRate), typeof(QuickRoute.BusinessEntities.RouteProperties.Altitude),
      typeof(Inclination), typeof(AscentFromStart), typeof(DescentFromStart), typeof(DirectionDeviationToNextLap), typeof(QuickRoute.BusinessEntities.RouteProperties.Location)
    };

    private List<LapInfo> lapInfoList = new List<LapInfo>();
    private readonly List<(Type Type, TextBlock Text)> momentaneousInfoLabels = new List<(Type, TextBlock)>();

    private void WirePanelEvents()
    {
      LapGrid.SelectionChanged += (_, _) =>
      {
        if (UpdatingUi) return;
        AlphaAdjustSelectedLap();
        CreateLineGraph();
      };
      SessionList.SelectionChanged += (_, _) =>
      {
        if (UpdatingUi || document == null) return;
        var selected = SessionList.SelectedItems?.OfType<SessionListItem>().Select(i => i.Session).ToList() ?? new List<Session>();
        if (selected.Count == 0) return;
        var sessions = new SessionCollection();
        foreach (var s in selected) sessions.Add(s);
        Canvas.SessionsToDraw = sessions;
        Canvas.CurrentSession = selected[0];
        RefreshAll();
      };
      SessionList.SelectionMode = SelectionMode.Multiple;
    }

    private sealed class SessionListItem
    {
      public SessionListItem(Session session, int index)
      {
        Session = session;
        var name = session.SessionInfo?.Person?.Name;
        Text = string.IsNullOrEmpty(name) ? string.Format(Strings.UntitledX, index + 1) : name;
      }

      public Session Session { get; }

      public string Text { get; }

      public override string ToString() => Text;
    }

    private void PopulateSessionList()
    {
      updatingUi++;
      var items = document.Sessions.Select((s, i) => new SessionListItem(s, i)).ToList();
      SessionList.ItemsSource = items;
      SessionList.IsVisible = items.Count > 1;
      SessionList.SelectedItems?.Clear();
      foreach (var item in items.Where(i => Canvas.SessionsToDraw.Contains(i.Session))) SessionList.SelectedItems?.Add(item);
      updatingUi--;
    }

    private void RefreshAll()
    {
      if (document == null) return;
      CalculateLapInfo();
      CreateMomentaneousInfoLabels();
      CreateLineGraph();
      Canvas.InvalidateRoute();
    }

    #region Lap grid

    private sealed class LapRow
    {
      public LapRow(LapInfo info, string[] cells)
      {
        Info = info;
        Cells = cells;
      }

      public LapInfo Info { get; }

      public string[] Cells { get; }
    }

    private sealed class LapRowComparer : IComparer
    {
      private readonly int column;

      public LapRowComparer(int column)
      {
        this.column = column;
      }

      public int Compare(object x, object y)
      {
        var a = (LapRow)x;
        var b = (LapRow)y;
        // keep the total row last regardless of sort direction is not possible with a single comparer; sort it like the other rows
        return a.Info.CompareProperty(b.Info, column);
      }
    }

    private void CalculateLapInfo()
    {
      var s = CurrentSession;
      if (s == null) return;
      var r = s.Route;
      var previousLapPL = ParameterizedLocation.Start;
      lapInfoList = new List<LapInfo>();
      var cacheManager = new RoutePropertyCacheManager();
      for (var i = 0; i < s.Laps.Count; i++)
      {
        var lap = s.Laps[i];
        var lapPL = r.GetParameterizedLocationFromTime(lap.Time, previousLapPL, ParameterizedLocation.Direction.Forward);
        if (lap.LapType != LapType.Start && i > 0)
        {
          var li = new LapInfo { LapType = lap.LapType, Index = i };
          var locations = new RouteLocations(previousLapPL, lapPL);
          foreach (var type in lapPropertyTypes)
          {
            RetrieveExternalPropertyDelegate dlg = new ExternalRoutePropertyRetriever(s.Settings).RetrieveExternalProperty;
            var lp = (RouteProperty)Activator.CreateInstance(type, s, locations, dlg);
            lp.CacheManager = cacheManager;
            li.AddProperty(lp);
          }
          if (s.Laps.Count > 2) lapInfoList.Add(li);
        }
        previousLapPL = lapPL;
      }

      // total row
      var totalInfo = new LapInfo { LapType = LapType.Stop, Index = -1 };
      foreach (var type in lapPropertyTypes)
      {
        var spanProperty = Activator.CreateInstance(type, s, new RouteLocations(ParameterizedLocation.Start), null) as RouteSpanProperty;
        var fromStartType = spanProperty != null ? spanProperty.GetRouteFromStartPropertyType() : type;
        RetrieveExternalPropertyDelegate dlg = new ExternalRoutePropertyRetriever(s.Settings).RetrieveExternalProperty;
        var fromStart = fromStartType == null ? null : Activator.CreateInstance(fromStartType, s, new RouteLocations(r.LastPL), dlg) as RouteFromStartProperty;
        totalInfo.AddProperty(fromStart ?? (RouteProperty)new BlankRouteProperty());
      }
      lapInfoList.Add(totalInfo);

      updatingUi++;
      LapGrid.Columns.Clear();
      for (var c = 0; c < lapPropertyTypes.Length; c++)
      {
        LapGrid.Columns.Add(new DataGridTextColumn
        {
          Header = Strings.ResourceManager.GetString("RoutePropertyName_" + lapPropertyTypes[c].Name) ?? lapPropertyTypes[c].Name,
          Binding = new Binding("Cells[" + c + "]"),
          CustomSortComparer = new LapRowComparer(c)
        });
      }
      var rows = lapInfoList.Select(li =>
      {
        var cells = li.GetProperties().Select(p => p is LapNumber && li.Index == -1 ? Strings.Total : (p.ContainsValue || p.Value != null ? p.ToString() : "-")).ToArray();
        return new LapRow(li, cells);
      }).ToList();
      LapGrid.ItemsSource = rows;
      LapGrid.SelectedIndex = rows.Count - 1;
      updatingUi--;
      AlphaAdjustSelectedLap();
    }

    private LapInfo SelectedLapInfo => (LapGrid.SelectedItem as LapRow)?.Info;

    private void AlphaAdjustSelectedLap()
    {
      var s = CurrentSession;
      var info = SelectedLapInfo;
      if (s == null || info == null) return;
      var rls = CurrentRouteLineSettings;
      var lap = info.Index > 0 ? s.Laps[info.Index] : null;
      Canvas.AlphaAdjustLap(s, lap,
        GraphicsUtil.CombineAlphaAdjustments(rls.AlphaAdjustment, 0.5),
        GraphicsUtil.CombineAlphaAdjustments(rls.AlphaAdjustment, -0.5),
        rls.AlphaAdjustment);
      if (lap != null)
      {
        var lastLap = s.Laps[info.Index - 1];
        Canvas.EnsureVisible(s.AdjustedRoute.GetBoundingRectangle(
          s.Route.GetParameterizedLocationFromTime(lastLap.Time),
          s.Route.GetParameterizedLocationFromTime(lap.Time)));
      }
    }

    #endregion

    #region Momentaneous info

    private void CreateMomentaneousInfoLabels()
    {
      momentaneousInfoLabels.Clear();
      MomentaneousInfoPanel.Children.Clear();
      var s = CurrentSession;
      if (s == null) return;
      foreach (var type in momentaneousInfoPropertyTypes)
      {
        RetrieveExternalPropertyDelegate dlg = new ExternalRoutePropertyRetriever(s.Settings).RetrieveExternalProperty;
        var property = Activator.CreateInstance(type, s, new RouteLocations(s.Route.FirstPL), dlg) as RouteProperty;
        if (property == null || !property.ContainsValue) continue;
        var text = new TextBlock { Margin = new Avalonia.Thickness(6, 2), MinWidth = 60 };
        momentaneousInfoLabels.Add((type, text));
        MomentaneousInfoPanel.Children.Add(new Border
        {
          BorderBrush = Brushes.LightGray,
          BorderThickness = new Avalonia.Thickness(0, 0, 1, 0),
          Child = text
        });
      }
      UpdateMomentaneousInfo(null);
    }

    private void UpdateMomentaneousInfo(ParameterizedLocation pl)
    {
      var s = CurrentSession;
      if (s == null) return;
      var lapStartPL = pl == null ? null : s.Route.GetLapStartParameterizedLocation(pl);
      foreach (var (type, text) in momentaneousInfoLabels)
      {
        var template = Strings.ResourceManager.GetString("RoutePropertyNameAndValue_" + type.Name) ?? type.Name + ": {0}";
        if (pl == null)
        {
          text.Text = string.Format(template, "-");
          continue;
        }
        RetrieveExternalPropertyDelegate dlg = new ExternalRoutePropertyRetriever(s.Settings).RetrieveExternalProperty;
        var property = Activator.CreateInstance(type, s, new RouteLocations(lapStartPL, pl), dlg) as RouteProperty;
        text.Text = property?.Value != null ? string.Format(template, property) : string.Format(template, "-");
      }
    }

    private void Canvas_RouteMouseHover(object sender, RouteHoverEventArgs e)
    {
      UpdateMomentaneousInfo(e.IsClose ? e.ParameterizedLocation : null);
    }

    #endregion

    #region Line graph

    private void CreateLineGraph()
    {
      var s = CurrentSession;
      if (document == null || s == null || lapInfoList.Count == 0)
      {
        LineGraphView.Graph = null;
        return;
      }
      var wa = SelectedColorCodingAttribute;
      var criss = document.Settings.ColorRangeIntervalSliderSettings[wa];
      var graph = new LineGraph
      {
        YAxisAttribute = wa,
        Session = s,
        YAxisMinValue = criss.MinValue,
        YAxisMaxValue = criss.MaxValue,
        YAxisScaleCreator = CreateScaleCreator(wa, criss.MinValue, criss.MaxValue, 10),
        YAxisNumericConverter = GetNumericConverter(wa),
        XAxisAttribute = DomainAttribute.TimeOfDay,
        XAxisCaption = Strings.Time
      };
      var info = SelectedLapInfo ?? lapInfoList[lapInfoList.Count - 1];
      if (info.Index == -1)
      {
        graph.StartPL = s.Route.GetParameterizedLocationFromTime(s.Route.FirstWaypoint.Time);
        graph.EndPL = s.Route.GetParameterizedLocationFromTime(s.Route.LastWaypoint.Time);
      }
      else
      {
        graph.StartPL = s.Route.GetParameterizedLocationFromTime(s.Laps[info.Index - 1].Time);
        graph.EndPL = s.Route.GetParameterizedLocationFromTime(s.Laps[info.Index].Time);
      }
      var name = new WaypointAttributeString(wa);
      graph.YAxisCaption = string.IsNullOrEmpty(name.Unit) ? name.Name : name.Name + " (" + name.Unit + ")";
      graph.Calculate();
      LineGraphView.Graph = graph;
    }

    private void LineGraphView_GraphHover(object sender, double? xValue)
    {
      var s = CurrentSession;
      if (s == null || !xValue.HasValue)
      {
        Canvas.ShowHoverMarker(null);
        UpdateMomentaneousInfo(null);
        return;
      }
      var time = new DateTime((long)(xValue.Value * TimeSpan.TicksPerSecond), DateTimeKind.Local).ToUniversalTime();
      if (time < s.Route.FirstWaypoint.Time || time > s.Route.LastWaypoint.Time) return;
      var pl = s.Route.GetParameterizedLocationFromTime(time);
      Canvas.ShowHoverMarker(pl);
      UpdateMomentaneousInfo(pl);
    }

    #endregion
  }
}
