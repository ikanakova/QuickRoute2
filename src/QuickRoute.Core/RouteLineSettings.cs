using System;
using System.Drawing;
using QuickRoute.BusinessEntities.GlobalizedProperties;

namespace QuickRoute.BusinessEntities
{
  public class RouteLineSettings : GlobalizedObject
  {
    private ColorRange colorRange;
    private double width;
    private Color maskColor;
    private double maskWidth;
    private bool maskVisible;
    private double alphaAdjustment;
    private double monochromeWidth;
    private Color monochromeColor;

    public RouteLineSettings()
    {

    }

    public RouteLineSettings(ColorRange colorRange, double width, Color maskColor, double maskWidth, bool maskVisible, double alphaAdjustment)
    {
      this.colorRange = colorRange;
      this.width = width;
      this.maskColor = maskColor;
      this.maskWidth = maskWidth;
      this.maskVisible = maskVisible;
      AlphaAdjustment = alphaAdjustment;
    }

    public RouteLineSettings Copy()
    {
      return new RouteLineSettings(colorRange?.Copy(), width, maskColor, maskWidth, maskVisible, alphaAdjustment)
      {
        monochromeColor = monochromeColor,
        monochromeWidth = monochromeWidth
      };
    }

    public ColorRange ColorRange
    {
      get { return colorRange; }
      set { colorRange = value; }
    }

    public double Width
    {
      get { return width; }
      set { width = value; }
    }

    public Color MaskColor
    {
      get { return maskColor; }
      set { maskColor = value; }
    }

    public double MaskWidth
    {
      get { return maskWidth; }
      set { maskWidth = value; }
    }

    public bool MaskVisible
    {
      get { return maskVisible; }
      set { maskVisible = value; }
    }

    public double AlphaAdjustment
    {
      get { return alphaAdjustment; }
      set { alphaAdjustment = Math.Min(1, Math.Max(-1, value)); }
    }

    public double MonochromeWidth
    {
      get { return monochromeWidth; }
      set { monochromeWidth = value; }
    }

    public Color MonochromeColor
    {
      get { return monochromeColor; }
      set { monochromeColor = value; }
    }

  }
}
