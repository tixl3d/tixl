using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Operators.Types.Id_f70c9e89_16f8_4d8d_b1a5_a5db6cf5d709
{
    public class PlayImageSequence : Instance<PlayImageSequence>
    {

        [Input(Guid = "73ca7259-128f-46a3-9fe3-669957add8da")]
        public readonly InputSlot<string> FolderPath = new InputSlot<string>();

        [Input(Guid = "bc5f36cc-2090-41ba-aafb-ae0a98e42dad")]
        public readonly InputSlot<string> FilenameFilter = new InputSlot<string>();

        [Input(Guid = "570691e0-425a-4a61-a824-f72917c71c14")]
        public readonly InputSlot<int> MinFrameNumber = new InputSlot<int>();

        [Input(Guid = "46337a40-cd2e-4ce8-8176-c07d9ba4b822")]
        public readonly InputSlot<int> MaxFrameNumber = new InputSlot<int>();

        [Input(Guid = "5fb8b5d3-05c5-4e0a-a89a-6ad53641da67")]
        public readonly InputSlot<bool> LoopPlayback = new InputSlot<bool>();

        [Input(Guid = "ea058707-1906-4c75-92cc-56d147496b07")]
        public readonly InputSlot<float> FrameOffset = new InputSlot<float>();

        [Input(Guid = "b5161355-66d4-478e-bcf0-1876c5b1765a")]
        public readonly InputSlot<float> Framerate = new InputSlot<float>();

        [Output(Guid = "94262a4f-0b0c-420b-aed2-4200f5956942")]
        public readonly Slot<SharpDX.Direct3D11.Texture2D> output = new Slot<SharpDX.Direct3D11.Texture2D>();

        [Output(Guid = "69ab3d65-aed5-437e-87d1-254490d34d0e")]
        public readonly Slot<System.Collections.Generic.List<string>> FileList = new Slot<System.Collections.Generic.List<string>>();

    }
}

