using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QuickRoute.App.Services;

namespace QuickRoute.App.Dialogs
{
  public enum MessageBoxResult
  {
    Ok,
    Cancel,
    Yes,
    No
  }

  /// <summary>
  /// Simple modal message boxes.
  /// </summary>
  public static class MessageBox
  {
    public static Task<MessageBoxResult> ShowError(Window owner, string title, string message, Exception exception = null)
    {
      var text = exception == null ? message : message + Environment.NewLine + Environment.NewLine + exception.Message;
      return Show(owner, title, text, MessageBoxResult.Ok);
    }

    public static Task<MessageBoxResult> ShowInfo(Window owner, string title, string message)
    {
      return Show(owner, title, message, MessageBoxResult.Ok);
    }

    public static Task<MessageBoxResult> Show(Window owner, string title, string message, params MessageBoxResult[] buttons)
    {
      var window = new Window
      {
        Title = title,
        SizeToContent = SizeToContent.WidthAndHeight,
        MinWidth = 320,
        MaxWidth = 640,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
      };
      var result = buttons.Length > 0 ? buttons[buttons.Length - 1] : MessageBoxResult.Ok;
      var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
      foreach (var button in buttons)
      {
        var b = new Button { Content = GetButtonText(button), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (button == MessageBoxResult.Ok || button == MessageBoxResult.Yes) b.IsDefault = true;
        if (button == MessageBoxResult.Cancel || (button == MessageBoxResult.Ok && buttons.Length == 1)) b.IsCancel = true;
        var value = button;
        b.Click += (_, _) =>
        {
          result = value;
          window.Close();
        };
        buttonPanel.Children.Add(b);
      }
      window.Content = new StackPanel
      {
        Margin = new Thickness(16),
        Spacing = 16,
        Children =
        {
          new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
          buttonPanel
        }
      };
      return ShowAndReturn(window, owner, () => result);
    }

    internal static string GetButtonText(MessageBoxResult button)
    {
      switch (button)
      {
        case MessageBoxResult.Cancel:
          return Loc.Plain("Button_Cancel");
        case MessageBoxResult.Yes:
          return Loc.Plain("Button_Yes");
        case MessageBoxResult.No:
          return Loc.Plain("Button_No");
        default:
          return Loc.Plain("Button_Ok");
      }
    }

    private static async Task<T> ShowAndReturn<T>(Window window, Window owner, Func<T> result)
    {
      await window.ShowDialog(owner);
      return result();
    }
  }

  /// <summary>
  /// A modal form with labelled rows and OK/Cancel buttons, built in code.
  /// </summary>
  public class FormDialog : Window
  {
    private readonly Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 12 };
    private readonly Button okButton;
    private bool accepted;

    public FormDialog(string title)
    {
      Title = title;
      SizeToContent = SizeToContent.WidthAndHeight;
      MinWidth = 380;
      CanResize = false;
      ShowInTaskbar = false;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      okButton = new Button { Content = Loc.Plain("Button_Ok"), IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
      var cancelButton = new Button { Content = Loc.Plain("Button_Cancel"), IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
      okButton.Click += (_, _) =>
      {
        if (Validate != null && !Validate()) return;
        accepted = true;
        Close();
      };
      cancelButton.Click += (_, _) => Close();
      Content = new StackPanel
      {
        Margin = new Thickness(16),
        Spacing = 16,
        Children =
        {
          grid,
          new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { okButton, cancelButton } }
        }
      };
    }

    /// <summary>
    /// Optional validation run when OK is clicked; returning false keeps the dialog open.
    /// </summary>
    public Func<bool> Validate { get; set; }

    public bool OkEnabled
    {
      get => okButton.IsEnabled;
      set => okButton.IsEnabled = value;
    }

    public T AddRow<T>(string label, T control) where T : Control
    {
      var row = grid.RowDefinitions.Count;
      grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
      if (label != null)
      {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
      }
      else
      {
        Grid.SetColumnSpan(control, 2);
      }
      Grid.SetRow(control, row);
      grid.Children.Add(control);
      return control;
    }

    public async Task<bool> ShowAsync(Window owner)
    {
      await ShowDialog(owner);
      return accepted;
    }

    public static ComboBox CreateComboBox<T>(IEnumerable<T> items, int selectedIndex = 0)
    {
      var combo = new ComboBox { ItemsSource = new List<T>(items), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
      if (selectedIndex >= 0) combo.SelectedIndex = selectedIndex;
      return combo;
    }
  }
}
