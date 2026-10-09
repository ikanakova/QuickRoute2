using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Importers;
using QuickRoute.Persistence;
using SkiaSharp;

namespace QuickRoute.App.Dialogs
{
  public sealed class NewDocumentResult
  {
    public Map Map { get; set; }
    public ImportResult ImportResult { get; set; }
    public Transformation InitialTransformation { get; set; }
    public string PersonName { get; set; }
    public string FileNameProposal { get; set; }
  }

  /// <summary>
  /// Creates a new document from a map image (image, QuickRoute document or KMZ) and a route file.
  /// </summary>
  public class NewDocumentDialog : FormDialog
  {
    private readonly DocumentFileService documentFileService;
    private readonly AppSettings settings;
    private readonly TextBox mapBox = new TextBox { MinWidth = 320 };
    private readonly TextBox routeBox = new TextBox { MinWidth = 320 };
    private readonly TextBox personBox = new TextBox();

    public NewDocumentDialog(DocumentFileService documentFileService, AppSettings settings, string routeFileName)
      : base(Loc.Plain("NewDocument_Title"))
    {
      this.documentFileService = documentFileService;
      this.settings = settings;
      routeBox.Text = routeFileName;
      personBox.Text = settings.LastPersonName ?? "";
      AddRow(Loc.Plain("NewDocument_Map"), WithBrowse(mapBox, BrowseMapAsync));
      AddRow(null, new TextBlock { Text = Loc.Plain("NewDocument_BlankMapHint"), Opacity = 0.7 });
      AddRow(Loc.Plain("NewDocument_Route"), WithBrowse(routeBox, BrowseRouteAsync));
      AddRow(Loc.Plain("NewDocument_Person"), personBox);
      Validate = () => !string.IsNullOrWhiteSpace(routeBox.Text);
    }

    public new async Task<NewDocumentResult> ShowAsync(Window owner)
    {
      if (!await base.ShowAsync(owner)) return null;
      return await CreateResultAsync(owner);
    }

    private async Task<NewDocumentResult> CreateResultAsync(Window owner)
    {
      var result = new NewDocumentResult { PersonName = personBox.Text?.Trim() };
      try
      {
        var mapFile = mapBox.Text?.Trim();
        if (string.IsNullOrEmpty(mapFile))
        {
          result.Map = await Task.Run(CreateBlankMap);
        }
        else
        {
          await Task.Run(() => LoadMap(mapFile, result));
        }
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(owner, Loc.S("QuickRoute"), Loc.Plain("Error_MapImage"), ex);
        return null;
      }
      if (result.Map == null)
      {
        await MessageBox.ShowError(owner, Loc.S("QuickRoute"), Loc.Plain("Error_MapImage"));
        return null;
      }

      result.ImportResult = await RouteImportHelper.ImportAsync(owner, routeBox.Text.Trim(), documentFileService);
      if (result.ImportResult == null) return null;
      result.FileNameProposal = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(mapBox.Text) ? routeBox.Text : mapBox.Text);
      return result;
    }

    private void LoadMap(string fileName, NewDocumentResult result)
    {
      var service = documentFileService.MapImageService;
      var format = DocumentFileService.DetectFormat(fileName);
      if (format == QuickRouteFileFormat.Qrtx || format == QuickRouteFileFormat.Qrt)
      {
        var d = documentFileService.Open(fileName);
        result.Map = d.Map;
        result.InitialTransformation = d.Sessions.CalculateAverageTransformation();
        return;
      }
      if (string.Equals(Path.GetExtension(fileName), ".kmz", StringComparison.OrdinalIgnoreCase))
      {
        using var stream = File.OpenRead(fileName);
        var kmz = KmzMapReader.Read(stream, service);
        result.Map = kmz?.Map;
        result.InitialTransformation = kmz?.Transformation;
        return;
      }
      var data = File.ReadAllBytes(fileName);
      result.Map = service.CreateMap(data, fileName);
      // a QuickRoute image contains the transformation of the original document
      var ed = QuickRouteJpegExtensionData.FromStream(new MemoryStream(data, false));
      if (ed?.Sessions != null && ed.Sessions.Count > 0) result.InitialTransformation = ed.Sessions.CalculateAverageTransformation();
    }

