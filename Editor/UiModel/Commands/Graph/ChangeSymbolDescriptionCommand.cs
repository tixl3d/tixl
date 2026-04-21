namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class ChangeSymbolDescriptionCommand : ICommand
{
    public string Name => "Change symbol description";
    public bool IsUndoable => true;

    internal ChangeSymbolDescriptionCommand(Guid symbolId,
                                            string previousDescription,
                                            IEnumerable<ExternalLink> previousLinks,
                                            string newDescription,
                                            IEnumerable<ExternalLink> newLinks)
    {
        _symbolId = symbolId;
        _previousDescription = previousDescription;
        _newDescription = newDescription;
        _previousLinks = CloneAll(previousLinks);
        _newLinks = CloneAll(newLinks);
    }

    public void Do() => Apply(_newDescription, _newLinks);
    public void Undo() => Apply(_previousDescription, _previousLinks);

    private void Apply(string description, List<ExternalLink> links)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
            return;

        symbolUi.Description = description;
        symbolUi.Links.Clear();
        foreach (var link in links)
            symbolUi.Links.Add(link.Id, link.Clone());

        symbolUi.FlagAsModified();
    }

    private static List<ExternalLink> CloneAll(IEnumerable<ExternalLink> source)
    {
        var list = new List<ExternalLink>();
        foreach (var link in source)
            list.Add(link.Clone());
        return list;
    }

    private readonly Guid _symbolId;
    private readonly string _previousDescription;
    private readonly string _newDescription;
    private readonly List<ExternalLink> _previousLinks;
    private readonly List<ExternalLink> _newLinks;
}
