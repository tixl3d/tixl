using Clockworks.CLX;
using T3.Core.Animation;

namespace Clx.Utils;

/// <summary>
/// One <see cref="CLXClient"/> per UDP port, shared by every CLX operator listening on it and
/// closed when the last one lets go. The client receives on its own thread and keeps the latest
/// state, which operators read directly; <see cref="Connection.Update"/> pumps its queued events
/// once per frame on the main thread.
/// </summary>
internal static class ClxConnections
{
    internal const int DefaultPort = CLXClient.DefaultPort;

    internal static Connection Acquire(int port)
    {
        lock (_lock)
        {
            if (!_byPort.TryGetValue(port, out var connection))
            {
                connection = new Connection(port);
                _byPort[port] = connection;
            }

            connection.RefCount++;
            return connection;
        }
    }

    internal static void Release(Connection connection)
    {
        if (connection == null)
            return;

        lock (_lock)
        {
            connection.RefCount--;
            if (connection.RefCount > 0)
                return;

            _byPort.Remove(connection.Port);
            connection.Close();
        }
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<int, Connection> _byPort = new();

    internal sealed class Connection
    {
        internal Connection(int port)
        {
            Port = port;
            Client = new CLXClient(new CLXClientOptions { ListenPort = port });
            Client.EventReceived += OnEvent;
            Client.TrackChanged += OnTrackChanged;
            Client.HandlerError += e => Log.Warning($"CLX: event handler failed: {e.Message}");
            IsStarted = Client.Start();
            if (!IsStarted)
                Log.Warning($"CLX: couldn't listen on port {port}: {Client.LastError}");
        }

        internal readonly int Port;
        internal readonly CLXClient Client;
        internal readonly bool IsStarted;
        internal int RefCount;

        /// <summary>Raises the client's queued events. Safe to call from every operator: it runs once per frame.</summary>
        internal void Update()
        {
            var frame = Playback.FrameCount;
            if (frame == _lastPumpFrame)
                return;

            _lastPumpFrame = frame;
            Client.Pump();
        }

        /// <summary>Events received so far, oldest first, with increasing sequence numbers.</summary>
        internal IReadOnlyList<ReceivedEvent> RecentEvents => _events;

        /// <summary>Counts track changes per deck (index 0 to 6), so an operator can see that one happened.</summary>
        internal int GetTrackChangeCount(int deck) => deck is >= 0 and < MaxDecks ? _trackChanges[deck] : 0;

        internal IStatusProvider.StatusLevel GetStatus(out string message)
        {
            if (!IsStarted)
            {
                message = $"Can't listen on port {Port}: {Client.LastError}";
                return IStatusProvider.StatusLevel.Error;
            }

            var age = Client.SecondsSinceLastPacket;
            if (double.IsPositiveInfinity(age))
            {
                message = $"Listening on port {Port} and multicast {CLXClient.MulticastGroup}. No CLX sender heard yet.";
                return IStatusProvider.StatusLevel.Warning;
            }

            if (age > StaleAfterSeconds)
            {
                message = $"No CLX packets for {age:0} s.";
                return IStatusProvider.StatusLevel.Warning;
            }

            message = null;
            return IStatusProvider.StatusLevel.Success;
        }

        internal void Close()
        {
            Client.EventReceived -= OnEvent;
            Client.TrackChanged -= OnTrackChanged;
            Client.Dispose();
        }

        private void OnEvent(EventPacket packet)
        {
            _eventSequence++;
            if (_events.Count >= MaxEvents)
                _events.RemoveAt(0);

            _events.Add(new ReceivedEvent(_eventSequence, packet));
        }

        private void OnTrackChanged(MetaPacket meta)
        {
            if (meta.Deck is >= 0 and < MaxDecks)
                _trackChanges[meta.Deck]++;
        }

        private const int MaxEvents = 256;
        private const int MaxDecks = 7;
        private const double StaleAfterSeconds = 2;

        private int _lastPumpFrame = -1;
        private long _eventSequence;
        private readonly List<ReceivedEvent> _events = new();
        private readonly int[] _trackChanges = new int[MaxDecks];
    }

    internal readonly record struct ReceivedEvent(long Sequence, EventPacket Packet);
}

/// <summary>
/// The part every CLX operator shares: it holds a connection for the chosen port, swaps it when
/// the port changes or listening is turned off, and reports its status.
/// </summary>
internal sealed class ClxBinding
{
    /// <summary>The client to read from, or null when not listening. Call once per update.</summary>
    internal CLXClient Update(int port, bool listen)
    {
        if (port is < 1 or > 65535)
        {
            Release();
            _invalidPort = true;
            return null;
        }

        _invalidPort = false;
        if (!listen)
        {
            Release();
            return null;
        }

        if (_connection == null || _connection.Port != port)
        {
            Release();
            _connection = ClxConnections.Acquire(port);
        }

        _connection.Update();
        return _connection.IsStarted ? _connection.Client : null;
    }

    internal ClxConnections.Connection Connection => _connection;

    internal void Release()
    {
        ClxConnections.Release(_connection);
        _connection = null;
    }

    internal IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (_invalidPort)
            return IStatusProvider.StatusLevel.Warning;

        return _connection?.GetStatus(out _) ?? IStatusProvider.StatusLevel.Notice;
    }

    internal string GetStatusMessage()
    {
        if (_invalidPort)
            return "Port must be between 1 and 65535.";

        if (_connection == null)
            return "Not listening.";

        _connection.GetStatus(out var message);
        return message;
    }

    private ClxConnections.Connection _connection;
    private bool _invalidPort;
}
