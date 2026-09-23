#nullable enable
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.SingleControl;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

/// <summary>
/// Picker for Guid inputs. The <see cref="Usage"/> setting (persisted in .t3ui, mirroring
/// StringInputUi's Usage) defines what the input references and what the dropdown lists —
/// names are display only, the Guid is stored, so renames never break bindings. An id that
/// no longer resolves shows as explicitly unresolved, never silently re-matched.
/// Future usages (symbol children, variations, ...) are added as enum values + list providers.
/// </summary>
public sealed class GuidInputUi : SingleControlInputUi<Guid>
{
    public enum UsageType
    {
        Undefined,
        OutputSetupEntities,
    }

    public UsageType Usage { get; private set; } = UsageType.Undefined;

    public override IInputUi Clone()
    {
        return new GuidInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Usage = Usage,
                   };
    }

    protected override bool DrawSingleEditControl(string name, ref Guid value)
    {
        var modified = false;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##guidRef", GetDisplayLabel(value), ImGuiComboFlags.HeightLargest))
        {
            if (CustomComponents.DrawMenuItem(0, NoneLabel, isChecked: value == Guid.Empty))
            {
                value = Guid.Empty;
                modified = true;
            }

            if (Usage == UsageType.OutputSetupEntities && OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            {
                var itemId = 1;
                modified |= DrawEntityGroup("Outputs", setup.Outputs, o => o.Id, o => o.Name, ref value, ref itemId);
                modified |= DrawEntityGroup("Surfaces", setup.Surfaces, s => s.Id, s => s.Name, ref value, ref itemId);
                modified |= DrawEntityGroup("Reference Images", setup.ReferenceImages, r => r.Id, r => r.Name, ref value, ref itemId);
            }
            else if (Usage == UsageType.Undefined)
            {
                CustomComponents.DrawMenuGroupLabel("No usage defined — set one in the input settings");
            }

            ImGui.EndCombo();
        }

        return modified;
    }

    protected override void DrawReadOnlyControl(string name, ref Guid value)
    {
        ImGui.TextUnformatted(GetDisplayLabel(value));
    }

    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();
        FormInputs.AddVerticalSpace();

        FormInputs.DrawFieldSetHeader("Usage");
        {
            var tmpForRef = Usage;
            if (FormInputs.AddEnumDropdown(ref tmpForRef, null))
            {
                modified = true;
                Usage = tmpForRef;
            }
        }

        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        if (Usage != UsageType.Undefined)
            writer.WriteObject(nameof(Usage), Usage.ToString());
    }

    public override void Read(JToken? inputToken)
    {
        if (inputToken == null)
            return;

        base.Read(inputToken);

        var usageToken = inputToken[nameof(Usage)];
        if (usageToken != null && Enum.TryParse<UsageType>(usageToken.Value<string>(), out var usageValue))
        {
            Usage = usageValue;
        }
    }

    private static bool DrawEntityGroup<T>(string title, List<T> entities, Func<T, Guid> getId, Func<T, string> getName, ref Guid value, ref int itemId)
    {
        if (entities.Count == 0)
            return false;

        CustomComponents.MenuGroupHeader(title);
        var modified = false;
        foreach (var entity in entities)
        {
            var id = getId(entity);
            var entityName = getName(entity);
            if (CustomComponents.DrawMenuItem(itemId++, string.IsNullOrEmpty(entityName) ? "untitled" : entityName,
                                              isChecked: id == value))
            {
                value = id;
                modified = true;
            }
        }

        return modified;
    }

    private string GetDisplayLabel(Guid value)
    {
        if (value == Guid.Empty)
            return NoneLabel;

        if (Usage == UsageType.OutputSetupEntities && OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            foreach (var output in setup.Outputs)
            {
                if (output.Id == value)
                    return output.Name;
            }

            foreach (var surface in setup.Surfaces)
            {
                if (surface.Id == value)
                    return surface.Name;
            }

            foreach (var image in setup.ReferenceImages)
            {
                if (image.Id == value)
                    return image.Name;
            }
        }

        // Bindings into another venue's setup stay visibly broken until the user re-assigns
        return $"? unresolved ({value.ToString()[..8]}…)";
    }

    private const string NoneLabel = "— none —";
}
