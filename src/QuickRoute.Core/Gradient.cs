using System;
using System.Collections.Generic;
using System.Drawing;
using QuickRoute.BusinessEntities.GlobalizedProperties;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// Class representing a gradient, i e a list of colors and their locations.
  /// </summary>
  public class Gradient : GlobalizedObject
  {
    private string name;
    private string fileName;
    private List<GradientColorEntry> colorEntries = new List<GradientColorEntry>();

    public Gradient() : this(Color.White, Color.Black) { }

    /// <summary>
    /// Creating a new gradient based on the specified color entries.
    /// </summary>
    /// <param name="colorEntryParamArray">First parameter: first color, second parameter: location of first color, third parameter: second color, fourth parameter: location of second color, and so forth. Locations are in the interval (0, 1), inclusive.</param>
    public Gradient(params object[] colorEntryParamArray)
    {
      if (colorEntryParamArray == null)
      {
        throw new Exception("Param array length must be greater than zero.");
      }
      else
      {
        if (colorEntryParamArray.Length % 2 != 0) throw new Exception("Param array length must be an even number.");
        for (int i = 0; i < colorEntryParamArray.Length / 2; i++)
        {
          colorEntries.Add(new GradientColorEntry((Color)colorEntryParamArray[2 * i], (double)colorEntryParamArray[2 * i + 1]));
        }
      }
      CreateSortedColorEntries();
    }

    /// <summary>
    /// Creating a new gradient based on the specified start and end color, and the specified color entries.
    /// </summary>
    /// <param name="startColor"></param>
    /// <param name="endColor"></param>
    /// <param name="colorEntryParamArray">First parameter: first middle color, second parameter: location of first middle color, third parameter: second middle color, fourth parameter: location of second middle color, and so forth. Locations are in the interval (0, 1), inclusive.</param>
    public Gradient(Color startColor, Color endColor, params object[] colorEntryParamArray)
    {
      colorEntries.Add(new GradientColorEntry(startColor, 0));
      colorEntries.Add(new GradientColorEntry(endColor, 1));
      if (colorEntryParamArray != null)
      {
        if (colorEntryParamArray.Length % 2 != 0) throw new Exception("Param array length must be an even number.");
        for (int i = 0; i < colorEntryParamArray.Length / 2; i++)
        {
          colorEntries.Add(new GradientColorEntry((Color)colorEntryParamArray[2 * i], (double)colorEntryParamArray[2 * i + 1]));
        }
      }
      CreateSortedColorEntries();
    }

    /// <summary>
    /// Creates a gradient from explicit color/location pairs.
    /// </summary>
    public static Gradient FromEntries(string name, IEnumerable<GradientColorEntry> entries)
    {
      var gradient = new Gradient { colorEntries = new List<GradientColorEntry>(), name = name };
      foreach (var entry in entries) gradient.colorEntries.Add(new GradientColorEntry(entry.Color, entry.Location));
      return gradient;
    }

    public Gradient Copy()
    {
      var copy = FromEntries(name, colorEntries);
      copy.fileName = fileName;
      return copy;
    }

    /// <summary>
    /// Inserts a color at the given location.
    /// </summary>
    /// <param name="color">The color to insert.</param>
    /// <param name="location">Location expressed as a value in the interval (0, 1), inclusive.</param>
    public void AddColor(Color color, double location)
    {
      if (location < 0.0 || location > 1.0) throw new Exception("Location out of range, must be between 0 and 1");
      colorEntries.Add(new GradientColorEntry(color, location));
      CreateSortedColorEntries();
    }

    /// <summary>
    /// Gets the color of the specified location.
    /// </summary>
    /// <param name="location">A location in the (0, 1) interval, inclusive.</param>
    /// <returns></returns>
    public Color GetColor(double location)
    {
      List<GradientColorEntry> sortedColorEntries = CreateSortedColorEntries();
      if (sortedColorEntries.Count > 0)
      {
        if (location <= sortedColorEntries[0].Location) return sortedColorEntries[0].Color;
        if (location >= sortedColorEntries[sortedColorEntries.Count - 1].Location)
          return sortedColorEntries[sortedColorEntries.Count - 1].Color;
        for (int i = 1; i < sortedColorEntries.Count; i++)
        {
          if (location <= sortedColorEntries[i].Location)
          {
            double t = (sortedColorEntries[i].Location == sortedColorEntries[i - 1].Location
                          ? 0.0
                          : (location - sortedColorEntries[i - 1].Location)/
                            (sortedColorEntries[i].Location - sortedColorEntries[i - 1].Location));
            Color c0 = sortedColorEntries[i - 1].Color;
            Color c1 = sortedColorEntries[i].Color;
            return Color.FromArgb(
              c0.A + (int) (t*(c1.A - c0.A)),
              c0.R + (int) (t*(c1.R - c0.R)),
              c0.G + (int) (t*(c1.G - c0.G)),
              c0.B + (int) (t*(c1.B - c0.B)));
          }
        }
      }
      return Color.Black;
    }

    /// <summary>
    /// The (possible unsorted) list of color entries.
    /// </summary>
    public List<GradientColorEntry> ColorEntries
    {
      get { return colorEntries; }
      set 
      { 
        colorEntries = value;
        CreateSortedColorEntries();
      }
    }

    private List<GradientColorEntry> CreateSortedColorEntries()
    {
      List<GradientColorEntry> sortedColorEntries = new List<GradientColorEntry>();

      foreach (GradientColorEntry colorEntry in colorEntries)
      {
        sortedColorEntries.Add(colorEntry);
      }
      sortedColorEntries.Sort();
      return sortedColorEntries;
    }

    /// <summary>
    /// The name of the gradient.
    /// </summary>
    public string Name
    {
      get { return name; }
      set { name = value; }
    }

    /// <summary>
    /// The name of the file where the gradient is stored.
    /// </summary>
    public string FileName
    {
      get { return fileName; }
      set { fileName = value; }
    }

    /// <summary>
    /// Checks whether obj is a gradient with the same name, colors and locations as this gradient.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public override bool Equals(object obj)
    {
      if (!(obj.GetType() == typeof(Gradient))) return false;

      Gradient other = obj as Gradient;
      if (other == null || name != other.Name) return false;
      for (int i = 0; i < colorEntries.Count; i++)
      {
        if (colorEntries[i].Color != other.ColorEntries[i].Color ||
           colorEntries[i].Location != other.ColorEntries[i].Location)
          return false;
      }
      return true;
    }

    public override int GetHashCode()
    {
      return base.GetHashCode();
    }

    /// <summary>
    /// Drawing direction of the gradient.
    /// </summary>
    public enum Direction
    {
      Horizontal,
      Vertical,
      ReverseHorizontal,
      ReverseVertical
    }
  }

  /// <summary>
  /// A color-location pair that describes an entry in a gradient color list.
  /// </summary>
  public class GradientColorEntry : IComparable<GradientColorEntry>
  {
    private Color color;
    private double location;

    public GradientColorEntry(Color color, double location)
    {
      this.color = color;
      this.location = location;
    }

    /// <summary>
    /// The color.
    /// </summary>
    public Color Color
    {
      get { return color; }
      set { color = value; }
    }

    /// <summary>
    /// The location, in the (0, 1) interval, inclusive.
    /// </summary>
    public double Location
    {
      get { return location; }
      set { location = value; }
    }

    /// <summary>
    /// Comprison of color entries based on their locations.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(GradientColorEntry other)
    {
      return Math.Sign(location - other.Location);
    }
  }

}
