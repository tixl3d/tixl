#nullable enable

using T3.Core.DataTypes.DataSet;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Undoable removal of either whole <see cref="DataChannel"/>s or events inside a time
/// range on selected channels. Backs the "Remove" button in the DataSet output view; the
/// two modes share a command so the user gets a single undo entry regardless of which
/// branch fired. Captures the data needed for restore at construction time; <see cref="Do"/>
/// applies the removal and <see cref="Undo"/> re-inserts in reverse order so indices line
/// up with the original list state.
/// </summary>
/// <remarks>
/// Event lists may be appended to from a background recorder thread, so every mutation
/// takes the per-channel <c>Events</c> lock. The command captures references to live
/// <see cref="DataChannel"/> objects; if the <see cref="DataSet"/> itself is replaced
/// (e.g. file reload) before undo runs, the restore quietly no-ops on the missing channels
/// rather than throwing.
/// </remarks>
internal sealed class RemoveDataSetItemsCommand : ICommand
{
    public string Name => _isChannelRemoval ? "Remove DataSet channels" : "Remove DataSet events";
    public bool IsUndoable => true;

    /// <summary>
    /// Channel-removal mode: drops the listed channels entirely (events and metadata).
    /// Caller supplies the channels to remove — usually the user's selection. Undo
    /// re-inserts them at their original indices.
    /// </summary>
    public static RemoveDataSetItemsCommand ForChannels(DataSet dataSet, IReadOnlyList<DataChannel> channels)
    {
        var entries = new List<ChannelEntry>(channels.Count);
        foreach (var channel in channels)
        {
            var index = dataSet.Channels.IndexOf(channel);
            if (index < 0)
                continue;
            entries.Add(new ChannelEntry(index, channel));
        }

        // Stable removal order: descending index so each remove doesn't shift the next.
        entries.Sort((a, b) => b.OriginalIndex.CompareTo(a.OriginalIndex));
        return new RemoveDataSetItemsCommand(dataSet, channelEntries: entries);
    }

    /// <summary>
    /// Event-range mode: deletes every event whose <see cref="DataEvent.Time"/> falls in
    /// <c>[min(rangeA, rangeB), max(rangeA, rangeB)]</c> on each supplied channel. If
    /// <paramref name="channels"/> is empty the full <see cref="DataSet.Channels"/> list is
    /// the implicit selection — matches the "no-channel-selection means all channels"
    /// behaviour the Remove button already used.
    /// </summary>
    public static RemoveDataSetItemsCommand ForEventRange(DataSet dataSet,
                                                          IReadOnlyList<DataChannel> channels,
                                                          double rangeA,
                                                          double rangeB)
    {
        var rangeMin = Math.Min(rangeA, rangeB);
        var rangeMax = Math.Max(rangeA, rangeB);

        var effective = channels.Count > 0 ? channels : (IReadOnlyList<DataChannel>)dataSet.Channels;
        var entries = new List<EventRangeEntry>(effective.Count);

        foreach (var channel in effective)
        {
            // Snapshot the indices + values that match the range so Undo can re-insert.
            // Walking once here is cheaper than re-scanning Events in Do() — and lets
            // Undo restore even if the channel is later cleared by another command.
            List<EventRestore>? matches = null;
            lock (channel.Events)
            {
                for (var i = 0; i < channel.Events.Count; i++)
                {
                    var ev = channel.Events[i];
                    if (ev == null)
                        continue;
                    if (ev.Time < rangeMin || ev.Time > rangeMax)
                        continue;
                    matches ??= new List<EventRestore>();
                    matches.Add(new EventRestore(i, ev));
                }
            }

            if (matches != null)
                entries.Add(new EventRangeEntry(channel, matches));
        }

        return new RemoveDataSetItemsCommand(dataSet, eventRangeEntries: entries);
    }

    private RemoveDataSetItemsCommand(DataSet dataSet,
                                      List<ChannelEntry>? channelEntries = null,
                                      List<EventRangeEntry>? eventRangeEntries = null)
    {
        _dataSet = dataSet;
        _channelEntries = channelEntries;
        _eventRangeEntries = eventRangeEntries;
        _isChannelRemoval = channelEntries != null;
    }

