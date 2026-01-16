using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Styling;

namespace T3.Editor.UiModel;

// Public for serialization...
[SuppressMessage("ReSharper", "MemberCanBeInternal")]
public sealed class ExternalLink
{
    public ExternalLink()
    {
        Id = Guid.NewGuid();
    }
        
    public string Title=string.Empty;
    public string Url ="https://tixl.app";
    public string Description = String.Empty;
    public LinkTypes Type = LinkTypes.Other;
    public Guid Id { get; init; }

    public enum LinkTypes
    {
        TutorialVideo =0,
        Documentation=1,
        Example=2,
        Reference=3,
        Source=4,
        OriginalBy,
        Author,
        Other = 99,
    }

    public ExternalLink Clone()
    {
        return new ExternalLink()
                   {
                       Id = Id,
                       Title = Title,
                       Description = Description,
                       Url = Url,
                       Type = Type
                   };
    }

    public static readonly Dictionary<ExternalLink.LinkTypes, Icon> LinkIcons
        = new()
              {
                  { ExternalLink.LinkTypes.TutorialVideo, Icon.PlayOutput },
                  { ExternalLink.LinkTypes.Example, Icon.Tip },
                  { ExternalLink.LinkTypes.Reference, Icon.Help },
                  { ExternalLink.LinkTypes.Documentation, Icon.Help },
              };
}