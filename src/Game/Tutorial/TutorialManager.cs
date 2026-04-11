using System;
using System.Collections.Generic;

namespace CorditeWars.Game.Tutorial;

public enum TriggerCondition
{
    Immediate,
    UnitSelected,
    BuildingPlaced,
    CorditeAbove,
    TimerSeconds
}

public sealed class TutorialStep
{
    public string           Id               { get; set; } = string.Empty;
    public string           Title            { get; set; } = string.Empty;
    public string           Body             { get; set; } = string.Empty;
    public TriggerCondition TriggerCondition { get; set; }
    public float            TriggerValue     { get; set; }
    public string           HighlightTarget  { get; set; } = string.Empty;
}

public sealed class TutorialManager
{
    private readonly List<TutorialStep> _steps = new();
    private int   _currentIndex = -1;
    private float _elapsedSeconds;
    private bool  _buildingPlaced;
    private bool  _unitSelected;
    private int   _cordite;

    public event Action<TutorialStep>? StepChanged;
    public event Action?               TutorialEnded;

    public bool          IsActive    { get; private set; }
    public TutorialStep? CurrentStep => _currentIndex >= 0 && _currentIndex < _steps.Count
        ? _steps[_currentIndex] : null;

    public void Start(List<TutorialStep> steps)
    {
        _steps.Clear();
        _steps.AddRange(steps);
        _currentIndex = -1;
        IsActive = true;
        AdvanceStep();
    }

    public void NotifyUnitSelected()      { _unitSelected   = true; }
    public void NotifyBuildingPlaced()    { _buildingPlaced = true; }
    public void NotifyCordite(int amount) { _cordite = amount; }

    public void AdvanceStep()
    {
        _currentIndex++;
        _elapsedSeconds = 0;
        _buildingPlaced = false;
        _unitSelected   = false;

        if (_currentIndex >= _steps.Count)
        {
            IsActive = false;
            TutorialEnded?.Invoke();
            return;
        }

        var step = _steps[_currentIndex];
        StepChanged?.Invoke(step);

        if (step.TriggerCondition == TriggerCondition.Immediate)
            AdvanceStep();
    }

    public void SkipTutorial()
    {
        IsActive = false;
        TutorialEnded?.Invoke();
    }

    public void Tick(float deltaSeconds)
    {
        if (!IsActive || CurrentStep is null) return;

        _elapsedSeconds += deltaSeconds;
        var step = CurrentStep;

        bool triggered = step.TriggerCondition switch
        {
            TriggerCondition.TimerSeconds   => _elapsedSeconds >= step.TriggerValue,
            TriggerCondition.UnitSelected   => _unitSelected,
            TriggerCondition.BuildingPlaced => _buildingPlaced,
            TriggerCondition.CorditeAbove   => _cordite >= (int)step.TriggerValue,
            _                               => false
        };

        if (triggered)
            AdvanceStep();
    }
}
