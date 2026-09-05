

// The `using static T3.Core.Operator.Symbol.Child;` statement from the prompt
// is not required for the logic within this specific class.

namespace Lib.numbers.floats.conversion
{
    [Guid("de95667b-4b5e-40fc-a6d1-a672c529960e")]
    internal sealed class FloatListToIntList : Instance<FloatListToIntList>
    {
        [Input(Guid = "152697b9-b4b5-454e-9cd1-3bc80e88ab5c")]
        public readonly InputSlot<List<float>> FloatList = new();

        [Output(Guid = "b51b9aca-ce74-464a-ba24-b93e274f5eee")]
        public readonly Slot<List<int>> Result = new();

        public FloatListToIntList()
        {
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            var floatValues = FloatList.GetValue(context);
            if (floatValues == null)
            {
                // To prevent errors in connected operators, provide an empty list if the input is null.
                Result.Value = new List<int>();
                return;
            }

            // Using LINQ's .Select() method is a clean and efficient way to convert all items in the list.
            // Casting a float to an int truncates the decimal part (e.g., 9.8f becomes 9).
            Result.Value = floatValues.Select(f => (int)f).ToList();
        }
    }
}