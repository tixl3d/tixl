using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Linq;
using T3.Core.Utils;

namespace T3.Operators.Types.Id_4b943f29_3e59_4b2c_a267_a8221525966c
{
    public class RoundValues : Instance<RoundValues>
    {
        [Output(Guid = "9e3535dc-49ca-44ce-8b86-9770077289e0")]
        public readonly Slot<List<float>> Output = new();

        public RoundValues()
        {
            Output.UpdateAction = Update;
        }


        enum RoundType
        {
            Round = 0,
            Floor = 1,
            Ceiling = 2 
        }

        private void Update(EvaluationContext context)
        {
            //Log.Debug(" global time " + EvaluationContext.BeatTime);
            var list = Input.GetValue(context);
            if (list == null || list.Count == 0)
            {
                return;
            }
            
            if(_output.Count != list.Count)
            {
                _output = new List<float>(list.Count);
                _output.AddRange(Enumerable.Repeat(0f, list.Count));
            }
            

            _output.Clear();
            int typeSelection = Mode.GetValue(context);
            switch (typeSelection)
            {
                case 0:
                    foreach (var value in list)
                    {
                        _output.Add((float)System.Math.Round(value));
                    }
                    break;
                case 1:
                    foreach (var value in list)
                    {
                        _output.Add((float)System.Math.Floor(value));
                    }
                    break;
                case 2:
                    foreach (var value in list)
                    {
                        _output.Add((float)System.Math.Ceiling(value));
                    }
                    break;
            }
     
            

            Output.Value = _output;
        }
        
       // private float[] _averagedValues = new float[0];
        private float[] _lastValues = new float[0];
        private List<float> _output = new();

        [Input(Guid = "854b35c6-696a-4706-bdf3-785f7f47b4e8")]
        public readonly InputSlot<List<float>> Input = new(new List<float>(20));
        
        [Input(Guid = "E89BB94E-D12E-45F2-9158-497EA3071456", MappedType = typeof(RoundType))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();
        
        
        
    }
}