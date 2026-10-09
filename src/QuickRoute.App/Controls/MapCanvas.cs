using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Actions;
using QuickRoute.Rendering;
using SkiaSharp;

namespace QuickRoute.App.Controls
{
  public enum MouseTool
  {
    Pointer,
    AdjustRoute,
    ZoomIn,
    ZoomOut,
    Cut,
    Lap
  }

  public sealed class RouteHoverEventArgs : EventArgs
  {
    public RouteHoverEventArgs(ParameterizedLocation parameterizedLocation, bool isClose)
    {
      ParameterizedLocation = parameterizedLocation;
      IsClose = isClose;
    }

    public ParameterizedLocation ParameterizedLocation { get; }

    public bool IsClose { get; }
  }

  /// <summary>
  /// The interactive map view: map image, routes, markers, route adjustment, laps, cropping and zooming.
  /// Hosted in a <see cref="ScrollViewer"/>; the control size equals the zoomed map size so that pointer
  /// positions are zoomed map coordinates.
  /// </summary>
  public class MapCanvas : Control
  {
    private const double LapClosenessTolerance = 16.0;
    private const double RouteClosenessTolerance = 32.0;

    private Document document;
    private Session currentSession;
    private double zoom = 1;
    private MouseTool mouseTool = MouseTool.Pointer;
    private WaypointAttribute colorCodingAttribute = WaypointAttribute.Pace;
    private WaypointAttribute? secondaryColorCodingAttribute;

    private SKPicture routePicture;
    private bool routeDirty = true;

    private Handle activeHandle;
    private PointD activeHandleOffset;
    private PointD activeHandleOldLocation;
    private Handle mouseHoverHandle;
    private Lap activeLap;
    private Lap oldActiveLap;
    private bool movingActiveHandleNow;
    private bool movingWholeRouteNow;
    private bool movingActiveLapNow;
    private Session.AdjustmentState adjustmentBeforeDrag;
    private Session.AdjustmentState adjustmentDragBase;
    private PointD adjustmentPivot;

    private bool panning;
    private Point panStart;
    private Vector panStartOffset;
    private Point? zoomSelectionStart;
    private Point? zoomSelectionEnd;
    private PointD lastMouseLocation;
    private KeyModifiers lastModifiers;

    public MapCanvas()
    {
      Focusable = true;
      ClipToBounds = true;
    }

    public event EventHandler<IAction> ActionPerformed;
    public event EventHandler<RouteHoverEventArgs> RouteMouseHover;
    public event EventHandler ZoomChanged;

    /// <summary>
    /// Asks the host to edit a time, e.g. for laps added with Shift or when cropping the route. Returns null when cancelled.
    /// </summary>
    public Func<DateTime, Session, System.Threading.Tasks.Task<DateTime?>> LapTimeEditor { get; set; }

    /// <summary>
    /// Asks the host for the time and side to crop the route. Returns null when cancelled.
    /// </summary>
    public Func<DateTime, CutType, System.Threading.Tasks.Task<(DateTime Time, CutType CutType)?>> CutEditor { get; set; }

    public ScrollViewer ScrollViewer { get; set; }

    public double MinZoom { get; set; } = 0.25;

    public double MaxZoom { get; set; } = 2.0;

    public SessionCollection SessionsToDraw { get; set; } = new SessionCollection();

    public Document Document
    {
      get => document;
      set
      {
        if (document != null) document.MapChanged -= Document_MapChanged;
        document = value;
        currentSession = document != null && document.Sessions.Count > 0 ? document.Sessions[0] : null;
        if (document != null)
        {
          document.EnsureProjectionOrigin();
          document.MapChanged += Document_MapChanged;
        }
        ResetInteraction();
        UpdateSize();
        InvalidateRoute();
      }
    }

    public Session CurrentSession
    {
      get => currentSession;
      set
      {
        if (document == null || value == currentSession) return;
        if (!document.Sessions.Contains(value)) throw new ArgumentException("The session does not exist in the document.");
        currentSession = value;
        ResetInteraction();
        InvalidateRoute();
      }
    }

    public double Zoom
    {
      get => zoom;
      set => SetZoom(value, null);
    }

