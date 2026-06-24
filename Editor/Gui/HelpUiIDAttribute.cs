#nullable enable
using System;

namespace T3.Editor.Gui;

/// <summary>
/// Tags an editor UI component (window, panel, popup, canvas area) with a stable id so help and
/// guided tours can reference and highlight it. The id is the bare form of a <c>ui:</c> topic in the
/// help UI-topic registry — e.g. <c>[HelpUiID("Timeline")]</c> corresponds to <c>ui:Timeline</c>.
/// Living in <c>T3.Editor.Gui</c>, it is in scope for any class under that namespace without a using.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class HelpUiIDAttribute : Attribute
{
    public string Id { get; }

    public HelpUiIDAttribute(string id) => Id = id;
}
