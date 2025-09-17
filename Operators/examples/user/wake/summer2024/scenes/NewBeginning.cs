namespace Examples.user.wake.summer2024.scenes{
    [Guid("6dda24f8-6e7b-4edf-bbb6-3a9e8ac3e26b")]
    internal sealed class NewBeginning : Instance<NewBeginning>
    {

        [Output(Guid = "0d520353-827c-4724-bae6-6715ea2475e5")]
        public readonly Slot<T3.Core.DataTypes.Command> Output = new Slot<T3.Core.DataTypes.Command>();


        [Input(Guid = "d2de1619-5d45-4552-bbfe-539593846e75")]
        public readonly InputSlot<bool> BeatTrigger = new InputSlot<bool>();

        [Input(Guid = "cd427437-6e48-48ff-939c-71d36072738f")]
        public readonly InputSlot<bool> ClapTrigger = new InputSlot<bool>();

        [Input(Guid = "4c7d125a-abf9-4bab-9e65-3be69a1782a0")]
        public readonly InputSlot<bool> Bass1Trigger = new InputSlot<bool>();

        [Input(Guid = "76aa7b67-48cc-4370-b258-9190749c5667")]
        public readonly InputSlot<bool> ArpSynth1Trigger = new InputSlot<bool>();

        [Input(Guid = "c8ad2bdd-4065-4ba1-a76c-f435685935f6")]
        public readonly InputSlot<bool> Trigger5 = new InputSlot<bool>();

        [Input(Guid = "932ffb6a-3d85-4ddc-b4fa-5e0b05fbf677")]
        public readonly InputSlot<bool> AcidScreechTrigger = new InputSlot<bool>();

        [Input(Guid = "b92378da-8bb5-4948-9ff7-06ee404248cf")]
        public readonly InputSlot<bool> LenaVoiceTrigger = new InputSlot<bool>();

    }
}

