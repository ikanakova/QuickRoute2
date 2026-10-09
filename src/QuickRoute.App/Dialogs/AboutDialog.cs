using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using QuickRoute.App.Services;

namespace QuickRoute.App.Dialogs
{
  /// <summary>
  /// The About dialog: application name and version on the original QuickRoute background image.
  /// </summary>
  public class AboutDialog : Window
  {
    public AboutDialog()
    {
      Title = Loc.Plain("About_Title");
      Width = 484;
      Height = 183;
      CanResize = false;
      ShowInTaskbar = false;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      var closeButton = new Button
      {
        Name = "CloseButton",
        Content = Loc.Plain("About_Close"),
        MinWidth = 75,
        IsDefault = true,
        IsCancel = true,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 12, 8)
      };
      closeButton.Click += (_, _) => Close();

      var texts = new StackPanel
      {
        Margin = new Thickness(177, 24, 8, 0),
        Spacing = 6,
        Children =
        {
          new TextBlock { Name = "NameText", Text = Loc.S("QuickRoute"), FontSize = 19 },
          new TextBlock { Name = "VersionText", Text = string.Format(Loc.S("Version"), GetVersionString()), FontSize = 12 },
          new TextBlock { Name = "BasedOnText", Text = Loc.Plain("About_BasedOn"), FontSize = 12, TextWrapping = TextWrapping.Wrap }
        }
      };

      using var stream = AssetLoader.Open(new Uri("avares://" + typeof(AboutDialog).Assembly.GetName().Name + "/Assets/AboutBackground.png"));
      Content = new Panel
      {
        Background = new ImageBrush(new Bitmap(stream)) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
        Children = { texts, closeButton }
      };
    }

    public static string GetVersionString()
    {
      return typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "";
    }
  }
}
