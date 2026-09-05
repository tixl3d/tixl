using System.Text.RegularExpressions;

namespace Lib.@string.logic;

[Guid("7baaa83d-5c09-42a0-b7bc-35dbcfa5156d")]
internal sealed class PickStringPart : Instance<PickStringPart>
{
    [Output(Guid = "62368C06-7815-47BC-9B0D-3024A2907E01")]
    public readonly Slot<string> Fragments = new();

    [Output(Guid = "88888C06-7815-47BC-9B0D-3024A2907E01")]
    public readonly Slot<int> TotalCount = new();


    public PickStringPart()
    {
        Fragments.UpdateAction += Update;
        TotalCount.UpdateAction += Update;
    }

    private enum EntityTypes
    {
        Characters = 0,
        Words,
        Lines,
        Sentences,
    }

    private EntityTypes _splitInto;

    private void Update(EvaluationContext context)
    {
        if (InputText.DirtyFlag.IsDirty || SplitInto.DirtyFlag.IsDirty)
        {
            _splitInto = (EntityTypes)SplitInto.GetValue(context);
            var inputText = InputText.GetValue(context);

            if (inputText == null)
            {
                Fragments.Value = null;
                return;
            }
                
            if (!string.IsNullOrEmpty(inputText))
                inputText = inputText.Replace("\\n", "\n");

            switch (_splitInto)
            {
                case EntityTypes.Characters:
                    _chunks = Regex.Split(inputText, string.Empty);
                    //_chunks = inputText.ToCharArray();
                    //_chunks = new Regex("(.)").Split(inputText);
                    _delimiter = "";
                    break;

                case EntityTypes.Words:
                    _chunks = new Regex("[\\s\\.\\;\\,()`:]+").Split(inputText);
                    _delimiter = " ";
                    break;

                case EntityTypes.Lines:
                    _chunks = new Regex("\\n+").Split(inputText);
                    _delimiter = "\n";
                    break;

                case EntityTypes.Sentences:
                    _chunks = new Regex("\\.[\\s\\.]*").Split(inputText);
                    _delimiter = ". ";
                    break;
                default:
                    _chunks = new string[0];
                    break;
            }

            _numberOfChunks = _chunks.Length > 0 && string.IsNullOrEmpty(_chunks[_chunks.Length - 1])
                                  ? _chunks.Length - 1
                                  : _chunks.Length;
            //_lastFragment = "";
        }

        var fragmentStart = FragmentStart.GetValue(context);
        var fragmentCount = FragmentCount.GetValue(context);
        //if (_splitInto == EntityTypes.Characters)
        //    fragmentCount *= 2;

        Fragments.Value = GetFragment(fragmentStart, fragmentCount);
        TotalCount.Value = _chunks.Length;
            
        Fragments.DirtyFlag.Clear();
        TotalCount.DirtyFlag.Clear();
    }

    private string GetFragment(int startFragment, int fragmentCount)
    {
        if (fragmentCount <= 0 || _numberOfChunks == 0)
            return "";
            
        var sb = new StringBuilder();
        for (var index = 0;
             index < fragmentCount;
             index++)
        {
            if(index > 0)
                sb.Append(_delimiter);
                    
            var moduloIndex = (startFragment + index) % _numberOfChunks;
            if (moduloIndex < 0)
                moduloIndex += _numberOfChunks;

            //sb.Append(d);
            //sb.Append(_chunks[moduloIndex]);
            //d = _delimiter;
                
            sb.Append(_chunks[moduloIndex]);
        }

        return sb.ToString();
    }

        
    private int _numberOfChunks;
    private string[] _chunks;
    private string _delimiter;
        
    [Input(Guid = "05d7962b-a02e-4ab5-9927-865375348ccd")]
    public readonly InputSlot<string> InputText = new("Line\nLine");

    [Input(Guid = "5D7184F6-CF46-4CB0-B29F-B3C52B34B634", MappedType = typeof(EntityTypes))]
    public readonly InputSlot<int> SplitInto = new();


    [Input(Guid = "9CB908AD-0800-4B88-B256-C6CC2B84AB6C")]
    public readonly InputSlot<int> FragmentStart = new();

    [Input(Guid = "7520DB6D-7855-40E1-BB81-EAD290815435")]
    public readonly InputSlot<int> FragmentCount = new();
}