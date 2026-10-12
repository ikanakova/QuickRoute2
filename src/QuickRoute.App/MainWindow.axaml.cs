using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using QuickRoute.App.Controls;
using QuickRoute.App.Dialogs;
using QuickRoute.App.Services;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Actions;
using QuickRoute.Persistence;
using QuickRoute.Rendering;

namespace QuickRoute.App
{
  public partial class MainWindow : Window
  {
    private static readonly double[] zoomPresets = { 0.5, 0.75, 1, 1.25, 1.5, 2 };

    private readonly AppSettings settings;
    private readonly SkiaMapImageService mapImageService = new SkiaMapImageService();
    private readonly DocumentFileService documentFileService;
    private readonly Stack<IAction> undoStack = new Stack<IAction>();
    private readonly Stack<IAction> redoStack = new Stack<IAction>();
    private Document document;
    private bool documentChanged;
    private string untitledFileNameProposal;
    private int updatingUi;
    private WindowState stateBeforeFullScreen = WindowState.Normal;

    public MainWindow() : this(AppSettings.Load())
    {
    }

    public MainWindow(AppSettings settings)
    {
      this.settings = settings;
      documentFileService = new DocumentFileService(mapImageService);
      InitializeComponent();
      Canvas.ScrollViewer = MapScrollViewer;
      Canvas.ActionPerformed += (_, action) => HandleAction(action);
      Canvas.RouteMouseHover += Canvas_RouteMouseHover;
      Canvas.ZoomChanged += (_, _) => UpdateZoomBox();
      Canvas.LapTimeEditor = EditLapTimeAsync;
      Canvas.CutEditor = EditCutAsync;
      LineGraphView.GraphHover += LineGraphView_GraphHover;
      SetTexts();
      WireEvents();
      AddHandler(DragDrop.DropEvent, OnDrop);
      AddHandler(DragDrop.DragOverEvent, OnDragOver);
      Closing += MainWindow_Closing;
      ApplyPanelVisibility();
      UpdateUi();
    }

    private bool UpdatingUi => updatingUi > 0;

    private Session CurrentSession => Canvas.CurrentSession;

    public void OpenFileOnStartup(string fileName)
    {
      Avalonia.Threading.Dispatcher.UIThread.Post(async () => await OpenDocumentAsync(fileName));
    }

    #region Initialization

