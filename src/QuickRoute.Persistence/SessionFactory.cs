using System.Collections.Generic;
using System.Drawing;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  internal static class SessionFactory
  {
    /// <summary>
    /// Recreates a stored session, keeping the stored transformation matrices of all handles.
    /// </summary>
    public static Session Create(Route route, LapCollection laps, Size mapSize, GeneralMatrix initialTransformationMatrix,
      LongLat projectionOrigin, SessionSettings settings, IEnumerable<Handle> handles, SessionInfo sessionInfo)
    {
      var session = new Session(route, laps, mapSize, initialTransformationMatrix, projectionOrigin, settings);
      foreach (var handle in handles) session.AddHandle(handle);
      session.SessionInfo = sessionInfo ?? new SessionInfo();
      session.CreateAdjustedRoute();
      return session;
    }
  }
}