    public MouseTool MouseTool
    {
      get => mouseTool;
      set
      {
        var previousMode = RouteDrawingMode;
        mouseTool = value;
        Cursor = GetCursor(value);
        ResetInteraction();
        if (RouteDrawingMode != previousMode) InvalidateRoute();
        else InvalidateVisual();
      }
    }

    public WaypointAttribute ColorCodingAttribute
    {
      get => colorCodingAttribute;
      set
      {
        colorCodingAttribute = value;
        InvalidateRoute();
      }
    }

    public WaypointAttribute? SecondaryColorCodingAttribute
    {
      get => secondaryColorCodingAttribute;
      set
      {
        secondaryColorCodingAttribute = value;
        InvalidateRoute();
      }
    }

    public Document.RouteDrawingMode RouteDrawingMode =>
      mouseTool == MouseTool.AdjustRoute ? Document.RouteDrawingMode.Simple : Document.RouteDrawingMode.Extended;

    /// <summary>
    /// Re-creates the route drawing, e.g. after the route, its adjustment or its appearance has changed.
    /// </summary>
    public void InvalidateRoute()
    {
      routeDirty = true;
      InvalidateVisual();
    }

    public void SetZoom(double value, PointD fixedMapPoint)
    {
      value = Math.Min(Math.Max(value, MinZoom), MaxZoom);
      if (Math.Abs(value - zoom) < 1e-9) return;
      var center = fixedMapPoint ?? GetViewportCenter();
      zoom = value;
      UpdateSize();
      InvalidateRoute();
      if (center != null) CenterMap(center);
      ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CenterMap(PointD mapPoint)
    {
      if (ScrollViewer == null) return;
      Dispatcher.UIThread.Post(() =>
      {
        var viewport = ScrollViewer.Viewport;
        ScrollViewer.Offset = new Vector(
          Math.Max(0, mapPoint.X * zoom - viewport.Width / 2),
          Math.Max(0, mapPoint.Y * zoom - viewport.Height / 2));
      }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Shows a hover marker at the specified route location, e.g. when hovering the line graph.
    /// </summary>
    public void ShowHoverMarker(ParameterizedLocation pl)
    {
      if (currentSession == null) return;
      ResetActiveHandle();
      activeLap = null;
      mouseHoverHandle = pl == null
        ? null
        : new Handle(pl, currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(pl),
          currentSession.GetTransformationMatrixFromParameterizedLocation(pl), currentSession.Settings.MarkerDrawers[MarkerType.MouseHover]);
      InvalidateVisual();
    }

    /// <summary>
    /// Highlights a lap by making the rest of the route more transparent, as the lap grid selection does.
    /// </summary>
    public void AlphaAdjustLap(Session session, Lap lap, double lapAlphaAdjustment, double courseAlphaAdjustment, double defaultAlphaAdjustment)
    {
      var r = session.Route;
      var aac = new List<AlphaAdjustmentChange>();
      var lapIndex = -1;
      for (var i = 0; lap != null && i < session.Laps.Count; i++)
      {
        if (session.Laps[i] == lap) lapIndex = i;
      }
      if (lapIndex > 0 && lap.LapType != LapType.Start)
      {
        var lastLap = session.Laps[lapIndex - 1];
        if (r.FirstWaypoint.Time < lastLap.Time)
          aac.Add(new AlphaAdjustmentChange(r.GetParameterizedLocationFromTime(r.FirstWaypoint.Time), courseAlphaAdjustment));
        aac.Add(new AlphaAdjustmentChange(r.GetParameterizedLocationFromTime(lastLap.Time), lapAlphaAdjustment));
        aac.Add(new AlphaAdjustmentChange(r.GetParameterizedLocationFromTime(lap.Time), courseAlphaAdjustment));
      }
      else
      {
        aac.Add(new AlphaAdjustmentChange(r.GetParameterizedLocationFromTime(r.FirstWaypoint.Time), defaultAlphaAdjustment));
      }
      session.AlphaAdjustmentChanges = aac;
      InvalidateRoute();
    }

    public void EnsureVisible(RectangleD mapRectangle)
    {
      if (ScrollViewer == null || mapRectangle == null) return;
      var viewport = ScrollViewer.Viewport;
      var offset = ScrollViewer.Offset;
      var left = mapRectangle.Left * zoom;
      var top = mapRectangle.Top * zoom;
      var right = mapRectangle.Right * zoom;
      var bottom = mapRectangle.Bottom * zoom;
      if (left >= offset.X && right <= offset.X + viewport.Width && top >= offset.Y && bottom <= offset.Y + viewport.Height) return;
      CenterMap(new PointD((mapRectangle.Left + mapRectangle.Right) / 2, (mapRectangle.Top + mapRectangle.Bottom) / 2));
    }

    #region Rendering

    public override void Render(DrawingContext context)
    {
      if (document == null) return;
      if (routeDirty) RecordRoute();
      var mapImage = SkiaUtil.GetImage(document.Map);
      var mapSize = document.GetMapImageSize(zoom);
      var route = routePicture;
      var markers = RecordMarkers();
      context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
      {
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
          canvas.DrawImage(mapImage, SKRect.Create(0, 0, mapImage.Width, mapImage.Height), SKRect.Create(0, 0, mapSize.Width, mapSize.Height), paint);
        }
        if (route != null) canvas.DrawPicture(route);
        canvas.DrawPicture(markers);
        markers.Dispose();
      }));
    }

