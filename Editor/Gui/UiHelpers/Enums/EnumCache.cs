using System.Reflection;

namespace T3.Editor.Gui.UiHelpers.Enums;

public sealed class EnumCache
{
    public static EnumCache Instance { get; } = new();

    public TypedEntry<T> GetTypedEnumEntry<T>() where T : Enum
    {
        Type enumType = typeof(T);
        if (!_entries.TryGetValue(enumType, out var entry))
        {
            entry = new TypedEntry<T>();
            _entries.Add(enumType, entry);
        }

        return entry as TypedEntry<T>;
    }

    public Entry GetEnumEntry(Type enumType)
    {
        if (!_entries.TryGetValue(enumType, out var entry))
        {
            entry = new Entry(enumType);
            _entries.Add(enumType, entry);
        }

        return entry;
    }

    public sealed class TypedEntry<T> : Entry where T : Enum
    {
        public TypedEntry() : base(typeof(T))
        {
            var values = Enum.GetValues(EnumType);
            Values = new T[ValueNames.Length];
            for (int i = 0; i < values.Length; i++)
            {
                Values[i] = (T)values.GetValue(i);
            }
        }

        public T this[int i] => Values[i];
        public T[] Values { get; }
    }

    public class Entry
    {
        public Entry(Type enumType)
        {
            EnumType = enumType;
            ValueNames = Enum.GetNames(enumType);
            var values = Enum.GetValues(enumType);
            ValuesAsInt = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                ValuesAsInt[i] = (int)values.GetValue(i);
            }

            IsFlagEnum = enumType.GetCustomAttributes<FlagsAttribute>().Any();
            if (IsFlagEnum)
            {
                SetFlags = new bool[values.Length];
            }

            Hints = new EnumOptionHint[values.Length];
            DisplayNames = new string[values.Length];
            SelectableIds = new string[values.Length];
            DisplayOrder = new int[values.Length];

            var provider = EnumOptionHintRegistry.Find(enumType);
            HasHints = provider != null;
            ImportantGroupTitle = provider?.ImportantGroupTitle;
            for (var i = 0; i < values.Length; i++)
            {
                var hint = provider?.GetHint(ValueNames[i], values.GetValue(i)) ?? default;
                Hints[i] = hint;
                DisplayNames[i] = hint.Label ?? ValueNames[i];

                // Two values may share a label, so the popup id keeps the value name.
                SelectableIds[i] = hint.Label != null ? $"{hint.Label}##{ValueNames[i]}" : ValueNames[i];
            }

            var orderIndex = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (Hints[i].Relevance == OptionRelevance.Important)
                    DisplayOrder[orderIndex++] = i;
            }

            ImportantCount = orderIndex;
            for (var i = 0; i < values.Length; i++)
            {
                if (Hints[i].Relevance != OptionRelevance.Important)
                    DisplayOrder[orderIndex++] = i;
            }
        }

        protected Type EnumType { get; }
        public string[] ValueNames { get; }
        public int[] ValuesAsInt { get; }
        public bool IsFlagEnum { get; }
        public bool[] SetFlags { get; }

        internal EnumOptionHint[] Hints { get; }

        /** The readable label where the enum has one, otherwise the value name. */
        internal string[] DisplayNames { get; }

        internal string[] SelectableIds { get; }

        /** Value indices with the important values first; <see cref="ImportantCount"/> of them. */
        internal int[] DisplayOrder { get; }

        internal int ImportantCount { get; }
        internal string ImportantGroupTitle { get; }
        internal bool HasHints { get; }
    }

    private readonly Dictionary<Type, Entry> _entries = new();
}
