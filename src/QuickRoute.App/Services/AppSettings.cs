using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickRoute.App.Services
{
  /// <summary>
  /// Per-user application settings. The file is created at runtime in the platform-specific configuration directory;
  /// nothing user-specific is shipped with the application.
  /// </summary>
  public sealed class AppSettings
  {
    private const int MaxRecentDocuments = 8;
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public string UiCulture { get; set; }

    public List<string> RecentDocuments { get; set; } = new List<string>();

    public Dictionary<string, string> LastFolders { get; set; } = new Dictionary<string, string>();

    public bool AutoAdjustColorRangeInterval { get; set; } = true;

    public bool RightPanelVisible { get; set; } = true;

    public bool BottomPanelVisible { get; set; } = true;

    public double ExportImagePercentualSize { get; set; } = 1;

    public double ExportImageQuality { get; set; } = 0.8;

    public string LastPersonName { get; set; }

    public int ExternalLapDataSourceIndex { get; set; }

    [JsonIgnore]
    public static string FileName => Path.Combine(AppPaths.ConfigDirectory, "settings.json");

    public static AppSettings Load()
    {
      try
      {
        if (File.Exists(FileName))
        {
          return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FileName), jsonOptions) ?? new AppSettings();
        }
      }
      catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
      {
        // fall back to default settings if the file is damaged or inaccessible
      }
      return new AppSettings();
    }

    public void Save()
    {
      try
      {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        var temporary = FileName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, jsonOptions));
        File.Move(temporary, FileName, true);
      }
      catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
      {
        // settings are a convenience; failing to save them must not stop the application
      }
    }

    public void AddRecentDocument(string fileName)
    {
      if (string.IsNullOrEmpty(fileName)) return;
      var fullPath = Path.GetFullPath(fileName);
      RecentDocuments.RemoveAll(f => string.Equals(f, fullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
      RecentDocuments.Insert(0, fullPath);
      if (RecentDocuments.Count > MaxRecentDocuments) RecentDocuments.RemoveRange(MaxRecentDocuments, RecentDocuments.Count - MaxRecentDocuments);
    }

    public string GetLastFolder(string key)
    {
      return LastFolders.TryGetValue(key, out var folder) && Directory.Exists(folder) ? folder : null;
    }

    public void SetLastFolder(string key, string fileName)
    {
      var folder = Path.GetDirectoryName(fileName);
      if (!string.IsNullOrEmpty(folder)) LastFolders[key] = folder;
    }
  }
}