    private void RecordRoute()
    {
      routeDirty = false;
      // the previous picture may still be used by a pending draw operation, so it is released by its finalizer
      routePicture = null;
      if (document == null) return;
      var size = document.GetMapImageSize(zoom);
      using var recorder = new SKPictureRecorder();
      var canvas = recorder.BeginRecording(SKRect.Create(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)));
      DocumentRenderer.DrawRoutes(canvas, GetSessionsToDraw(), zoom, RouteDrawingMode, colorCodingAttribute, secondaryColorCodingAttribute, null);
      routePicture = recorder.EndRecording();
    }

    private SKPicture RecordMarkers()
    {
      var size = document.GetMapImageSize(zoom);
      using var recorder = new SKPictureRecorder();
      var canvas = recorder.BeginRecording(SKRect.Create(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)));
      if (currentSession != null)
      {
        if (mouseTool == MouseTool.AdjustRoute)
        {
          foreach (var h in currentSession.Handles)
          {
            if (h.MarkerDrawer != null) RouteRenderer.DrawMarker(canvas, h.MarkerDrawer, zoom * h.Location, zoom);
          }
        }
        if (mouseHoverHandle?.MarkerDrawer != null) RouteRenderer.DrawMarker(canvas, mouseHoverHandle.MarkerDrawer, zoom * mouseHoverHandle.Location, zoom);
        DocumentRenderer.DrawLapMarkers(canvas, currentSession, zoom);
      }
      if (zoomSelectionStart.HasValue && zoomSelectionEnd.HasValue)
      {
        var a = zoomSelectionStart.Value;
        var b = zoomSelectionEnd.Value;
        using var paint = new SKPaint
        {
          Style = SKPaintStyle.Stroke,
          Color = SKColors.Black,
          StrokeWidth = 1,
          PathEffect = SKPathEffect.CreateDash(new[] { 2f, 2f }, 0)
        };
        canvas.DrawRect(SKRect.Create((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Abs(a.X - b.X), (float)Math.Abs(a.Y - b.Y)), paint);
      }
      return recorder.EndRecording();
    }

    private IEnumerable<Session> GetSessionsToDraw()
    {
      if (currentSession == null) return Array.Empty<Session>();
      if (mouseTool == MouseTool.AdjustRoute) return new[] { currentSession };
      return SessionsToDraw != null && SessionsToDraw.Count > 0 ? (IEnumerable<Session>)SessionsToDraw : new[] { currentSession };
    }

    #endregion

    #region Pointer handling

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
      base.OnPointerPressed(e);
      if (document == null || currentSession == null) return;
      Focus();
      var point = e.GetCurrentPoint(this);
      var position = point.Position;
      lastModifiers = e.KeyModifiers;
      lastMouseLocation = ToMapLocation(position);
      e.Pointer.Capture(this);

      if (point.Properties.IsRightButtonPressed)
      {
        BeginPan(e);
        return;
      }
      if (!point.Properties.IsLeftButtonPressed) return;

