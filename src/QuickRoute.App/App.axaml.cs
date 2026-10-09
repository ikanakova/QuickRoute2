using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuickRoute.App.Services;

namespace QuickRoute.App
{
  public class App : Application
  {
    public override void Initialize()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
      var settings = AppSettings.Load();
      if (!string.IsNullOrEmpty(settings.UiCulture))
      {
        try
        {
          var culture = CultureInfo.GetCultureInfo(settings.UiCulture);
          CultureInfo.DefaultThreadCurrentUICulture = culture;
          Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
          settings.UiCulture = null;
        }
      }

      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
        var window = new MainWindow(settings);
        desktop.MainWindow = window;
        if (desktop.Args != null && desktop.Args.Length > 0) window.OpenFileOnStartup(desktop.Args[0]);
      }
      base.OnFrameworkInitializationCompleted();
    }
  }
}
