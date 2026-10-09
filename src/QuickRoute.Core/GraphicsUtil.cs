using System;
using System.Drawing;

namespace QuickRoute.BusinessEntities
{
  public static class GraphicsUtil
  {
    public static Color AlphaAdjustColor(Color color, double alphaAdjustment)
    {
      alphaAdjustment = Math.Max(-1, Math.Min(1, alphaAdjustment));
      int alpha;
      if (alphaAdjustment < 0)
      {
        alpha = (int)((1 + alphaAdjustment) * (double)color.A);
      }
      else
      {
        alpha = (int)((double)color.A + (double)(255 - color.A) * alphaAdjustment);
      }
      return Color.FromArgb(alpha, color);
    }

    public static double CombineAlphaAdjustments(double aa1, double aa2)
    {
      if (aa2 < 0)
      {
        return aa1 - (aa2 + 1) * (aa1 + 1);
      }
      return aa1 + (1 - aa2) * (1 - aa1);
    }

    public static Color CombineColors(params Color[] colors)
    {
      int a = 0, r = 0, g = 0, b = 0;
      foreach (var color in colors)
      {
        a += color.A;
        r += color.R;
        g += color.G;
        b += color.B;
      }
      return Color.FromArgb(
        (int)Math.Round((double)a / colors.Length),
        (int)Math.Round((double)r / colors.Length),
        (int)Math.Round((double)g / colors.Length),
        (int)Math.Round((double)b / colors.Length));
    }
  }
}
