using System;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Collections;
using Xunit;

namespace QuickRoute.Tests
{
  public class CoreTests
  {
    [Fact]
    public void DistanceScaleHasExpectedMarkerCount()
    {
      Scale s = Distance.CreateScale(0, 100, 7, false);
      Assert.Equal(6, s.Markers.Count);
    }

    [Fact]
    public void ElapsedTimeIsFormatted()
    {
      Assert.Equal("2:10", new ElapsedTime(130).ToString());
    }

    [Fact]
    public void ElapsedTimeIsParsed()
    {
      var p = new ElapsedTime();
      p.FromString("2:10");
      Assert.Equal(130, p.Value);
    }

    [Fact]
    public void TimeOfDayScaleIsRounded()
    {
      Scale s = TimeOfDay.CreateScale(new DateTime(2008, 8, 1, 10, 22, 0), new DateTime(2008, 8, 1, 11, 30, 0), 10, true);
      Assert.Equal(new DateTime(2008, 8, 1, 10, 15, 0), ((TimeOfDay)s.Start).Value);
      Assert.Equal(new DateTime(2008, 8, 1, 11, 30, 0), ((TimeOfDay)s.End).Value);
      Assert.Equal(6, s.Markers.Count);
    }

    [Fact]
    public void OrderedBagKeepsSortOrderAndInsertionOrderOfEqualItems()
    {
      var bag = new OrderedBag<Tuple<int, string>>(new FirstItemComparer());
      bag.Add(Tuple.Create(2, "a"));
      bag.Add(Tuple.Create(1, "b"));
      bag.Add(Tuple.Create(2, "c"));
      bag.Add(Tuple.Create(0, "d"));

      Assert.Equal(new[] { "d", "b", "a", "c" }, new[] { bag[0].Item2, bag[1].Item2, bag[2].Item2, bag[3].Item2 });
      Assert.Equal(2, bag.IndexOf(Tuple.Create(2, "x")));
      Assert.True(bag.Remove(Tuple.Create(2, "x")));
      Assert.Equal("c", bag[2].Item2);
      Assert.Equal(-1, bag.IndexOf(Tuple.Create(5, "x")));
    }

    [Fact]
    public void SessionSettingsCopyIsDeep()
    {
      var original = new SessionSettings();
      var copy = original.Copy();
      copy.RouteLineSettingsCollection[WaypointAttribute.Pace].Width = 42;
      copy.SmoothingIntervals[WaypointAttribute.Altitude].End = 99;
      copy.RouteLineSettingsCollection[WaypointAttribute.Pace].ColorRange.Gradient.ColorEntries[0].Location = 0.25;

      Assert.Equal(3, original.RouteLineSettingsCollection[WaypointAttribute.Pace].Width);
      Assert.Equal(10, original.SmoothingIntervals[WaypointAttribute.Altitude].End);
      Assert.Equal(0, original.RouteLineSettingsCollection[WaypointAttribute.Pace].ColorRange.Gradient.ColorEntries[0].Location);
      Assert.Equal(original.MarkerDrawers.Count, copy.MarkerDrawers.Count);
    }

    [Fact]
    public void ExtendedRouteDrawingProducesFilledPaths()
    {
      var document = TestDocuments.CreateSyntheticDocument();
      var session = document.Sessions[0];
      var primitives = session.CreateRouteDrawing(1, Document.RouteDrawingMode.Extended, WaypointAttribute.Pace, null, null);
      Assert.NotEmpty(primitives);
      Assert.All(primitives, p => Assert.NotNull(p.Path));

      var simple = session.CreateRouteDrawing(2, Document.RouteDrawingMode.Simple, WaypointAttribute.Pace, null, null);
      var line = Assert.Single(simple);
      Assert.Equal(session.Route.Segments[0].Waypoints.Count, line.Polyline.Count);
      Assert.Equal(2 * session.Settings.RouteLineSettingsCollection[WaypointAttribute.Pace].MonochromeWidth, line.LineWidth);
    }

    private sealed class FirstItemComparer : System.Collections.Generic.IComparer<Tuple<int, string>>
    {
      public int Compare(Tuple<int, string> x, Tuple<int, string> y) => x.Item1.CompareTo(y.Item1);
    }
  }
}
