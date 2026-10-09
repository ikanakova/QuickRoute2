using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuickRoute.App.Dialogs;
using QuickRoute.BusinessEntities.Importers;
using QuickRoute.BusinessEntities.Importers.FIT;
using QuickRoute.BusinessEntities.Importers.FRWD;
using QuickRoute.BusinessEntities.Importers.GPX;
using QuickRoute.BusinessEntities.Importers.Navilock;
using QuickRoute.BusinessEntities.Importers.TCX;
using QuickRoute.Persistence;

namespace QuickRoute.App.Services
{
  /// <summary>
  /// Selects a route file and imports it, asking the user to choose between alternatives when the file contains several activities.
  /// </summary>
  public static class RouteImportHelper
  {
    public static IReadOnlyList<(string Filter, Func<IRouteFileImporter> Create)> GetRouteFileFormats(DocumentFileService documentFileService)
    {
      return new (string, Func<IRouteFileImporter>)[]
      {
        (Loc.S("FileFilter_GpxFiles"), () => new GPXImporter()),
        ("QuickRoute2 / QuickRoute (*.qrtx; *.qrt; *.jpg; *.jpeg)|*.qrtx;*.qrt;*.jpg;*.jpeg", () => new QuickRouteImporter(documentFileService)),
        (Loc.S("FileFilter_FitFiles"), () => new FITImporter()),
        (Loc.S("FileFilter_TcxFiles"), () => new TCXImporter()),
        (Loc.S("FileFilter_FrwdTxtFiles"), () => new FRWDImporter()),
        (Loc.S("FileFilter_NavilockActFiles"), () => new NavilockImporter())
      };
    }

    public static IReadOnlyList<FilePickerFileType> GetFileTypes(DocumentFileService documentFileService)
    {
      var formats = GetRouteFileFormats(documentFileService);
      var all = new FilePickerFileType(Loc.Plain("FileType_AllRoutes"))
      {
        Patterns = formats.SelectMany(f => MainWindow.FileTypeFromFilter(f.Filter).Patterns).Distinct().ToList()
      };
      return new[] { all }.Concat(formats.Select(f => MainWindow.FileTypeFromFilter(f.Filter))).ToList();
    }

    public static IRouteFileImporter CreateImporter(string fileName, DocumentFileService documentFileService)
    {
      var extension = Path.GetExtension(fileName);
      foreach (var format in GetRouteFileFormats(documentFileService))
      {
        foreach (var pattern in MainWindow.FileTypeFromFilter(format.Filter).Patterns)
        {
          if (string.Equals(pattern.TrimStart('*'), extension, StringComparison.OrdinalIgnoreCase))
          {
            var importer = format.Create();
            importer.FileName = fileName;
            return importer;
          }
        }
      }
      return null;
    }

    public static async Task<string> SelectRouteFileAsync(Window owner, DocumentFileService documentFileService, AppSettings settings)
    {
      var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = Loc.Plain("NewDocument_SelectRoute"),
        AllowMultiple = false,
        FileTypeFilter = GetFileTypes(documentFileService),
        SuggestedStartLocation = settings.GetLastFolder("Route") is string folder ? await owner.StorageProvider.TryGetFolderFromPathAsync(folder) : null
      });
      var fileName = files.FirstOrDefault()?.TryGetLocalPath();
      if (fileName != null) settings.SetLastFolder("Route", fileName);
      return fileName;
    }

    public static async Task<ImportResult> ImportAsync(Window owner, string fileName, DocumentFileService documentFileService)
    {
      var importer = CreateImporter(fileName, documentFileService);
      if (importer == null)
      {
        await MessageBox.ShowError(owner, Loc.S("QuickRoute"), string.Format(Loc.S("UnrecognizedFileFormat"), Path.GetFileName(fileName)));
        return null;
      }
      try
      {
        var choices = await Task.Run(() => importer.GetImportChoices());
        if (choices.Count == 1)
        {
          importer.SelectedChoice = choices[0];
        }
        else if (choices.Count > 1)
        {
          var dialog = new FormDialog(Loc.Plain("ImportChoice_Title"));
          var combo = dialog.AddRow(Loc.Plain("ImportChoice_Label"), FormDialog.CreateComboBox(choices));
          if (!await dialog.ShowAsync(owner)) return null;
          importer.SelectedChoice = choices[Math.Max(0, combo.SelectedIndex)];
        }
        await Task.Run(() => importer.Import());
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(owner, Loc.S("QuickRoute"), Loc.S("InvalidRoute"), ex);
        return null;
      }
      var result = importer.ImportResult;
      if (result == null || !result.Succeeded)
      {
        await MessageBox.ShowError(owner, Loc.S("QuickRoute"), result?.ErrorMessage ?? Loc.S("InvalidRoute"), result?.Exception);
        return null;
      }
      return result;
    }

    public static async Task<ImportResult> SelectAndImportRouteAsync(Window owner, DocumentFileService documentFileService, AppSettings settings)
    {
      var fileName = await SelectRouteFileAsync(owner, documentFileService, settings);
      return fileName == null ? null : await ImportAsync(owner, fileName, documentFileService);
    }
  }
}
