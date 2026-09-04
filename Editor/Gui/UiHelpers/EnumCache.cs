using System.Reflection;

namespace T3.Editor.Gui.UiHelpers;

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
        }

        protected Type EnumType { get; }
        public string[] ValueNames { get; }
        public int[] ValuesAsInt { get; }
        public bool IsFlagEnum { get; }
        public bool[] SetFlags { get; }
    }

    private readonly Dictionary<Type, Entry> _entries = new();
}