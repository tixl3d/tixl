using System;

namespace T3.Core.Operator;

public abstract partial class Instance
{
    [Flags]
    public enum InstanceStatus
    {
        Uninitialized = 0,
        Initialized = 1 << 0,
        Active = 1 << 1, 
        ConnectedInternally = 1 << 2, // all our children are connected to each other or me
        IsReconnecting = 1 << 3, // this instance is currently reconnecting to its parent or children
        ResourceFoldersDirty = 1 << 4, // resource folders have been modified and need to be reloaded
        Bypassed = 1 << 5, // this instance is bypassed and should not be executed
        Disposed = 1 << 6 // this instance has been disposed and should not be used anymore
    }

    public InstanceStatus Status
    {
        get
        {
            // check parent instance existence before reporting our status
            if (_parentSymbolChild == null ||!TryGetParentInstance(out _, false))
            {
                // if we have no existing parent instance, we are not active (and can be safely discarded)
                _status &= ~InstanceStatus.Active; // remove active status
            }

            return _status;
        }
    }

    private InstanceStatus _status;

    internal bool IsReconnecting => (_status & InstanceStatus.IsReconnecting) == InstanceStatus.IsReconnecting;
    internal bool Initialized => (_status & InstanceStatus.Initialized) == InstanceStatus.Initialized;
    internal bool NeedsInternalReconnections => (_status & InstanceStatus.ConnectedInternally) != InstanceStatus.ConnectedInternally;
    public bool IsDisposed => (_status & InstanceStatus.Disposed) == InstanceStatus.Disposed;
}