      var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
      var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
      switch (mouseTool)
      {
        case MouseTool.AdjustRoute:
          if (mouseHoverHandle != null)
          {
            // transform the mouse hover handle to an adjustment handle
            activeHandle = mouseHoverHandle;
            activeHandleOldLocation = activeHandle.Location;
            SnapNewHandleToEndpoint();
            activeHandleOffset = lastMouseLocation - activeHandle.Location;
            BeginActiveHandleMoving(e.ClickCount);
          }
          else if (activeHandle != null)
          {
            if (control) DeleteActiveHandle();
            else BeginActiveHandleMoving(e.ClickCount);
          }
          else
          {
            BeginPan(e);
          }
          break;

        case MouseTool.ZoomIn:
          zoomSelectionStart = position;
          zoomSelectionEnd = position;
          break;

        case MouseTool.ZoomOut:
          SetZoom(zoom * 0.75, lastMouseLocation);
          break;

        case MouseTool.Cut:
          var cutHandle = activeHandle ?? mouseHoverHandle;
          if (cutHandle == null)
          {
            BeginPan(e);
          }
          else
          {
            DateTime cutTime;
            if (cutHandle.Time.HasValue)
            {
              cutTime = cutHandle.Time.Value;
            }
            else
            {
              cutTime = currentSession.Route.GetTimeFromParameterizedLocation(cutHandle.ParameterizedLocation);
              cutTime = new DateTime(TimeSpan.TicksPerSecond * (long)Math.Round((double)cutTime.Ticks / TimeSpan.TicksPerSecond), cutTime.Kind);
            }
            _ = CutAsync(cutHandle.ParameterizedLocation, cutTime);
          }
          break;

        case MouseTool.Lap:
          if (control)
          {
            if (activeLap != null && activeLap.LapType == LapType.Lap)
            {
              ExecuteAction(new DeleteLapAction(activeLap, currentSession));
              activeLap = null;
              mouseHoverHandle = null;
              InvalidateRoute();
            }
            else if (activeLap == null)
            {
              BeginPan(e);
            }
          }
          else if (activeLap != null && activeLap.LapType == LapType.Lap)
          {
            movingActiveLapNow = true;
            oldActiveLap = (Lap)activeLap.Clone();
          }
          else if (activeLap == null)
          {
            var addLapHandle = activeHandle ?? mouseHoverHandle;
            if (addLapHandle == null) BeginPan(e);
            else _ = AddLapAsync(addLapHandle.ParameterizedLocation, shift);
          }
          break;

        default:
          BeginPan(e);
          break;
      }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
      base.OnPointerMoved(e);
      if (document == null || currentSession == null) return;
      var position = e.GetPosition(this);
      lastModifiers = e.KeyModifiers;
      lastMouseLocation = ToMapLocation(position);

      if (panning)
      {
        if (ScrollViewer != null)
        {
          var current = e.GetPosition(ScrollViewer);
          ScrollViewer.Offset = new Vector(
            Math.Max(0, panStartOffset.X - (current.X - panStart.X)),
            Math.Max(0, panStartOffset.Y - (current.Y - panStart.Y)));
        }
        return;
      }

      switch (mouseTool)
      {
        case MouseTool.Pointer:
        case MouseTool.AdjustRoute:
        case MouseTool.Cut:
        case MouseTool.Lap:
          CheckMouseHovering(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
          break;
        case MouseTool.ZoomIn:
          if (zoomSelectionStart.HasValue)
          {
            zoomSelectionEnd = position;
            InvalidateVisual();
          }
          break;
      }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
      base.OnPointerReleased(e);
      e.Pointer.Capture(null);
      if (document == null || currentSession == null) return;
      if (panning)
      {
        panning = false;
        Cursor = GetCursor(mouseTool);
        return;
      }
      switch (mouseTool)
      {
        case MouseTool.AdjustRoute:
          if (movingActiveHandleNow) EndActiveHandleMoving();
          ResetActiveHandle();
          InvalidateVisual();
          break;
        case MouseTool.ZoomIn:
          if (zoomSelectionStart.HasValue) ZoomIn(zoomSelectionStart.Value, e.GetPosition(this));
          zoomSelectionStart = null;
          zoomSelectionEnd = null;
          InvalidateVisual();
          break;
        case MouseTool.Lap:
          if (movingActiveLapNow) EndActiveLapMoving();
          break;
      }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
      base.OnPointerExited(e);
      if (movingActiveHandleNow || movingActiveLapNow || panning) return;
      if (mouseHoverHandle != null || activeHandle != null || activeLap != null)
      {
        ResetInteraction();
        InvalidateVisual();
      }
      RouteMouseHover?.Invoke(this, new RouteHoverEventArgs(null, false));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
      if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
      {
        SetZoom(zoom + 0.1 * Math.Sign(e.Delta.Y), ToMapLocation(e.GetPosition(this)));
        e.Handled = true;
        return;
      }
      base.OnPointerWheelChanged(e);
    }

