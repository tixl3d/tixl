#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.io.video;

[Guid("608acba9-f310-4876-981b-ade0664c5c66")]
[ExportDependencies("libomtnet.dll", "libvmx.dll", "libvmx.so", "libvmx.dylib")]
public sealed class OmtInput : Instance<OmtInput>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "786f11e0-1e13-42f1-bcb0-1c07c8659be6", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Texture = new();

    public OmtInput()
    {
        Texture.UpdateAction += Update;

        // Discovery binds the platform's DNS-SD service on its own thread; starting it now means the source
        // list is filled by the time someone opens the dropdown.
        OmtRuntime.EnsureDiscoveryStarted();
    }

    private void Update(EvaluationContext context)
    {
        var reconnectTriggered = MathUtils.WasTriggered(TriggerReconnect.GetValue(context), ref _reconnect);
        if (reconnectTriggered)
            TriggerReconnect.SetTypedInputValue(false);

        if (SourceName.DirtyFlag.IsDirty || reconnectTriggered)
        {
            var sourceName = SourceName.GetValue(context);
            _receiver.Connect(IsPlaceholder(sourceName) ? null : sourceName);
        }

        _receiver.Update();
        Texture.Value = _receiver.Texture;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        // Returns at once: the receive thread frees its native receiver itself.
        _receiver.Dispose();
        Texture.Value = null;
    }

    private static bool IsPlaceholder(string? sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName) || sourceName == PlaceholderText;
    }

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        if (!OmtRuntime.IsAvailable)
            return IStatusProvider.StatusLevel.Error;

        if (_receiver.SourceName == null || _receiver.IsConnected)
            return IStatusProvider.StatusLevel.Success;

        return IStatusProvider.StatusLevel.Warning;
    }

    string? IStatusProvider.GetStatusMessage()
    {
        if (!OmtRuntime.IsAvailable)
            return OmtRuntime.MissingMessage;

        if (_receiver.SourceName == null)
            return OmtRuntime.DiscoveredSources.Count == 0 ? OmtRuntime.NoSourcesHint : PlaceholderText;

        return _receiver.IsConnected ? null : _receiver.StatusMessage;
    }
    #endregion

    #region source dropdown
    private const string PlaceholderText = "Select OMT source...";

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        var currentValue = SourceName.Value;

        // A typed omt://host:port address isn't announced but is a valid choice, so it is shown as is.
        return IsPlaceholder(currentValue) ? PlaceholderText : currentValue;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != SourceName.Id)
        {
            yield return "undefined";
            yield break;
        }

        yield return PlaceholderText;

        var sources = OmtRuntime.DiscoveredSources;
        for (var index = 0; index < sources.Count; index++)
            yield return sources[index];
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId != SourceName.Id)
            return;

        SourceName.SetTypedInputValue(IsPlaceholder(selected) ? string.Empty : selected!.Trim());
    }
    #endregion

    private readonly OmtVideoReceiver _receiver = new();
    private bool _reconnect;

    [Input(Guid = "6988eb41-8866-43b8-a6aa-124e413cecc4")]
    public readonly InputSlot<string> SourceName = new();

    [Input(Guid = "b1e6d486-3e33-4147-b03b-c30f8cd6caab")]
    public readonly InputSlot<bool> TriggerReconnect = new();
}
