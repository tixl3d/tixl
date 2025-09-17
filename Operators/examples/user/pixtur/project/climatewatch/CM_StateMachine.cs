using T3.Core.Animation;
using T3.Core.Utils;

namespace Examples.user.pixtur.project.climatewatch;

[Guid("b0453fd5-e9c5-481a-aa6b-0040bd5c1318")]
public class CM_StateMachine : Instance<CM_StateMachine>
{
    [Output(Guid = "78a7a222-ef84-45cb-9732-eb29afc83c3d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SimulationProgress = new();

    [Output(Guid = "BBE120E4-DD68-4301-A3F2-3C1CA7D345E5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> RestCarbon = new();

    [Output(Guid = "8160A329-0A1A-4FDF-8E9D-D5E21CA95954", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> RestCarbonRatio = new();

    [Output(Guid = "A72A83F1-B36B-4653-8D1F-C0EBA3D03A0A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsSimulationRunning = new();

    [Output(Guid = "BE19AE67-EDB8-402C-A218-C327AF886D81", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Temperature = new();

    [Output(Guid = "08C995C9-B091-476C-8985-1C7EB64F451D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<DateTime> Date = new();

    [Output(Guid = "B2865766-2D84-4753-8E03-AF044051FCC2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SourceOil = new();

    [Output(Guid = "FB7618D7-A738-452C-A10E-EF7968866250", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SourceGas = new();

    [Output(Guid = "70682E31-6F51-4C08-8D96-BED403FC1C7D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SourceCoal = new();

    [Output(Guid = "01C2E74E-E665-4F01-84E6-10F41A74AD6A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SourceCoalMines = new();

    [Output(Guid = "FDBFE711-067B-4D95-83E6-A9125704069F", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> FreezeParticleGrowth = new();

    [Output(Guid = "64EDD71F-6D86-4669-9481-3D197B505022", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> ResetParticles = new();

        
    public CM_StateMachine()
    {
        SimulationProgress.UpdateAction += Update;
        RestCarbon.UpdateAction += Update;
    }

    private static double RunTime => Playback.RunTimeInSecs;
        
    private void Update(EvaluationContext context)
    {
        _renewPower = RenewPower.GetValue(context);
        _renewHeating = RenewHeating.GetValue(context);
        _renewMobility = RenewMobility.GetValue(context);
        _simulationSpeed = SimulationSpeed.GetValue(context);
        var startPressed = TriggerSimulation.GetValue(context);

        switch (_state)
        {
            case States.Idle:
            case States.ShowConfiguration:
                SourceOil.Value = SelectedSimulationMode.OilConsumption;
                SourceGas.Value = SelectedSimulationMode.GasConsumption;
                SourceCoalMines.Value = SelectedSimulationMode.CoalConsumption;
                SourceCoal.Value = SelectedSimulationMode.CoalConsumption;
                break;

            case States.Simulating:
            {
                // SourceOil.Value = 0;
                // SourceGas.Value = 0;
                // SourceCoalMines.Value = 0;
                // SourceCoal.Value = 0;

                var complete = Progress >= 1;
                if (complete)
                {
                    _lastInteractionTime = RunTime;
                    SetState(States.SimulationComplete);
                }

                //SimulationProgress.Value = (float)Progress;
                break;
            }

            case States.SimulationComplete:
            {
                // Update result animation?
                // Eventually switch to idle mode
                break;
            }
        }

        if (_state != States.SimulationComplete)
        {
            RestCarbon.Value = GetRestCarbon();
            Temperature.Value = GetTemperature();
            RestCarbonRatio.Value = GetRestCarbonRatio();
            Date.Value = GetDate();
            SimulationProgress.Value = (float)Progress;
        }

        FreezeParticleGrowth.Value = _state == States.SimulationComplete;
            
        if (startPressed != _startPressed)
        {
            if (startPressed)
            {
                SetState(States.Simulating);
            }

            _startPressed = startPressed;
        }

        ResetParticles.Value = false;
        var newSimMode = GetModeFromRenewalSetting();
        if (newSimMode != _simulationModeIndex)
        {
            if(_state != States.ShowConfiguration)
                ResetParticles.Value = true;
                
            SetState(States.ShowConfiguration);
            _simulationModeIndex = newSimMode;
        }

        if (_state != States.Simulating && _state != States.Idle)
        {
            if (RunTime - _lastInteractionTime > IdleTimeOut)
                SetState(States.Idle);
        }

        IsSimulationRunning.Value = _state == States.Simulating;
    }

    private void SetState(States newState)
    {
        // if (newState == _state)
        //     return;

        Log.Debug($"Switch {_state} -> {newState}", this);

        switch (newState)
        {
            case States.Idle:
                break;

            case States.Simulating:
                _simulationStartTime = RunTime;
                _lastInteractionTime = RunTime;
                break;

            case States.ShowConfiguration:
                _lastInteractionTime = RunTime;
                SimulationProgress.Value = 0;
                break;

            case States.SimulationComplete:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
        }

        _state = newState;
    }

    private int GetModeFromRenewalSetting()
    {
        return (_renewPower ? (1 << 0) : 0)
               + (_renewHeating ? (1 << 1) : 0)
               + (_renewMobility ? (1 << 2) : 0);
    }

    private string GetRestCarbon()
    {
        long rest = (long)MathUtils.Lerp(InitialRestCarbon, ActiveMode.RestCarbon, Progress);
        return $"{rest:0}";
    }

    private float GetRestCarbonRatio()
    {
        long rest = (long)MathUtils.Lerp(InitialRestCarbon, ActiveMode.RestCarbon, Progress);
        return (float)(rest / (double)InitialRestCarbon);
    }

    private float GetTemperature()
    {
        return (float)MathUtils.Lerp(InitialTemp, ActiveMode.EndTemperature, Progress);
    }

    private DateTime GetDate()
    {
        var timeA = _initialDate;
        var timeB = ActiveMode.EndDate;
        var duration = timeB - timeA;
        return timeA + TimeSpan.FromHours(duration.TotalHours * Progress);
    }

    private double Progress
    {
        get
        {
            switch (_state)
            {
                case States.Simulating:
                    return (RunTime - _simulationStartTime) / SelectedSimulationMode.GetSimulationDuration() * _simulationSpeed;
                case States.SimulationComplete:
                    return 1;
            }

            var worstMode = _simulationModes[0];
            var waitDuration = DateTime.Now - _initialDate;
            var endDuration = worstMode.EndDate - _initialDate;
            return (waitDuration.TotalDays / endDuration.TotalDays);
        }
    }

    private SimulationMode SelectedSimulationMode => _simulationModes[_simulationModeIndex];

    private const double IdleTimeOut = 3 * 60;
    private const long InitialRestCarbon = 7430000000000;

    private readonly DateTime _initialDate = new(2021, 5, 12);
    private const float InitialTemp = 1.21f;

    private SimulationMode DefaultMode => _simulationModes[0];

    private SimulationMode ActiveMode => _state == States.Simulating || _state == States.SimulationComplete
                                             ? SelectedSimulationMode
                                             : DefaultMode;

    private States _state = States.Idle;
    private double _lastInteractionTime;
    private double _simulationStartTime;
    private bool _startPressed;
    private bool _renewPower;
    private bool _renewHeating;
    private bool _renewMobility;

    private int _simulationModeIndex;

    enum States
    {
        Idle,
        ShowConfiguration,
        Simulating,
        SimulationComplete,
    }

    private struct SimulationMode
    {
        public SimulationMode(int year, float endTemperature, long restCarbon, float oil, float gas, float coal)
        {
            Year = year;
            EndTemperature = endTemperature;
            RestCarbon = restCarbon;
            EndDate = new DateTime(year, 12, 30);
            OilConsumption = oil;
            GasConsumption = gas;
            CoalConsumption = coal;
        }

        public readonly int Year;
        public float EndTemperature;
        public readonly long RestCarbon;
        public DateTime EndDate;
        public float OilConsumption;
        public float GasConsumption;
        public float CoalConsumption;

        public float GetSimulationDuration()
        {
            return (Year - 2021);
        }
    }

    private List<SimulationMode> _simulationModes = new()
                                                        {
                                                            new SimulationMode(2125, 5f, 3381000000000, 1, 1, 1),
                                                            new SimulationMode(2268, 5f, 3381000000000, 1, 0.6f, 0.4f),
                                                            new SimulationMode(2251, 5f, 3381000000000, 0.8f, 0.7f, 0.5f),
                                                            new SimulationMode(2140, 2.9f, 5517000000000, 0.8f, 0.3f, 0),
                                                            new SimulationMode(2190, 5f, 3381000000000, 0.1f, 0.9f, 1),
                                                            new SimulationMode(2317, 4.1f, 4451000000000, 0.1f, 0.5f, 0.4f),
                                                            new SimulationMode(2317, 4.3f, 4102000000000, 0, 0.6f, 0.5f),
                                                            new SimulationMode(2036, 1.72f, 6887000000000, 0, 0, 0),
                                                        };

    [Input(Guid = "c766e021-8478-4507-859d-25badb679ff2")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "3477E8BA-CB57-4007-85A5-FD1EFE1B578C")]
    public readonly InputSlot<bool> RenewPower = new();

    [Input(Guid = "286AB912-8D04-466B-B5C8-4D673F7F97E1")]
    public readonly InputSlot<bool> RenewHeating = new();

    [Input(Guid = "B212B24D-E6F2-481F-9DC6-42CBB4D9ADF6")]
    public readonly InputSlot<bool> RenewMobility = new();

    [Input(Guid = "03C4CCC6-87AA-4F5B-B321-BE5A1CD6A3E8")]
    public readonly InputSlot<bool> TriggerSimulation = new();

    [Input(Guid = "C82893BB-5106-4280-A2C0-03CAFC5112A1")]
    public readonly InputSlot<float> SimulationSpeed = new();

    private float _simulationSpeed;
}