    private void BeginPan(PointerPressedEventArgs e)
    {
      if (ScrollViewer == null) return;
      panning = true;
      panStart = e.GetPosition(ScrollViewer);
      panStartOffset = ScrollViewer.Offset;
      Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void CheckMouseHovering(bool shiftDown)
    {
      var mouseLocation = lastMouseLocation;
      var closestPL = currentSession.AdjustedRoute.GetClosestParameterizedLocation(mouseLocation, out var distanceToRoute);

      if (!movingActiveHandleNow && !movingActiveLapNow)
      {
        if (closestPL == null)
        {
          if (mouseHoverHandle != null)
          {
            ResetInteraction();
            InvalidateVisual();
          }
          return;
        }

        double closestHandleDistance = 0;
        double closestLapDistance = 0;
        Handle closestHandle = null;
        Lap closestLap = null;
        var routeLocation = currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(closestPL);

        // Shift prevents snapping to handles and laps
        if (!shiftDown && mouseTool == MouseTool.AdjustRoute) closestHandle = GetClosestHandle(routeLocation, out closestHandleDistance);
        if (!shiftDown) closestLap = GetClosestLap(routeLocation, out closestLapDistance);

        if (distanceToRoute >= RouteClosenessTolerance)
        {
          ResetInteraction();
          RouteMouseHover?.Invoke(this, new RouteHoverEventArgs(closestPL, false));
        }
        else if (closestHandle != null && closestHandleDistance < RouteClosenessTolerance &&
                 !(closestLap != null && closestLapDistance * 1.00001 < closestHandleDistance))
        {
          ResetActiveHandle();
          activeHandle = closestHandle;
          activeHandleOldLocation = activeHandle.Location;
          activeHandleOffset = mouseLocation - activeHandle.Location;
          activeHandle.MarkerDrawer = currentSession.Settings.MarkerDrawers[MarkerType.ActiveHandle];
          mouseHoverHandle = null;
          activeLap = null;
          RouteMouseHover?.Invoke(this, new RouteHoverEventArgs(activeHandle.ParameterizedLocation, true));
        }
        else if (closestLap != null && closestLapDistance < LapClosenessTolerance)
        {
          var pl = currentSession.Route.GetParameterizedLocationFromTime(closestLap.Time);
          var lapLocation = currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(pl);
          mouseHoverHandle = new Handle(pl, closestLap.Time, lapLocation, currentSession.GetTransformationMatrixFromParameterizedLocation(pl),
            currentSession.Settings.MarkerDrawers[MarkerType.MouseHover]);
          activeLap = closestLap;
          RouteMouseHover?.Invoke(this, new RouteHoverEventArgs(pl, true));
        }
        else
        {
          ResetActiveHandle();
          activeLap = null;
          mouseHoverHandle = new Handle(closestPL, routeLocation, currentSession.GetTransformationMatrixFromParameterizedLocation(closestPL),
            currentSession.Settings.MarkerDrawers[MarkerType.MouseHover]);
          RouteMouseHover?.Invoke(this, new RouteHoverEventArgs(closestPL, true));
        }
        InvalidateVisual();
      }
      else if (movingActiveHandleNow)
      {
        var location = mouseLocation - activeHandleOffset;
        if (movingWholeRouteNow)
        {
          currentSession.RestoreAdjustment(adjustmentDragBase);
          currentSession.MoveEntireAdjustment(activeHandleOldLocation, location, adjustmentPivot);
        }
        else
        {
          activeHandle.Location = location;
          currentSession.UpdateHandle(activeHandle);
        }
        InvalidateRoute();
      }
      else if (movingActiveLapNow)
      {
        if (closestPL == null) return;
        activeLap.Time = currentSession.Route.GetTimeFromParameterizedLocation(closestPL);
        InvalidateVisual();
      }
    }

    private void BeginActiveHandleMoving(int clickCount)
    {
      adjustmentBeforeDrag = currentSession.CaptureAdjustment();
      var first = currentSession.AdjustedRoute.GetFirstParameterizedLocation();
      var last = currentSession.AdjustedRoute.GetLastParameterizedLocation();
      // a single click on the route start or finish moves the whole adjustment; double click edits locally
      movingWholeRouteNow = clickCount == 1 && (activeHandle.ParameterizedLocation == first || activeHandle.ParameterizedLocation == last);
      adjustmentPivot = currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(activeHandle.ParameterizedLocation == first ? last : first);
      activeHandle.MarkerDrawer = currentSession.Settings.MarkerDrawers[MarkerType.MovingActiveHandle];
      if (!currentSession.ContainsHandle(activeHandle)) currentSession.AddHandle(activeHandle);
      if (!movingWholeRouteNow) currentSession.EnsureAdjustmentBoundaries();
      adjustmentDragBase = currentSession.CaptureAdjustment();
      movingActiveHandleNow = true;
      if (movingWholeRouteNow)
      {
        currentSession.MoveEntireAdjustment(activeHandleOldLocation, lastMouseLocation - activeHandleOffset, adjustmentPivot);
      }
      else
      {
        activeHandle.Location = lastMouseLocation - activeHandleOffset;
        currentSession.UpdateHandle(activeHandle);
      }
      InvalidateRoute();
    }

    private void EndActiveHandleMoving()
    {
      movingActiveHandleNow = false;
      movingWholeRouteNow = false;
      ExecuteAction(new MoveHandleAction(activeHandle, currentSession, activeHandleOldLocation, adjustmentBeforeDrag));
      adjustmentBeforeDrag = null;
      adjustmentDragBase = null;
      InvalidateRoute();
    }

    private void SnapNewHandleToEndpoint()
    {
      var first = currentSession.AdjustedRoute.GetFirstParameterizedLocation();
      var last = currentSession.AdjustedRoute.GetLastParameterizedLocation();
      var firstPoint = currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(first);
      var lastPoint = currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(last);
      var firstDistance = LinearAlgebraUtil.DistancePointToPoint(activeHandle.Location, firstPoint);
      var lastDistance = LinearAlgebraUtil.DistancePointToPoint(activeHandle.Location, lastPoint);
      if (Math.Min(firstDistance, lastDistance) * zoom > RouteClosenessTolerance) return;
      var endpoint = firstDistance <= lastDistance ? first : last;
      var existing = currentSession.FindAdjustmentHandle(endpoint);
      if (existing != null)
      {
        activeHandle = existing;
        activeHandleOldLocation = existing.Location;
        return;
      }
      activeHandle.ParameterizedLocation = endpoint;
      activeHandle.Time = currentSession.Route.GetTimeFromParameterizedLocation(endpoint);
      activeHandle.Location = endpoint == first ? firstPoint : lastPoint;
      activeHandle.TransformationMatrix = currentSession.GetTransformationMatrixFromParameterizedLocation(endpoint);
      activeHandleOldLocation = activeHandle.Location;
    }

    private void DeleteActiveHandle()
    {
      ExecuteAction(new DeleteHandleAction(activeHandle, currentSession));
      activeHandle = null;
      InvalidateRoute();
    }

    private void EndActiveLapMoving()
    {
      movingActiveLapNow = false;
      if (!currentSession.Laps.Exists(activeLap.Time, 2))
      {
        ExecuteAction(new EditLapAction(activeLap, oldActiveLap, currentSession));
        InvalidateRoute();
      }
      else
      {
        activeLap.Time = oldActiveLap.Time;
        InvalidateVisual();
      }
    }

    private async System.Threading.Tasks.Task AddLapAsync(ParameterizedLocation parameterizedLocation, bool showLapTimeEditor)
    {
      var session = currentSession;
      var time = session.Route.GetTimeFromParameterizedLocation(parameterizedLocation);
      time = time.AddTicks(-time.Ticks % TimeSpan.TicksPerSecond); // truncate to nearest second
      if (showLapTimeEditor && LapTimeEditor != null)
      {
        var edited = await LapTimeEditor(time, session);
        if (!edited.HasValue) return;
        time = edited.Value;
      }
      if (!session.Laps.Exists(time))
      {
        ExecuteAction(new AddLapAction(new Lap(time, LapType.Lap), session));
        InvalidateRoute();
      }
    }

    private async System.Threading.Tasks.Task CutAsync(ParameterizedLocation parameterizedLocation, DateTime time)
    {
      if (CutEditor == null) return;
      var session = currentSession;
      var distance = session.Route.GetAttributeFromParameterizedLocation(WaypointAttribute.Distance, parameterizedLocation).Value;
      var totalDistance = session.Route.LastWaypoint.Attributes[WaypointAttribute.Distance].Value;
      var result = await CutEditor(time, distance < totalDistance / 2 ? CutType.Before : CutType.After);
      if (!result.HasValue) return;
      mouseHoverHandle = null;
      ExecuteAction(new CutRouteAction(session, result.Value.Time, result.Value.CutType));
      InvalidateRoute();
    }

    private void ZoomIn(Point first, Point last)
    {
      var rectangle = new Rect(first, last).Normalize();
      var center = new PointD((rectangle.X + rectangle.Width / 2) / zoom, (rectangle.Y + rectangle.Height / 2) / zoom);
      double factor = 4.0 / 3;
      if (rectangle.Width > 4 && rectangle.Height > 4 && ScrollViewer != null)
      {
        factor = Math.Min(ScrollViewer.Viewport.Width / rectangle.Width, ScrollViewer.Viewport.Height / rectangle.Height);
      }
      SetZoom(zoom * factor, center);
      CenterMap(center);
    }

    private Handle GetClosestHandle(PointD location, out double distance)
    {
      Handle closest = null;
      distance = 0;
      foreach (var h in currentSession.Handles)
      {
        var d = LinearAlgebraUtil.DistancePointToPoint(location, h.Location);
        if (closest == null || d < distance)
        {
          closest = h;
          distance = d;
        }
      }
      return closest;
    }

    private Lap GetClosestLap(PointD location, out double distance)
    {
      Lap closest = null;
      distance = 0;
      foreach (var lap in currentSession.Laps)
      {
        var pl = currentSession.Route.GetParameterizedLocationFromTime(lap.Time);
        var d = LinearAlgebraUtil.DistancePointToPoint(location, currentSession.AdjustedRoute.GetLocationFromParameterizedLocation(pl));
        if (closest == null || d < distance)
        {
          closest = lap;
          distance = d;
        }
      }
      return closest;
    }

    #endregion

    private void ExecuteAction(IAction action)
    {
      action.Execute();
      ActionPerformed?.Invoke(this, action);
    }

    private void ResetActiveHandle()
    {
      if (activeHandle != null && currentSession != null && !movingActiveHandleNow)
        activeHandle.MarkerDrawer = currentSession.Settings.MarkerDrawers[MarkerType.Handle];
      activeHandle = null;
    }

    private void ResetInteraction()
    {
      ResetActiveHandle();
      mouseHoverHandle = null;
      activeLap = null;
    }

    private PointD ToMapLocation(Point position) => new PointD(position.X / zoom, position.Y / zoom);

    private PointD GetViewportCenter()
    {
      if (ScrollViewer == null || document == null) return null;
      var viewport = ScrollViewer.Viewport;
      var offset = ScrollViewer.Offset;
      var size = document.GetMapImageSize(zoom);
      return new PointD(
        (offset.X + Math.Min(size.Width, viewport.Width) / 2) / zoom,
        (offset.Y + Math.Min(size.Height, viewport.Height) / 2) / zoom);
    }

    private void UpdateSize()
    {
      if (document == null)
      {
        Width = 0;
        Height = 0;
        return;
      }
      var size = document.GetMapImageSize(zoom);
      Width = size.Width;
      Height = size.Height;
    }

    private void Document_MapChanged(object sender, EventArgs e)
    {
      UpdateSize();
      InvalidateRoute();
    }

    private static Cursor GetCursor(MouseTool tool)
    {
      switch (tool)
      {
        case MouseTool.AdjustRoute:
          return new Cursor(StandardCursorType.Hand);
        case MouseTool.Lap:
        case MouseTool.ZoomIn:
        case MouseTool.ZoomOut:
          return new Cursor(StandardCursorType.Cross);
        case MouseTool.Cut:
          return new Cursor(StandardCursorType.Ibeam);
        default:
          return Cursor.Default;
      }
    }
  }
}