    private void SetTexts()
    {
      MenuFile.Header = Loc.Ui("Menu_File");
      MenuFileNew.Header = Loc.Ui("Menu_FileNew");
      MenuFileOpen.Header = Loc.Ui("Menu_FileOpen");
      MenuFileRecent.Header = Loc.Ui("Menu_FileRecent");
      MenuFileSave.Header = Loc.Ui("Menu_FileSave");
      MenuFileSaveAs.Header = Loc.Ui("Menu_FileSaveAs");
      MenuFileImportSessions.Header = Loc.Ui("Menu_FileImportSessions");
      MenuFileExport.Header = Loc.Ui("Menu_FileExport");
      MenuFileExportImage.Header = Loc.Ui("Menu_FileExportImage");
      MenuFileExportGpx.Header = Loc.Ui("Menu_FileExportGpx");
      MenuFileExportKmz.Header = Loc.Ui("Menu_FileExportKmz");
      MenuFileExportRouteData.Header = Loc.Ui("Menu_FileExportRouteData");
      MenuFileExit.Header = Loc.Ui("Menu_FileExit");
      MenuEdit.Header = Loc.Ui("Menu_Edit");
      MenuEditUndo.Header = Loc.Ui("Menu_EditUndo");
      MenuEditRedo.Header = Loc.Ui("Menu_EditRedo");
      MenuEditChangeStartTime.Header = Loc.Ui("Menu_EditChangeStartTime");
      MenuView.Header = Loc.Ui("Menu_View");
      MenuViewFullScreen.Header = Loc.Ui("Menu_ViewFullScreen");
      MenuViewRightPanel.Header = Loc.Ui("Menu_ViewRightPanel");
      MenuViewBottomPanel.Header = Loc.Ui("Menu_ViewBottomPanel");
      MenuImage.Header = Loc.Ui("Menu_Image");
      MenuImageRotateClockwise.Header = Loc.Ui("Menu_ImageRotateClockwise");
      MenuImageRotateCounterclockwise.Header = Loc.Ui("Menu_ImageRotateCounterclockwise");
      MenuTools.Header = Loc.Ui("Menu_Tools");
      MenuToolsAddLaps.Header = Loc.Ui("Menu_ToolsAddLaps");
      MenuToolsOpenExportedKmz.Header = Loc.Ui("Menu_ToolsOpenExportedKmz");
      MenuSettings.Header = Loc.Ui("Menu_Settings");
      MenuSettingsLanguage.Header = Loc.Ui("Menu_SettingsLanguage");
      MenuHelp.Header = Loc.Ui("Menu_Help");
      MenuHelpHelp.Header = Loc.Ui("Menu_HelpHelp");
      MenuHelpAbout.Header = Loc.Ui("Menu_HelpAbout");
      LapsLabel.Text = Loc.Plain("Label_SplitTimes");

      SetTip(ToolNew, "Tip_New");
      SetTip(ToolOpen, "Tip_Open");
      SetTip(ToolSave, "Tip_Save");
      SetTip(ToolUndo, "Tip_Undo");
      SetTip(ToolRedo, "Tip_Redo");
      SetTip(ZoomBox, "Tip_Zoom");
      SetTip(ToolPointer, "Tip_Pointer");
      SetTip(ToolAdjustRoute, "Tip_AdjustRoute");
      SetTip(ToolLap, "Tip_Lap");
      SetTip(ToolCut, "Tip_Cut");
      SetTip(ToolZoomIn, "Tip_ZoomIn");
      SetTip(ToolZoomOut, "Tip_ZoomOut");
      SetTip(ToolFullScreen, "Tip_FullScreen");
      SetTip(ToolRightPanel, "Tip_RightPanel");
      SetTip(ToolBottomPanel, "Tip_BottomPanel");
      SetTip(ToolHelp, "Tip_Help");
      SetTip(ColorCodingAttributeBox, "Tip_ColorCodingAttribute");
      SetTip(ColorRangeStartBox, "Tip_ColorRangeStart");
      SetTip(ColorRangeEndBox, "Tip_ColorRangeEnd");
      SetTip(ColorRangeIntervalButton, "Tip_ColorRangeInterval");
      SetTip(AutoAdjustColorRangeButton, "Tip_AutoAdjustColorRange");
      SetTip(TransparencySlider, "Tip_Transparency");
      SetTip(LineWidthBox, "Tip_LineWidth");
      SetTip(MaskVisibleButton, "Tip_MaskVisible");
      SetTip(MaskWidthBox, "Tip_MaskWidth");
      SetTip(MaskColorButton, "Tip_MaskColor");
      SetTip(SecondaryColorCodingAttributeBox, "Tip_SecondaryColorCoding");
      SetTip(SmoothingIntervalBox, "Tip_SmoothingInterval");
      SetTip(CircleTimeRadiusBox, "Tip_CircleTimeRadius");

      ZoomBox.ItemsSource = zoomPresets.Select(z => z.ToString("P0", CultureInfo.CurrentCulture)).ToList();
      ColorCodingAttributeBox.ItemsSource = ColorCodingAttributes.Select(a => new WaypointAttributeString(a)).ToList();
      SecondaryColorCodingAttributeBox.ItemsSource = new WaypointAttribute?[] { null }.Concat(ColorCodingAttributes.Select(a => (WaypointAttribute?)a))
        .Select(a => new WaypointAttributeString(a)).ToList();
    }

    private static void SetTip(Control control, string key) => ToolTip.SetTip(control, Loc.Plain(key));