    public void Do()
    {
        if (_channelEntries != null)
        {
            // Entries are already in descending-index order so each remove leaves the
            // surviving indices intact for the next one.
            foreach (var entry in _channelEntries)
            {
                if (entry.OriginalIndex >= 0 && entry.OriginalIndex < _dataSet.Channels.Count
                    && ReferenceEquals(_dataSet.Channels[entry.OriginalIndex], entry.Channel))
                {
                    _dataSet.Channels.RemoveAt(entry.OriginalIndex);
                }
                else
                {
                    // Channel was moved/replaced since capture — fall back to a reference
                    // remove so we still drop the right object if it's still present.
                    _dataSet.Channels.Remove(entry.Channel);
                }
            }
            PersistChange();
            return;
        }

        if (_eventRangeEntries != null)
        {
            foreach (var entry in _eventRangeEntries)
            {
                lock (entry.Channel.Events)
                {
                    // Iterate the captured restores in descending index order so each
                    // removal at i doesn't shift the index of the next.
                    for (var i = entry.Events.Count - 1; i >= 0; i--)
                    {
                        var restore = entry.Events[i];
                        if (restore.OriginalIndex >= 0
                            && restore.OriginalIndex < entry.Channel.Events.Count
                            && ReferenceEquals(entry.Channel.Events[restore.OriginalIndex], restore.Event))
                        {
                            entry.Channel.Events.RemoveAt(restore.OriginalIndex);
                        }
                        else
                        {
                            // Index has drifted (e.g. concurrent recording). Reference
                            // remove keeps the contract — the event we promised to remove
                            // is gone, even if it costs an O(n) scan.
                            entry.Channel.Events.Remove(restore.Event);
                        }
                    }
                }
            }
            PersistChange();
        }
    }

    public void Undo()
    {
        if (_channelEntries != null)
        {
            // Insert in ascending order so each insertion lands at the captured index
            // without later inserts displacing earlier ones.
            for (var i = _channelEntries.Count - 1; i >= 0; i--)
            {
                var entry = _channelEntries[i];
                var insertAt = Math.Min(entry.OriginalIndex, _dataSet.Channels.Count);
                _dataSet.Channels.Insert(insertAt, entry.Channel);
            }
            PersistChange();
            return;
        }

        if (_eventRangeEntries != null)
        {
            foreach (var entry in _eventRangeEntries)
            {
                lock (entry.Channel.Events)
                {
                    // Restore in ascending captured-index order so earlier inserts
                    // don't push later ones out of place.
                    for (var i = 0; i < entry.Events.Count; i++)
                    {
                        var restore = entry.Events[i];
                        var insertAt = Math.Min(restore.OriginalIndex, entry.Channel.Events.Count);
                        entry.Channel.Events.Insert(insertAt, restore.Event);
                    }
                }
            }
            PersistChange();
        }
    }

    /// <summary>
    /// Mirrors the in-memory mutation to the source <c>.data</c> file. If the dataset
    /// isn't cache-owned (e.g. an in-flight recording before <c>EndRecording</c> has
    /// finalised the file) the edit stays in memory only — logged so the user can spot
    /// that the change won't survive a reload, but not surfaced as an error since the
    /// in-memory effect still played out.
    /// </summary>
    private void PersistChange()
    {
        if (!DataSetCache.TryWriteBack(_dataSet, out var reason))
        {
            Log.Warning($"RemoveDataSetItemsCommand: in-memory edit applied but not persisted ({reason})");
        }
    }

    private readonly DataSet _dataSet;
    private readonly List<ChannelEntry>? _channelEntries;
    private readonly List<EventRangeEntry>? _eventRangeEntries;
    private readonly bool _isChannelRemoval;

    private readonly record struct ChannelEntry(int OriginalIndex, DataChannel Channel);
    private readonly record struct EventRestore(int OriginalIndex, DataEvent? Event);
    private sealed record EventRangeEntry(DataChannel Channel, List<EventRestore> Events);
}
