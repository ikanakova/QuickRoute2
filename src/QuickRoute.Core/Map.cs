using System;
using System.Drawing;

namespace QuickRoute.BusinessEntities
{
  /// <summary>
  /// The map image of a document. The image is kept in its encoded form (e.g. JPEG or PNG) so that it can be stored losslessly;
  /// decoding is performed by the rendering layer.
  /// </summary>
  public class Map
  {
    private readonly byte[] encodedImage;

    public Map(byte[] encodedImage, Size size, string source = null, MapSourceType sourceType = MapSourceType.FileSystem, MapStorageType storageType = MapStorageType.Inline)
    {
      if (encodedImage == null || encodedImage.Length == 0) throw new ArgumentException("The map image data is empty.", nameof(encodedImage));
      if (size.Width <= 0 || size.Height <= 0) throw new ArgumentOutOfRangeException(nameof(size));
      this.encodedImage = encodedImage;
      Size = size;
      Source = source;
      SourceType = sourceType;
      StorageType = storageType;
    }

    /// <summary>
    /// The encoded image bytes. The returned array must not be modified.
    /// </summary>
    public byte[] EncodedImage => encodedImage;

    public Size Size { get; }

    public int Width => Size.Width;

    public int Height => Size.Height;

    public MapStorageType StorageType { get; set; }

    public MapSourceType SourceType { get; }

    public string Source { get; }
  }

  /// <summary>
  /// The type of storage of the map image when the map object is serialized.
  /// </summary>
  public enum MapStorageType
  {
    /// <summary>
    /// The map image is stored as a byte array in the object itself
    /// </summary>
    Inline,
    /// <summary>
    /// The map image is stored as a reference to a file or url
    /// </summary>
    Reference
  }

  /// <summary>
  /// The location type of the map image file.
  /// </summary>
  public enum MapSourceType
  {
    /// <summary>
    /// The source is a file in the file system
    /// </summary>
    FileSystem,
    /// <summary>
    /// The source is a file on an internet url 
    /// </summary>
    Url
  }

  /// <summary>
  /// Image operations on map images that require an image codec.
  /// </summary>
  public interface IMapImageService
  {
    /// <summary>
    /// Creates a map from encoded image data. Borders and header of exported QuickRoute images are removed.
    /// </summary>
    Map CreateMap(byte[] encodedImage, string source = null, MapSourceType sourceType = MapSourceType.FileSystem);

    /// <summary>
    /// Creates a new map rotated by a quarter turn.
    /// </summary>
    /// <param name="map">The map to rotate.</param>
    /// <param name="clockwise">True to rotate clockwise, false to rotate counterclockwise.</param>
    Map RotateQuarterTurn(Map map, bool clockwise);
  }
}