    private void WireEvents()
    {
      MenuFileNew.Click += async (_, _) => await NewDocumentAsync(null);
      MenuFileOpen.Click += async (_, _) => await OpenDocumentShowDialogAsync();
      MenuFileSave.Click += async (_, _) => await SaveDocumentAsync();
      MenuFileSaveAs.Click += async (_, _) => await SaveDocumentShowDialogAsync();
      MenuFileImportSessions.Click += async (_, _) => await ImportSessionsAsync();
      MenuFileExportImage.Click += async (_, _) => await ExportImageAsync();
      MenuFileExportGpx.Click += async (_, _) => await ExportGpxAsync();
      MenuFileExportKmz.Click += async (_, _) => await ExportKmzAsync(false);
      MenuFileExportRouteData.Click += async (_, _) => await ExportRouteDataAsync();
      MenuFileExit.Click += (_, _) => Close();
      MenuEditUndo.Click += (_, _) => Undo();
      MenuEditRedo.Click += (_, _) => Redo();
      MenuEditChangeStartTime.Click += async (_, _) => await ChangeStartTimeAsync();
      MenuViewFullScreen.Click += (_, _) => ToggleFullScreen();
      MenuViewRightPanel.Click += (_, _) => TogglePanel(right: true);
      MenuViewBottomPanel.Click += (_, _) => TogglePanel(right: false);
      MenuImageRotateClockwise.Click += (_, _) => RotateMapImage(-Math.PI / 2);
      MenuImageRotateCounterclockwise.Click += (_, _) => RotateMapImage(Math.PI / 2);
      MenuToolsAddLaps.Click += async (_, _) => await AddLapsFromExternalDataSourceAsync();
      MenuToolsOpenExportedKmz.Click += async (_, _) => await ExportKmzAsync(true);
      MenuSettingsLanguage.Click += async (_, _) => await SelectLanguageAsync();
      MenuHelpHelp.Click += (_, _) => OpenUrl(Loc.S("HelpUrl"));
      MenuHelpAbout.Click += async (_, _) => await ShowAboutAsync();

      ToolNew.Click += async (_, _) => await NewDocumentAsync(null);
      ToolOpen.Click += async (_, _) => await OpenDocumentShowDialogAsync();
      ToolSave.Click += async (_, _) => await SaveDocumentAsync();
      ToolUndo.Click += (_, _) => Undo();
      ToolRedo.Click += (_, _) => Redo();
      ToolPointer.Click += (_, _) => SetMouseTool(MouseTool.Pointer);
      ToolAdjustRoute.Click += (_, _) => SetMouseTool(MouseTool.AdjustRoute);
      ToolLap.Click += (_, _) => SetMouseTool(MouseTool.Lap);
      ToolCut.Click += (_, _) => SetMouseTool(MouseTool.Cut);
      ToolZoomIn.Click += (_, _) => SetMouseTool(MouseTool.ZoomIn);
      ToolZoomOut.Click += (_, _) => SetMouseTool(MouseTool.ZoomOut);
      ToolFullScreen.Click += (_, _) => ToggleFullScreen();
      ToolRightPanel.Click += (_, _) => TogglePanel(right: true);
      ToolBottomPanel.Click += (_, _) => TogglePanel(right: false);
      ToolHelp.Click += (_, _) => OpenUrl(Loc.S("HelpUrl"));
      ZoomBox.SelectionChanged += (_, _) =>
      {
        if (UpdatingUi || ZoomBox.SelectedIndex < 0) return;
        Canvas.Zoom = zoomPresets[ZoomBox.SelectedIndex];
      };

      WireRouteAppearanceEvents();
      WirePanelEvents();
      KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
      var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
      var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
      if (ctrl && !shift)
      {
        switch (e.Key)
        {
          case Key.N: _ = NewDocumentAsync(null); e.Handled = true; break;
          case Key.O: _ = OpenDocumentShowDialogAsync(); e.Handled = true; break;
          case Key.S: _ = SaveDocumentAsync(); e.Handled = true; break;
          case Key.Z: Undo(); e.Handled = true; break;
          case Key.Y: Redo(); e.Handled = true; break;
          case Key.OemPlus:
          case Key.Add: Canvas.Zoom *= 4.0 / 3; e.Handled = true; break;
          case Key.OemMinus:
          case Key.Subtract: Canvas.Zoom *= 0.75; e.Handled = true; break;
        }
      }
      else if (ctrl && shift && e.Key == Key.R)
      {
        TogglePanel(right: true);
        e.Handled = true;
      }
      else if (ctrl && shift && e.Key == Key.B)
      {
        TogglePanel(right: false);
        e.Handled = true;
      }
      else if (e.Key == Key.F11)
      {
        ToggleFullScreen();
        e.Handled = true;
      }
      else if (e.Key == Key.F1)
      {
        OpenUrl(Loc.S("HelpUrl"));
        e.Handled = true;
      }
    }

    #endregion

    #region Document lifecycle

