namespace Lib.numbers.@int.process;

[Guid("f99cb938-205d-4605-8c17-e0be67f25d98")]
internal sealed class GetAPrime : Instance<GetAPrime>
{
    [Output(Guid = "219AF347-49C8-4CED-A689-6E6ED0E49710")]
    public readonly Slot<int> Result = new();

    public GetAPrime()
    {
        Result.UpdateAction += Update;
    }

        
    private void Update(EvaluationContext context)
    {
        var index = Index.GetValue(context);
        if (index == _lastIndex)
            return;
            
        _lastIndex = index;
        Result.Value = ComputePrime(index);
    }

    private static int ComputePrime(int index)
    {
        if (index < 1)
            return -1;

        int count = 0;
        int n = 2;

        while (true)
        {
            if (count > 10000)
                return -1;

            bool isPrime = true;
            int limit = (int)Math.Sqrt(n); // Only check divisors up to sqrt(n)
            for (int i = 2; i <= limit; i++)
            {
                if (n % i == 0)
                {
                    isPrime = false;
                    break;
                }
            }

            if (isPrime)
            {
                count++;
                if (count == index)
                    return n;
            }

            // Increment to the next candidate number
            n = (n == 2) ? 3 : n + 2; // Skip even numbers after 2
        }
    }

        
    private int _lastIndex = -1;

    [Input(Guid = "f94d2c1c-35e4-4862-bbaa-1ed84fd276f6")]
    public readonly InputSlot<int> Index = new();
}