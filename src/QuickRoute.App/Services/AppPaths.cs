using System;
using System.IO;

namespace QuickRoute.App.Services
{
  /// <summary>
  /// Platform-specific locations for per-user files. On Linux the XDG base directories are honoured.
  /// The QUICKROUTE2_DATA_HOME environment variable can be used to select an isolated directory, e.g. for portable use or testing.
  /// </summary>
  public static class AppPaths
  {
    private const string ApplicationFolderName = "QuickRoute2";

    public static string ConfigDirectory => Resolve("XDG_CONFIG_HOME", Environment.SpecialFolder.ApplicationData, ".config");

    public static string DataDirectory => Resolve("XDG_DATA_HOME", Environment.SpecialFolder.LocalApplicationData, Path.Combine(".local", "share"));

    private static string Resolve(string xdgVariable, Environment.SpecialFolder folder, string homeRelativeFallback)
    {
      var overrideDirectory = Environment.GetEnvironmentVariable("QUICKROUTE2_DATA_HOME");
      if (!string.IsNullOrEmpty(overrideDirectory)) return Path.GetFullPath(overrideDirectory);

      if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
      {
        var xdg = Environment.GetEnvironmentVariable(xdgVariable);
        if (!string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)) return Path.Combine(xdg, ApplicationFolderName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) return Path.Combine(home, homeRelativeFallback, ApplicationFolderName);
      }
      var basePath = Environment.GetFolderPath(folder);
      if (string.IsNullOrEmpty(basePath)) basePath = Path.GetTempPath();
      return Path.Combine(basePath, ApplicationFolderName);
    }
  }
}
