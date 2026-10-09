using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace QuickRoute.App.Controls
{
  /// <summary>
  /// Draws immutable Skia content (pictures and images captured on the UI thread) on the render thread.
  /// </summary>
  internal sealed class SkiaDrawOperation : ICustomDrawOperation
  {
    private readonly Action<SKCanvas> draw;

    public SkiaDrawOperation(Rect bounds, Action<SKCanvas> draw)
    {
      Bounds = bounds;
      this.draw = draw;
    }

    public Rect Bounds { get; }

    public void Render(ImmediateDrawingContext context)
    {
      var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
      if (leaseFeature == null) return;
      using var lease = leaseFeature.Lease();
      lease.SkCanvas.Save();
      lease.SkCanvas.ClipRect(SKRect.Create(0, 0, (float)Bounds.Width, (float)Bounds.Height));
      draw(lease.SkCanvas);
      lease.SkCanvas.Restore();
    }

    public bool HitTest(Point p) => Bounds.Contains(p);

    public bool Equals(ICustomDrawOperation other) => false;

    public void Dispose()
    {
    }
  }
}