    private async Task NewDocumentAsync(string routeFileName)
    {
      if (!await QuerySaveDocumentAsync()) return;
      var dialog = new NewDocumentDialog(documentFileService, settings, routeFileName);
      var result = await dialog.ShowAsync(this);
      if (result == null) return;
      try
      {
        var newDocument = new Document(result.Map, result.ImportResult.Route, result.ImportResult.Laps,
          result.InitialTransformation?.TransformationMatrix, result.InitialTransformation?.ProjectionOrigin, new DocumentSettings());
        newDocument.Sessions[0].SessionInfo.Person = new SessionPerson { Name = result.PersonName };
        settings.LastPersonName = result.PersonName;
        untitledFileNameProposal = result.FileNameProposal;
        SetDocument(newDocument);
        InitColorRangeIntervals();
        RefreshAll();
        documentChanged = true;
        SetMouseTool(MouseTool.AdjustRoute);
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(this, Loc.S("QuickRoute"), Loc.S("Error_NewDocument"), ex);
      }
      UpdateUi();
    }

    private async Task OpenDocumentShowDialogAsync()
    {
      if (!await QuerySaveDocumentAsync()) return;
      var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = Loc.S("OpenQuickRouteFileTitle"),
        AllowMultiple = false,
        SuggestedStartLocation = await GetFolderAsync("Open"),
        FileTypeFilter = new[]
        {
          new FilePickerFileType(Loc.Plain("FileType_AllQuickRoute")) { Patterns = new[] { "*.qrtx", "*.qrt", "*.jpg", "*.jpeg", "*.xml" } },
          new FilePickerFileType(Loc.Plain("FileType_Qrtx")) { Patterns = new[] { "*.qrtx" } },
          FileTypeFromFilter(Loc.S("FileFilter_QuickRouteFiles")),
          FileTypeFromFilter(Loc.S("FileFilter_JpegFilesExportedFromQuickRoute")),
          FileTypeFromFilter(Loc.S("FileFilter_AllFiles"))
        }
      });
      var fileName = files.FirstOrDefault()?.TryGetLocalPath();
      if (fileName == null) return;
      settings.SetLastFolder("Open", fileName);
      await OpenDocumentAsync(fileName, false);
    }

    private async Task OpenDocumentAsync(string fileName, bool querySave = true)
    {
      if (querySave && !await QuerySaveDocumentAsync()) return;
      try
      {
        var opened = await Task.Run(() => documentFileService.Open(fileName));
        if (opened == null)
        {
          await MessageBox.ShowInfo(this, Loc.S("QuickRoute"), Loc.Plain("Info_NotAQuickRouteImage"));
          return;
        }
        untitledFileNameProposal = Path.GetFileNameWithoutExtension(fileName);
        SetDocument(opened);
        documentChanged = false;
        settings.AddRecentDocument(fileName);
        settings.Save();
        SetMouseTool(MouseTool.Pointer);
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(this, Loc.S("QuickRoute"), Loc.S("Error_OpenDocument"), ex);
      }
      UpdateUi();
    }

    private void SetDocument(Document newDocument)
    {
      document = newDocument;
      undoStack.Clear();
      redoStack.Clear();
      updatingUi++;
      Canvas.Document = document;
      Canvas.SessionsToDraw = document == null ? new SessionCollection() : new SessionCollection { document.Sessions[0] };
      updatingUi--;
      if (document != null)
      {
        PopulateSessionList();
        RefreshAll();
      }
      UpdateZoomBox();
    }

    /// <summary>
    /// Saves the document. Documents that are not in the current format are saved as a new .qrtx file so that the original is never overwritten.
    /// </summary>
    private async Task<bool> SaveDocumentAsync()
    {
      if (document == null) return false;
      if (document.FileName == null || document.FileFormat != QuickRouteFileFormat.Qrtx)
      {
        if (document.FileName != null && document.FileFormat != QuickRouteFileFormat.Qrtx)
          await MessageBox.ShowInfo(this, Loc.S("QuickRoute"), Loc.Plain("Legacy_SavedAsNewFormat"));
        return await SaveDocumentShowDialogAsync();
      }
      return await SaveDocumentWithFileNameAsync(document.FileName);
    }

    private async Task<bool> SaveDocumentShowDialogAsync()
    {
      if (document == null) return false;
      var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
      {
        Title = Loc.S("SaveAsTitle"),
        DefaultExtension = "qrtx",
        ShowOverwritePrompt = true,
        SuggestedFileName = GetFileNameProposal() + QrtxDocumentStore.FileExtension,
        SuggestedStartLocation = await GetFolderAsync("Save"),
        FileTypeChoices = new[] { new FilePickerFileType(Loc.Plain("FileType_Qrtx")) { Patterns = new[] { "*.qrtx" } } }
      });
      var fileName = file?.TryGetLocalPath();
      if (fileName == null) return false;
      if (!fileName.EndsWith(QrtxDocumentStore.FileExtension, StringComparison.OrdinalIgnoreCase)) fileName += QrtxDocumentStore.FileExtension;
      settings.SetLastFolder("Save", fileName);
      return await SaveDocumentWithFileNameAsync(fileName);
    }

    private async Task<bool> SaveDocumentWithFileNameAsync(string fileName)
    {
      try
      {
        var doc = document;
        await Task.Run(() => documentFileService.Save(doc, fileName));
        documentChanged = false;
        settings.AddRecentDocument(fileName);
        settings.Save();
        UpdateUi();
        return true;
      }
      catch (Exception ex)
      {
        await MessageBox.ShowError(this, Loc.S("QuickRoute"), Loc.S("Error_SaveDocument"), ex);
        return false;
      }
    }

    /// <summary>
    /// Asks whether unsaved changes should be saved. Returns false if the user cancels.
    /// </summary>
    private async Task<bool> QuerySaveDocumentAsync()
    {
      if (document == null || !documentChanged) return true;
      var name = document.FileName == null ? Loc.S("Untitled") : Path.GetFileName(document.FileName);
      var result = await MessageBox.Show(this, Loc.S("QuickRoute"), string.Format(Loc.S("PromptSaveChanges"), name),
        MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel);
      if (result == MessageBoxResult.Cancel) return false;
      if (result == MessageBoxResult.Yes) return await SaveDocumentAsync();
      return true;
    }

    private bool closingConfirmed;

    private async void MainWindow_Closing(object sender, WindowClosingEventArgs e)
    {
      if (closingConfirmed) return;
      if (document != null && documentChanged)
      {
        e.Cancel = true;
        if (!await QuerySaveDocumentAsync()) return;
        closingConfirmed = true;
        settings.Save();
        Close();
        return;
      }
      settings.Save();
    }

    private string GetFileNameProposal()
    {
      return document?.FileName != null ? Path.GetFileNameWithoutExtension(document.FileName) : (untitledFileNameProposal ?? Loc.S("Untitled"));
    }

    private async Task ImportSessionsAsync()
    {
      if (document == null) return;
      var import = await RouteImportHelper.SelectAndImportRouteAsync(this, documentFileService, settings);
      if (import == null) return;
      var monochromeColors = new[] { System.Drawing.Color.Red, System.Drawing.Color.Blue, System.Drawing.Color.DarkGreen, System.Drawing.Color.DarkOrange, System.Drawing.Color.DarkGray };
      var ss = document.Settings.DefaultSessionSettings.Copy();
      ss.RouteLineSettingsCollection[WaypointAttribute.Pace].MonochromeColor = System.Drawing.Color.FromArgb(160, monochromeColors[document.Sessions.Count % monochromeColors.Length]);
      ss.RouteLineSettingsCollection[WaypointAttribute.Pace].MonochromeWidth = 3;
      var session = new Session(import.Route, import.Laps, document.Map.Size, document.Sessions.CalculateAverageTransformation().TransformationMatrix, document.ProjectionOrigin, ss);
      session.CreateAdjustedRoute();
      document.Sessions.Add(session);
      documentChanged = true;
      PopulateSessionList();
      Canvas.InvalidateRoute();
      UpdateUi();
    }

    #endregion

    #region Undo/redo and actions

    private void HandleAction(IAction action)
    {
      undoStack.Push(action);
      redoStack.Clear();
      documentChanged = true;
      UpdateAfterAction(action);
    }

    private void Undo()
    {
      if (undoStack.Count == 0) return;
      var action = undoStack.Pop();
      redoStack.Push(action);
      action.Undo();
      documentChanged = true;
      UpdateAfterAction(action);
    }

    private void Redo()
    {
      if (redoStack.Count == 0) return;
      var action = redoStack.Pop();
      undoStack.Push(action);
      action.Execute();
      documentChanged = true;
      UpdateAfterAction(action);
    }

    private void UpdateAfterAction(IAction action)
    {
      if (action is RotateMapAction)
      {
        Canvas.InvalidateRoute();
        UpdateUi();
        return;
      }
      foreach (var session in document.Sessions) session.CreateAdjustedRoute();
      Canvas.InvalidateRoute();
      if (action is AddLapAction || action is DeleteLapAction || action is EditLapAction || action is CutRouteAction ||
          action is TrimRouteAndAddLapsAction || action is AddTimeOffsetToSessionAction)
      {
        RefreshAll();
      }
      UpdateUi();
    }

    private void RotateMapImage(double angle)
    {
      if (document?.Map == null) return;
      var action = new RotateMapAction(angle, document, mapImageService);
      action.Execute();
      HandleAction(action);
    }

    private async Task ChangeStartTimeAsync()
    {
      if (CurrentSession == null) return;
      var time = await TimeDialog.ShowAsync(this, Loc.Ui("Menu_EditChangeStartTime").Replace("_", ""), CurrentSession.Route.FirstWaypoint.Time);
      if (!time.HasValue) return;
      var action = new AddTimeOffsetToSessionAction(CurrentSession, time.Value - CurrentSession.Route.FirstWaypoint.Time);
      action.Execute();
      HandleAction(action);
    }

    private async Task<DateTime?> EditLapTimeAsync(DateTime time, Session session)
    {
      return await TimeDialog.ShowAsync(this, Loc.Plain("Tip_Lap"), time);
    }

    private async Task<(DateTime Time, CutType CutType)?> EditCutAsync(DateTime time, CutType cutType)
    {
      return await CutDialog.ShowAsync(this, time, cutType);
    }

    #endregion

    #region UI state

    private void SetMouseTool(MouseTool tool)
    {
      Canvas.MouseTool = tool;
      UpdateUi();
    }

    private void UpdateUi()
    {
      updatingUi++;
      var hasDocument = document != null;
      Title = hasDocument
        ? (document.FileName != null ? Path.GetFileName(document.FileName) : Loc.S("Untitled")) + (documentChanged ? "*" : "") + " - " + Loc.S("QuickRoute")
        : Loc.S("QuickRoute");

      foreach (var item in new Control[]
               {
                 MenuFileSave, MenuFileSaveAs, MenuFileImportSessions, MenuFileExport, MenuEditChangeStartTime, MenuImage, MenuToolsAddLaps,
                 MenuToolsOpenExportedKmz, ToolSave, ZoomBox, ToolPointer, ToolAdjustRoute, ToolLap, ToolCut, ToolZoomIn, ToolZoomOut, RouteAppearanceToolbar
               })
      {
        item.IsEnabled = hasDocument;
      }
      MenuEditUndo.IsEnabled = ToolUndo.IsEnabled = undoStack.Count > 0;
      MenuEditRedo.IsEnabled = ToolRedo.IsEnabled = redoStack.Count > 0;

      ToolPointer.IsChecked = Canvas.MouseTool == MouseTool.Pointer;
      ToolAdjustRoute.IsChecked = Canvas.MouseTool == MouseTool.AdjustRoute;
      ToolLap.IsChecked = Canvas.MouseTool == MouseTool.Lap;
      ToolCut.IsChecked = Canvas.MouseTool == MouseTool.Cut;
      ToolZoomIn.IsChecked = Canvas.MouseTool == MouseTool.ZoomIn;
      ToolZoomOut.IsChecked = Canvas.MouseTool == MouseTool.ZoomOut;
      ToolFullScreen.IsChecked = MenuViewFullScreen.IsChecked = WindowState == WindowState.FullScreen;
      ToolRightPanel.IsChecked = MenuViewRightPanel.IsChecked = settings.RightPanelVisible;
      ToolBottomPanel.IsChecked = MenuViewBottomPanel.IsChecked = settings.BottomPanelVisible;
      ToolTip.SetTip(MouseToolHelp, Loc.S("DynamicHelp_MouseTool_" + Canvas.MouseTool));

      UpdateRecentDocumentsMenu();
      if (hasDocument) UpdateRouteAppearanceControls();
      updatingUi--;
    }

    private void UpdateZoomBox()
    {
      updatingUi++;
      var index = Array.FindIndex(zoomPresets, z => Math.Abs(z - Canvas.Zoom) < 1e-6);
      ZoomBox.SelectedIndex = index;
      ZoomBox.PlaceholderText = Canvas.Zoom.ToString("P0", CultureInfo.CurrentCulture);
      updatingUi--;
    }

    private void UpdateRecentDocumentsMenu()
    {
      var items = new List<MenuItem>();
      foreach (var fileName in settings.RecentDocuments)
      {
        var item = new MenuItem { Header = fileName.Replace("_", "__") };
        var name = fileName;
        item.Click += async (_, _) => await OpenDocumentAsync(name);
        items.Add(item);
      }
      if (items.Count == 0) items.Add(new MenuItem { Header = Loc.Plain("Recent_None"), IsEnabled = false });
      MenuFileRecent.ItemsSource = items;
    }

    private void ToggleFullScreen()
    {
      if (WindowState == WindowState.FullScreen)
      {
        WindowState = stateBeforeFullScreen;
      }
      else
      {
        stateBeforeFullScreen = WindowState;
        WindowState = WindowState.FullScreen;
      }
      UpdateUi();
    }

    private void TogglePanel(bool right)
    {
      if (right) settings.RightPanelVisible = !settings.RightPanelVisible;
      else settings.BottomPanelVisible = !settings.BottomPanelVisible;
      ApplyPanelVisibility();
      UpdateUi();
    }

    private void ApplyPanelVisibility()
    {
      RightPanel.IsVisible = RightSplitter.IsVisible = settings.RightPanelVisible;
      ContentGrid.ColumnDefinitions[2].Width = settings.RightPanelVisible ? new GridLength(300) : new GridLength(0);
      BottomPanel.IsVisible = BottomSplitter.IsVisible = settings.BottomPanelVisible;
      ContentGrid.RowDefinitions[2].Height = settings.BottomPanelVisible ? new GridLength(180) : new GridLength(0);
    }

    #endregion

    #region Platform helpers

    private void OnDragOver(object sender, DragEventArgs e)
    {
      e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
      var file = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
      if (file == null) return;
      var format = QuickRouteFileFormat.Unknown;
      try
      {
        format = DocumentFileService.DetectFormat(file);
      }
      catch (IOException)
      {
      }
      if (format == QuickRouteFileFormat.Unknown) await NewDocumentAsync(file);
      else await OpenDocumentAsync(file);
    }

    private async Task<IStorageFolder> GetFolderAsync(string key)
    {
      var folder = settings.GetLastFolder(key);
      return folder == null ? null : await StorageProvider.TryGetFolderFromPathAsync(folder);
    }

    internal static FilePickerFileType FileTypeFromFilter(string winFormsFilter)
    {
      var parts = winFormsFilter.Split('|');
      return new FilePickerFileType(parts[0].Trim()) { Patterns = parts.Length > 1 ? parts[1].Split(';') : new[] { "*" } };
    }

    internal void OpenUrl(string url)
    {
      if (string.IsNullOrEmpty(url)) return;
      _ = Launcher.LaunchUriAsync(new Uri(url));
    }

    private async Task ShowAboutAsync()
    {
      await new AboutDialog().ShowDialog(this);
    }

    private async Task SelectLanguageAsync()
    {
      var cultures = new[] { "", "en", "cs", "da", "de", "fi", "fr", "it", "lt", "no", "ru", "sv" };
      var names = cultures.Select(c => c == "" ? Loc.Plain("Language_Default") : CultureInfo.GetCultureInfo(c).NativeName).ToList();
      var dialog = new FormDialog(Loc.Plain("Language_Title"));
      var combo = dialog.AddRow(Loc.Plain("Language_Label"), FormDialog.CreateComboBox(names, Math.Max(0, Array.IndexOf(cultures, settings.UiCulture ?? ""))));
      if (!await dialog.ShowAsync(this)) return;
      var selected = cultures[Math.Max(0, combo.SelectedIndex)];
      settings.UiCulture = selected == "" ? null : selected;
      settings.Save();
      await MessageBox.ShowInfo(this, Loc.Plain("Language_Title"), Loc.Plain("Language_Restart"));
    }

    #endregion
  }
}
