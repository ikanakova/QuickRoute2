using System.Globalization;
using System.Resources;
using QuickRoute.Resources;

namespace QuickRoute.App.Services
{
  /// <summary>
  /// Access to localized user interface texts.
  /// </summary>
  public static class Loc
  {
    private static readonly ResourceManager mainWindowStrings =
      new ResourceManager("QuickRoute.App.Resources.MainWindowStrings", typeof(Loc).Assembly);

    /// <summary>
    /// Gets a main window text; WinForms access key markers (&amp;) are converted to Avalonia access keys (_).
    /// </summary>
    public static string Ui(string key)
    {
      var value = mainWindowStrings.GetString(key, CultureInfo.CurrentUICulture) ?? key;
      return value.Replace("_", "__").Replace("&&", "\u0001").Replace('&', '_').Replace("\u0001", "&");
    }

    /// <summary>
    /// Gets a main window text without access key markers, e.g. for tooltips.
    /// </summary>
    public static string Plain(string key)
    {
      var value = mainWindowStrings.GetString(key, CultureInfo.CurrentUICulture) ?? key;
      return value.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
    }

    /// <summary>
    /// Gets a shared QuickRoute text.
    /// </summary>
    public static string S(string key)
    {
      return Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
  }
}
