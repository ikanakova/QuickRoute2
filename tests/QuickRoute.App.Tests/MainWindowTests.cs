using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuickRoute.App;
using QuickRoute.App.Controls;
using QuickRoute.App.Dialogs;
using QuickRoute.App.Services;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(QuickRoute.App.Tests.TestAppBuilder))]

namespace QuickRoute.App.Tests
{
  public class TestAppBuilder
  {
    public static AppBuilder BuildAvaloniaApp()
    {
      // never touch a real user profile during tests
      Environment.SetEnvironmentVariable("QUICKROUTE2_DATA_HOME", Path.Combine(Path.GetTempPath(), "QuickRoute2-AppTests-" + Environment.ProcessId));
      return AppBuilder.Configure<QuickRoute.App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
  }

  public class MainWindowTests
  {
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "TestData", "synthetic-rawdata.qrt");

    [AvaloniaFact]
    public async Task LegacyDocumentIsOpenedAndRendered()
    {
      var window = new MainWindow(new AppSettings());
      window.Show();
      window.OpenFileOnStartup(FixturePath);
      var canvas = window.GetVisualDescendants().OfType<MapCanvas>().Single();
      for (var i = 0; i < 100 && canvas.Document == null; i++)
      {
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
      }
      Assert.NotNull(canvas.Document);
      Assert.Equal("synthetic-rawdata.qrt - QuickRoute2", window.Title);
      Assert.Equal(320, canvas.Width);
      Assert.Equal(240, canvas.Height);

      var frame = window.CaptureRenderedFrame();
      Assert.NotNull(frame);
      using var stream = new MemoryStream();
      frame.Save(stream);
      Assert.True(stream.Length > 0);

      var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
      Assert.Equal(3, ((System.Collections.IEnumerable)grid.ItemsSource).Cast<object>().Count());
      window.Close();
    }

    [AvaloniaFact]
    public async Task SidePanelCanBeHiddenAndShownAndHelpIsATooltip()
    {
      var window = new MainWindow(new AppSettings());
      window.Show();
      window.OpenFileOnStartup(FixturePath);
      var canvas = window.GetVisualDescendants().OfType<MapCanvas>().Single();
      for (var i = 0; i < 100 && canvas.Document == null; i++)
      {
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
      }
      var rightPanel = window.FindControl<Grid>("RightPanel");
      var bottomPanel = window.FindControl<Border>("BottomPanel");
      var toolRight = window.FindControl<ToggleButton>("ToolRightPanel");
      var toolBottom = window.FindControl<ToggleButton>("ToolBottomPanel");
      Assert.True(rightPanel.IsVisible);
      Assert.True(bottomPanel.IsVisible);

      toolRight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      toolBottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Dispatcher.UIThread.RunJobs();
      Assert.False(rightPanel.IsVisible);
      Assert.False(bottomPanel.IsVisible);

      toolRight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      toolBottom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Dispatcher.UIThread.RunJobs();
      Assert.True(rightPanel.IsVisible);
      Assert.True(bottomPanel.IsVisible);

      Assert.Null(window.FindControl<TextBlock>("StatusText"));
      var help = window.FindControl<TextBlock>("MouseToolHelp");
      Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(help) as string));
      var metrics = window.FindControl<WrapPanel>("MomentaneousInfoPanel");
      Assert.NotEmpty(metrics.Children);
      window.Close();
    }

    [AvaloniaFact]
    public async Task AdjustRouteDragIsUndoable()
    {
      var window = new MainWindow(new AppSettings());
      window.Show();
      window.OpenFileOnStartup(FixturePath);
      var canvas = window.GetVisualDescendants().OfType<MapCanvas>().Single();
      for (var i = 0; i < 100 && canvas.Document == null; i++)
      {
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
      }
      canvas.MouseTool = MouseTool.AdjustRoute;
      var session = canvas.CurrentSession;
      var waypoints = session.AdjustedRoute.Segments[0].Waypoints;
      var middle = waypoints[waypoints.Count / 2].Location;
      var before = session.AdjustedRoute.Segments[0].Waypoints[waypoints.Count / 3].Location;

      var origin = canvas.TranslatePoint(new Point(middle.X, middle.Y), window).Value;
      window.MouseMove(origin);
      window.MouseDown(origin, MouseButton.Left);
      window.MouseMove(origin + new Point(20, 15));
      window.MouseUp(origin + new Point(20, 15), MouseButton.Left);
      Dispatcher.UIThread.RunJobs();

      var moved = session.AdjustedRoute.Segments[0].Waypoints[waypoints.Count / 3].Location;
      Assert.NotEqual(before.X, moved.X);

      window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
      Dispatcher.UIThread.RunJobs();
      var undone = session.AdjustedRoute.Segments[0].Waypoints[waypoints.Count / 3].Location;
      Assert.Equal(before.X, undone.X, 6);
      Assert.Equal(before.Y, undone.Y, 6);
      window.Close();
    }

    [AvaloniaFact]
    public void AboutDialogShowsNameVersionAndOriginalAuthors()
    {
      var previousCulture = CultureInfo.CurrentUICulture;
      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
      try
      {
        var dialog = new AboutDialog();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();

        Assert.Equal("About QuickRoute2", dialog.Title);
        Assert.Contains("QuickRoute2", texts);
        Assert.Contains("Version 1.0.0", texts);
        Assert.Contains("Based on QuickRoute by Mats Troeng and Jörgen Ohlin", texts);
        Assert.DoesNotContain(texts, t => t.Contains("translat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(texts, t => t.Contains("ikanakova", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dialog.CaptureRenderedFrame());
        dialog.Close();
      }
      finally
      {
        CultureInfo.CurrentUICulture = previousCulture;
      }
    }
  }
}
