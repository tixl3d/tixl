using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using T3.Core.Utils;
using T3.Serialization;

namespace T3.Editor.UiModel.Selection;

internal sealed class TourPoint
{
    public string Description = string.Empty;
    public Guid Id { get; internal set; }
    public Guid ChildId;
    public Guid InputId;

    [JsonConverter(typeof(SafeEnumConverter<Styles>))]
    public Styles Style = Styles.Info;

    #region temp for markdown parsing
    [JsonIgnore]
    public string ShortId = string.Empty;
    
    [JsonIgnore]
    public string StyleAndTargetString = string.Empty;
    #endregion
    
    public enum Styles
    {
        Info,
        InfoFor,
        CallToAction,
        Conclusion,
        Tip,
    }

    internal TourPoint Clone()
    {
        return new TourPoint
                   {
                       Id = Guid.NewGuid(),
                       Description = Description,
                       Style = Style,
                   };
    }

}