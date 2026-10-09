using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuickRoute.App.Dialogs;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Exporters;
using QuickRoute.Rendering;

namespace QuickRoute.App
{
  public partial class MainWindow
  {
    private async Task<string> SelectExportFileAsync(string title, string winFormsFilter, string extension)
    {
      var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
      {
        Title = title,
        DefaultExtension = extension,
        ShowOverwritePrompt = true,
        SuggestedFileName = GetFileNameProposal() + "." + extension,
        SuggestedStartLocation = await GetFolderAsync("Export"),
        FileTypeChoices = new[] { FileTypeFromFilter(winFormsFilter) }
      });
      var fileName = file?.TryGetLocalPath();
      if (fileName == null) return null;
      if (!fileName.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) fileName += "." + extension;
      settings.SetLastFolder("Export", fileName);
      return fileName;
    }

    private async Task ExportImageAsync()
    {
      if (document == null) return;
      var dialog = new FormDialog(Loc.S("ExportImageTitle"));
      var format = dialog.AddRow(null, FormDialog.CreateComboBox(new[] { MainWindow.FileTypeFromFilter(Loc.S("FileFilter_JpegFiles")).Name, MainWindow.FileTypeFromFilter(Loc.S("FileFilter_PngFiles")).Name }));
      var size = dialog.AddRow(Loc.Plain("ExportImage_Size"), new NumericUpDown { Minimum = 10, Maximum = 400, Increment = 10, Value = (decimal)(100 * settings.ExportImagePercentualSize), FormatString = "0" });
      var quality = dialog.AddRow(Loc.Plain("ExportImage_Quality"), new NumericUpDown { Minimum = 10, Maximum = 100, Increment = 5, Value = (decimal)(100 * settings.ExportImageQuality), FormatString = "0" });
      var pixels = dialog.AddRow(null, new TextBlock());
      void UpdatePixels()
      {
        var s = document.CalculateImageForExportSize((double)(size.Value ?? 100) / 100);
        pixels.Text = string.Format(Loc.Plain("ExportImage_Pixels"), s.Width, s.Height);
        quality.IsEnabled = format.SelectedIndex == 0;
      }
      size.ValueChanged += (_, _) => UpdatePixels();
      format.SelectionChanged += (_, _) => UpdatePixels();
      UpdatePixels();
      if (!await dialog.ShowAsync(this)) return;

      var isJpeg = format.SelectedIndex == 0;
      var fileName = await SelectExportFileAsync(Loc.S("ExportImageTitle"), Loc.S(isJpeg ? "FileFilter_JpegFiles" : "FileFilter_PngFiles"), isJpeg ? "jpg" : "png");
      if (fileName == null) return;
      settings.ExportImagePercentualSize = (double)(size.Value ?? 100) / 100;
      if (isJpeg) settings.ExportImageQuality = (double)(quality.Value ?? 80) / 100;

      var exporter = new ImageExporter(document, GetSelectedSessions())
      {
        Properties = new ImageExporterProperties
        {
          Format = isJpeg ? ExportImageFormat.Jpeg : ExportImageFormat.Png,
          JpegQuality = settings.ExportImageQuality,
          RouteDrawingMode = Document.RouteDrawingMode.Extended,
          ColorCodingAttribute = SelectedColorCodingAttribute,
          SecondaryColorCodingAttribute = SelectedSecondaryColorCodingAttribute,
          PercentualSize = settings.ExportImagePercentualSize,
          ColorRangeProperties = GetCurrentColorRangeProperties(),
          SessionSettings = null
        }
      };
      await RunExportAsync(fileName, stream => exporter.Export(stream), "Error_ExportImage");
    }

    private async Task ExportGpxAsync()
    {
      if (CurrentSession == null) return;
      var fileName = await SelectExportFileAsync(Loc.S("ExportGPXTitle"), Loc.S("FileFilter_GpxFiles"), "gpx");
      if (fileName == null) return;
      var session = CurrentSession;
      await RunExportAsync(fileName, stream => new GpxExporter(session, stream).Export(), "Error_ExportRoute");
    }

