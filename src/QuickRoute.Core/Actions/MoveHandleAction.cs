using QuickRoute.BusinessEntities;

namespace QuickRoute.BusinessEntities.Actions
{
  public class MoveHandleAction : IAction 
  {
    private Handle handle;
    private Session session;
    private PointD oldLocation;
    private PointD newLocation;
    private Session.AdjustmentState oldAdjustment;
    private Session.AdjustmentState newAdjustment;

    public MoveHandleAction(Handle handle, Session session, PointD oldLocation)
    {
      this.handle = handle;
      this.session = session;
      this.oldLocation = oldLocation;
      this.newLocation = handle.Location; 
    }

    public MoveHandleAction(Handle handle, Session session, PointD oldLocation, Session.AdjustmentState oldAdjustment)
      : this(handle, session, oldLocation)
    {
      this.oldAdjustment = oldAdjustment;
      newAdjustment = session.CaptureAdjustment();
    }

    public Handle Handle
    {
      get { return handle; }
      set { handle = value; }
    }

    public Session Session
    {
      get { return session; }
      set { session = value; }
    }

    public PointD OldLocation
    {
      get { return oldLocation; }
      set { oldLocation = value; }
    }

    public PointD NewLocation
    {
      get { return newLocation; }
      set { newLocation = value; }
    }

    public void Execute()
    {
      if (newAdjustment != null)
      {
        session.RestoreAdjustment(newAdjustment);
        return;
      }
      handle.Location = newLocation;
      session.UpdateHandle(handle);
    }

    public void Undo()
    {
      if (oldAdjustment != null)
      {
        session.RestoreAdjustment(oldAdjustment);
        return;
      }
      handle.Location = oldLocation;
      session.UpdateHandle(handle);
    }

  }
}
