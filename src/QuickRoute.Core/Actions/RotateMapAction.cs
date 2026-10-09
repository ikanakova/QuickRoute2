using System;

namespace QuickRoute.BusinessEntities.Actions
{
  /// <summary>
  /// Rotates only the map image a quarter turn; routes, handles and transformation matrices are left untouched.
  /// </summary>
  public class RotateMapAction : IAction
  {
    private readonly IMapImageService mapImageService;
    private Map originalMap;
    private Map rotatedMap;

    /// <summary>
    /// Angle in radians, counterclockwise direction
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// The document to rotate
    /// </summary>
    public Document Document { get; set; }

    public RotateMapAction(double angle, Document document, IMapImageService mapImageService)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      if (double.IsNaN(angle) || Math.Abs(Math.Abs(angle) - Math.PI / 2) > 1e-10)
        throw new ArgumentOutOfRangeException(nameof(angle));
      Angle = angle;
      Document = document;
      this.mapImageService = mapImageService ?? throw new ArgumentNullException(nameof(mapImageService));
    }

    public void Execute()
    {
      if (originalMap == null)
      {
        originalMap = Document.Map;
        rotatedMap = mapImageService.RotateQuarterTurn(originalMap, Angle < 0);
      }
      Document.Map = rotatedMap;
    }

    public void Undo()
    {
      if (originalMap != null) Document.Map = originalMap;
    }
  }
}