    /// <summary>
    /// Exports a KMZ file. When <paramref name="openAfterExport"/> is set, the file is written to a temporary location and opened with the default application.
    /// </summary>
    private async Task ExportKmzAsync(bool openAfterExport)
    {
      if (document == null) return;
      var dialog = new FormDialog(Loc.S("ExportKMZTitle"));
      var mapType = dialog.AddRow(Loc.Plain("Kmz_Map"), FormDialog.CreateComboBox(new[] { Loc.Plain("Kmz_MapNone"), Loc.Plain("Kmz_MapOnly"), Loc.Plain("Kmz_MapAndRoute") }, 2));
      var routeType = dialog.AddRow(Loc.Plain("Kmz_Route"), FormDialog.CreateComboBox(new[] { Loc.Plain("Kmz_RouteNone"), Loc.Plain("Kmz_RouteMonochrome"), Loc.Plain("Kmz_RouteColorCoded") }, 0));
      var adapt = dialog.AddRow(null, new CheckBox { Content = Loc.Plain("Kmz_Adapt") });
      if (!await dialog.ShowAsync(this)) return;

      string fileName;
      if (openAfterExport)
      {
        var directory = Path.Combine(Path.GetTempPath(), "QuickRoute2");
        Directory.CreateDirectory(directory);
        fileName = Path.Combine(directory, string.Concat(GetFileNameProposal().Split(Path.GetInvalidFileNameChars())) + ".kmz");
      }
      else
      {
        fileName = await SelectExportFileAsync(Loc.S("ExportKMZTitle"), Loc.S("FileFilter_KmzFiles"), "kmz");
        if (fileName == null) return;
      }

      var properties = new KmlProperties
      {
        MapType = (KmlExportMapType)Math.Max(0, mapType.SelectedIndex),
        RouteType = (KmlExportRouteType)Math.Max(0, routeType.SelectedIndex),
        RouteAdaptionStyle = adapt.IsChecked == true ? KmlRouteAdaptationStyle.AdaptToSessionMapImage : KmlRouteAdaptationStyle.NoAdaption
      };
      var imageExporter = new ImageExporter(document, GetSelectedSessions())
      {
        Properties = new ImageExporterProperties
        {
          ColorCodingAttribute = SelectedColorCodingAttribute,
          SecondaryColorCodingAttribute = SelectedSecondaryColorCodingAttribute,
          ColorRangeProperties = GetCurrentColorRangeProperties(),
          SessionSettings = null
        }
      };
      if (await RunExportAsync(fileName, stream => new KmzExporter(document, imageExporter, properties).Export(stream), "Error_ExportRoute") && openAfterExport)
      {
        try
        {
          Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
          await MessageBox.ShowError(this, Loc.S("QuickRoute"), Loc.Plain("Error_OpenFile"), ex);
        }
      }
    }

    private async Task ExportRouteDataAsync()
    {
      if (CurrentSession == null) return;
      var exportSettings = new ExportRouteDataSettings();
      var dialog = new FormDialog(Loc.S("ExportRouteDataTitle"));
      var interval = dialog.AddRow(Loc.Plain("RouteData_SamplingInterval"), new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 1, FormatString = "0" });
      var list = new StackPanel { Spacing = 2 };
      foreach (var type in exportSettings.RoutePropertyTypes)
      {
        var check = new CheckBox { Content = type.ToString(), IsChecked = type.Selected, Tag = type };
        list.Children.Add(check);
      }
      dialog.AddRow(null, new ScrollViewer { Content = list, Height = 320 });
      if (!await dialog.ShowAsync(this)) return;
      foreach (var check in list.Children.OfType<CheckBox>()) ((QuickRoute.BusinessEntities.RouteProperties.SelectableRoutePropertyType)check.Tag).Selected = check.IsChecked == true;
      exportSettings.SamplingInterval = TimeSpan.FromSeconds((double)(interval.Value ?? 1));

      var fileName = await SelectExportFileAsync(Loc.S("ExportRouteDataTitle"), Loc.S("FileFilter_RouteDataFiles"), "xml");
      if (fileName == null) return;
      var session = CurrentSession;
      await RunExportAsync(fileName, stream => new RouteDataExporter(session, stream) { Settings = exportSettings }.Export(), "Error_ExportRoute");
    }

    private async Task<bool> RunExportAsync(string fileName, Action<Stream> export, string errorKey)
    {
      var temporary = fileName + ".tmp";
      try
      {
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Wait);
        await Task.Run(() =>
        {
          using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
          {
            export(stream);
          }
          File.Move(temporary, fileName, true);
        });
        return true;
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(this, Loc.S("QuickRoute"), Loc.S(errorKey), ex);
        return false;
      }
      finally
      {
        if (File.Exists(temporary)) File.Delete(temporary);
        Cursor = Avalonia.Input.Cursor.Default;
      }
    }

    private SessionCollection GetSelectedSessions()
    {
      return Canvas.SessionsToDraw != null && Canvas.SessionsToDraw.Count > 0 ? Canvas.SessionsToDraw : document.Sessions;
    }

    private async Task AddLapsFromExternalDataSourceAsync()
    {
      if (CurrentSession == null) return;
      var dialog = new ExternalLapsDialog(CurrentSession.Route.FirstWaypoint.Time.ToLocalTime().Date, settings);
      var runner = await dialog.ShowAsync(this);
      if (runner == null) return;
      var session = CurrentSession;
      var action = new QuickRoute.BusinessEntities.Actions.TrimRouteAndAddLapsAction(
        runner.StartTime.HasValue ? runner.StartTime.Value.ToUniversalTime() : (DateTime?)null,
        runner.FinishTime.HasValue ? runner.FinishTime.Value.ToUniversalTime() : (DateTime?)null,
        new System.Collections.Generic.List<Lap>(),
        session);
      if (session.Route.FirstWaypoint != null && session.Route.LastWaypoint != null)
      {
        foreach (var dt in runner.Splits)
        {
          var utc = dt.ToUniversalTime();
          if (!session.Laps.Any(l => l.Time == utc)) action.Laps.Add(new Lap(utc, LapType.Lap));
        }
      }
      action.Execute();
      session.CreateAdjustedRoute();
      HandleAction(action);
    }
  }
}