    private static Map CreateBlankMap()
    {
      using var bitmap = new SKBitmap(1500, 1500, SKColorType.Rgba8888, SKAlphaType.Opaque);
      bitmap.Erase(SKColors.White);
      return new Map(QuickRoute.Rendering.SkiaUtil.Encode(bitmap, SKEncodedImageFormat.Png, 100), new System.Drawing.Size(1500, 1500));
    }

    private async Task BrowseMapAsync()
    {
      var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = Loc.Plain("NewDocument_SelectMap"),
        AllowMultiple = false,
        SuggestedStartLocation = settings.GetLastFolder("Map") is string folder ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null,
        FileTypeFilter = new[]
        {
          new FilePickerFileType(Loc.Plain("FileType_Images")) { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp" } },
          new FilePickerFileType(Loc.Plain("FileType_AllQuickRoute")) { Patterns = new[] { "*.qrtx", "*.qrt" } },
          MainWindow.FileTypeFromFilter(Loc.S("FileFilter_KmzFiles")),
          MainWindow.FileTypeFromFilter(Loc.S("FileFilter_AllFiles"))
        }
      });
      var fileName = files.FirstOrDefault()?.TryGetLocalPath();
      if (fileName == null) return;
      settings.SetLastFolder("Map", fileName);
      mapBox.Text = fileName;
    }

    private async Task BrowseRouteAsync()
    {
      var fileName = await RouteImportHelper.SelectRouteFileAsync(this, documentFileService, settings);
      if (fileName != null) routeBox.Text = fileName;
    }

    private static Control WithBrowse(TextBox box, Func<Task> browse)
    {
      var button = new Button { Content = Loc.Plain("Button_Browse") };
      button.Click += async (_, _) => await browse();
      var panel = new DockPanel();
      DockPanel.SetDock(button, Dock.Right);
      button.Margin = new Avalonia.Thickness(6, 0, 0, 0);
      panel.Children.Add(button);
      panel.Children.Add(box);
      return panel;
    }
  }

  /// <summary>
  /// Edits a date and time in local time; the result is returned as UTC.
  /// </summary>
  public static class TimeDialog
  {
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public static async Task<DateTime?> ShowAsync(Window owner, string title, DateTime utcTime)
    {
      var dialog = new FormDialog(title);
      var box = dialog.AddRow(Loc.Plain("Time_Label"), new TextBox { Text = utcTime.ToLocalTime().ToString(Format, CultureInfo.InvariantCulture), MinWidth = 200 });
      DateTime parsed = default;
      dialog.Validate = () =>
        DateTime.TryParseExact(box.Text?.Trim(), new[] { Format, "yyyy-MM-dd HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed);
      if (!await dialog.ShowAsync(owner)) return null;
      if (parsed.Date == DateTime.MinValue.Date || box.Text.Trim().Length <= 8) parsed = utcTime.ToLocalTime().Date + parsed.TimeOfDay;
      return parsed.ToUniversalTime();
    }
  }

  public static class CutDialog
  {
    public static async Task<(DateTime Time, CutType CutType)?> ShowAsync(Window owner, DateTime utcTime, CutType cutType)
    {
      var dialog = new FormDialog(Loc.Plain("Cut_Title"));
      var box = dialog.AddRow(Loc.Plain("Time_Label"), new TextBox { Text = utcTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) });
      var before = dialog.AddRow(null, new RadioButton { Content = Loc.Plain("Cut_Before"), GroupName = "cut", IsChecked = cutType == CutType.Before });
      dialog.AddRow(null, new RadioButton { Content = Loc.Plain("Cut_After"), GroupName = "cut", IsChecked = cutType == CutType.After });
      DateTime parsed = default;
      dialog.Validate = () => DateTime.TryParseExact(box.Text?.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed);
      if (!await dialog.ShowAsync(owner)) return null;
      return (parsed.ToUniversalTime(), before.IsChecked == true ? CutType.Before : CutType.After);
    }
  }
}
