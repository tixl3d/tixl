#nullable enable
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Inspects a loaded scene: node structure, skeletons as joint trees, animation clips and draw dispatches.
/// Meant for checking what actually made it out of a file, e.g. whether an exported glTF contains its clips.
/// </summary>
internal sealed class SceneSetupOutputUi : OutputUi<SceneSetup>
{
    public override IOutputUi Clone()
    {
        return new SceneSetupOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    private enum Views
    {
        Nodes,
        Skeletons,
        Clips,
        Dispatches,
    }

    private sealed class ViewSettings
    {
        public Views View = Views.Nodes;
    }

    private static readonly Dictionary<string, ViewSettings> _viewSettingsForId = [];

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<SceneSetup> sceneSetupSlot)
            return;

        var setup = sceneSetupSlot.Value;
        if (setup == null)
        {
            CustomComponents.StylizedText("Undefined setup", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        viewId ??= string.Empty;
        if (!_viewSettingsForId.TryGetValue(viewId, out var settings))
        {
            settings = new ViewSettings();
            _viewSettingsForId[viewId] = settings;
        }

        CustomComponents.SegmentedButton(ref settings.View);
        ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
        DrawSummary(setup);

        switch (settings.View)
        {
            case Views.Nodes:
                DrawNodes(setup);
                break;
            case Views.Skeletons:
                DrawSkeletons(setup);
                break;
            case Views.Clips:
                DrawClips(setup);
                break;
            case Views.Dispatches:
                DrawDispatches(setup);
                break;
        }
    }

    private static void DrawSummary(SceneSetup setup)
    {
        var length = 0;
        AppendInt(setup.Skeletons.Count, ref length);
        AppendText(" skeletons  ", ref length);
        AppendInt(setup.AnimationClips.Count, ref length);
        AppendText(" clips  ", ref length);
        AppendInt(setup.Dispatches.Count, ref length);
        AppendText(" dispatches", ref length);

        ImGui.AlignTextToFramePadding();
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(_formatBuffer.AsSpan(0, length));
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    #region nodes
    private static void DrawNodes(SceneSetup setup)
    {
        if (setup.RootNodes.Count == 0)
        {
            CustomComponents.StylizedText("No nodes", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        if (!BeginTable("##sceneNodes", 3))
            return;

        SetupColumn("Node", ImGuiTableColumnFlags.WidthStretch, 0);
        SetupColumn("Mesh", ImGuiTableColumnFlags.WidthFixed, 140);
        SetupColumn("Skeleton", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableHeadersRow();

        for (var index = 0; index < setup.RootNodes.Count; index++)
        {
            ImGui.PushID(index);
            DrawNodeRow(setup.RootNodes[index]);
            ImGui.PopID();
        }

        EndTable();
    }

    private static void DrawNodeRow(SceneSetup.SceneNode node)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var flags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth;
        if (node.ChildNodes.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        var isOpen = ImGui.TreeNodeEx(string.IsNullOrEmpty(node.Name) ? "(unnamed)" : node.Name, flags);

        ImGui.TableNextColumn();
        TextMutedOrValue(node.MeshName);

        ImGui.TableNextColumn();
        if (node.SkeletonIndex >= 0)
            TextInt(node.SkeletonIndex);

        if (!isOpen || node.ChildNodes.Count == 0)
            return;

        for (var index = 0; index < node.ChildNodes.Count; index++)
        {
            ImGui.PushID(index);
            DrawNodeRow(node.ChildNodes[index]);
            ImGui.PopID();
        }

        ImGui.TreePop();
    }
    #endregion

    #region skeletons
    private static void DrawSkeletons(SceneSetup setup)
    {
        if (setup.Skeletons.Count == 0)
        {
            CustomComponents.StylizedText("No skeletons. The file has no skins, or the mesh isn't bound to an armature.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        if (!BeginTable("##sceneJoints", 3))
            return;

        SetupColumn("Joint", ImGuiTableColumnFlags.WidthStretch, 0);
        SetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
        SetupColumn("Parent", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        for (var skeletonIndex = 0; skeletonIndex < setup.Skeletons.Count; skeletonIndex++)
        {
            var skeleton = setup.Skeletons[skeletonIndex];
            var jointCount = skeleton.ParentIndices.Length;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var length = 0;
            AppendText("Skeleton ", ref length);
            AppendInt(skeletonIndex, ref length);
            AppendText(" (", ref length);
            AppendInt(jointCount, ref length);
            AppendText(" joints)", ref length);

            ImGui.PushID(skeletonIndex);
            var isOpen = ImGui.TreeNodeEx("##skeleton", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth);
            ImGui.SameLine();
            ImGui.TextUnformatted(_formatBuffer.AsSpan(0, length));

            if (isOpen)
            {
                for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
                {
                    var parentIndex = skeleton.ParentIndices[jointIndex];
                    if (parentIndex < 0 || parentIndex >= jointCount)
                        DrawJointRow(skeleton, jointIndex);
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        EndTable();
    }

    /// <remarks>Children are found by scanning the parent indices. That's quadratic, but joint counts are small and it needs no allocation.</remarks>
    private static void DrawJointRow(SceneSetup.SceneSkeleton skeleton, int jointIndex)
    {
        var parentIndices = skeleton.ParentIndices;
        var hasChildren = false;
        for (var other = 0; other < parentIndices.Length; other++)
        {
            if (parentIndices[other] != jointIndex)
                continue;

            hasChildren = true;
            break;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var flags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        ImGui.PushID(jointIndex);
        var name = skeleton.JointNames[jointIndex];
        var isOpen = ImGui.TreeNodeEx(string.IsNullOrEmpty(name) ? "(unnamed)" : name, flags);
        ImGui.PopID();

        ImGui.TableNextColumn();
        TextInt(jointIndex);

        ImGui.TableNextColumn();
        TextInt(parentIndices[jointIndex]);

        if (!isOpen || !hasChildren)
            return;

        for (var child = 0; child < parentIndices.Length; child++)
        {
            if (parentIndices[child] == jointIndex)
                DrawJointRow(skeleton, child);
        }

        ImGui.TreePop();
    }
    #endregion

    #region clips
    private static void DrawClips(SceneSetup setup)
    {
        if (setup.AnimationClips.Count == 0)
        {
            CustomComponents.StylizedText(setup.Skeletons.Count == 0
                                              ? "No clips. Clips are only kept when they animate the joints of a skeleton."
                                              : "No clips. The file has no animations targeting skeleton joints.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        if (!BeginTable("##sceneClips", 7))
            return;

        // Index matches the ClipIndex input of [SampleGltfAnimation]
        SetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 40);
        SetupColumn("Clip", ImGuiTableColumnFlags.WidthStretch, 0);
        SetupColumn("Seconds", ImGuiTableColumnFlags.WidthFixed, 60);
        SetupColumn("Joints", ImGuiTableColumnFlags.WidthFixed, 50);
        SetupColumn("Pos", ImGuiTableColumnFlags.WidthFixed, 40);
        SetupColumn("Rot", ImGuiTableColumnFlags.WidthFixed, 40);
        SetupColumn("Scale", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableHeadersRow();

        for (var clipIndex = 0; clipIndex < setup.AnimationClips.Count; clipIndex++)
        {
            var clip = setup.AnimationClips[clipIndex];

            int translationCount = 0, rotationCount = 0, scaleCount = 0;
            for (var channelIndex = 0; channelIndex < clip.Channels.Count; channelIndex++)
            {
                var channel = clip.Channels[channelIndex];
                if (channel.TranslationSampler != null) translationCount++;
                if (channel.RotationSampler != null) rotationCount++;
                if (channel.ScaleSampler != null) scaleCount++;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            TextInt(clipIndex);

            ImGui.TableNextColumn();
            TextMutedOrValue(clip.Name);

            ImGui.TableNextColumn();
            clip.Duration.TryFormat(_formatBuffer, out var durationLength, "0.000", CultureInfo.InvariantCulture);
            ImGui.TextUnformatted(_formatBuffer.AsSpan(0, durationLength));

            ImGui.TableNextColumn();
            TextInt(clip.Channels.Count);

            ImGui.TableNextColumn();
            TextInt(translationCount);

            ImGui.TableNextColumn();
            TextInt(rotationCount);

            ImGui.TableNextColumn();
            TextInt(scaleCount);
        }

        EndTable();
    }
    #endregion

    #region dispatches
    private static void DrawDispatches(SceneSetup setup)
    {
        if (setup.Dispatches.Count == 0)
        {
            CustomComponents.StylizedText("No draw dispatches", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        if (!BeginTable("##sceneDispatches", 5))
            return;

        SetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 40);
        SetupColumn("Vertices", ImGuiTableColumnFlags.WidthFixed, 70);
        SetupColumn("Chunk", ImGuiTableColumnFlags.WidthFixed, 50);
        SetupColumn("Skeleton", ImGuiTableColumnFlags.WidthFixed, 60);
        SetupColumn("Skin Weights", ImGuiTableColumnFlags.WidthStretch, 0);
        ImGui.TableHeadersRow();

        for (var dispatchIndex = 0; dispatchIndex < setup.Dispatches.Count; dispatchIndex++)
        {
            var dispatch = setup.Dispatches[dispatchIndex];

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            TextInt(dispatchIndex);

            ImGui.TableNextColumn();
            TextInt(dispatch.VertexCount);

            ImGui.TableNextColumn();
            TextInt(dispatch.ChunkIndex);

            ImGui.TableNextColumn();
            if (dispatch.SkeletonIndex >= 0)
                TextInt(dispatch.SkeletonIndex);

            ImGui.TableNextColumn();
            TextMutedOrValue(dispatch.SkinWeights != null ? "yes" : null);
        }

        EndTable();
    }
    #endregion

    #region helpers
    private static bool BeginTable(string id, int columnCount)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV
                                      | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable(id, columnCount, flags, ImGui.GetContentRegionAvail()))
            return false;

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TableSetupScrollFreeze(0, 1);
        return true;
    }

    private static void EndTable()
    {
        ImGui.PopFont();
        ImGui.EndTable();
    }

    private static void SetupColumn(string label, ImGuiTableColumnFlags flags, float width)
    {
        ImGui.TableSetupColumn(label, flags, width * T3Ui.UiScaleFactor);
    }

    private static void TextInt(int value)
    {
        value.TryFormat(_formatBuffer, out var length, default, CultureInfo.InvariantCulture);
        ImGui.TextUnformatted(_formatBuffer.AsSpan(0, length));
    }

    private static void TextMutedOrValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            CustomComponents.StylizedText("-", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        ImGui.TextUnformatted(value);
    }

    private static void AppendText(string text, ref int length)
    {
        text.AsSpan().CopyTo(_formatBuffer.AsSpan(length));
        length += text.Length;
    }

    private static void AppendInt(int value, ref int length)
    {
        value.TryFormat(_formatBuffer.AsSpan(length), out var written, default, CultureInfo.InvariantCulture);
        length += written;
    }

    private static readonly char[] _formatBuffer = new char[128];
    #endregion
}
