using System;
using UnityEngine;

public enum NpcNeedType
{
    Hunger,
    Boredom,
    Tiredness
}

[Serializable]
public class NpcNeedSettings
{
    [Header("Need Growth")]
    [Min(0f)] public float hungerIncreasePerSecond = 2f;
    [Min(0f)] public float boredomIncreasePerSecond = 1.2f;
    [Min(0f)] public float tirednessIncreasePerSecond = 1f;

    [Header("Planner Thresholds")]
    [Range(0f, 100f)] public float considerNeedAbove = 25f;
    [Range(0f, 100f)] public float urgentNeedAbove = 75f;
}

[Serializable]
public class NpcPersonalityProfile
{
    [Header("Need Priorities")]
    [Range(0.2f, 3f)] public float hungerPriority = 1.4f;
    [Range(0.2f, 3f)] public float boredomPriority = 1f;
    [Range(0.2f, 3f)] public float tirednessPriority = 1.15f;

    [Header("Goal Pressure")]
    [Range(0.2f, 3f)] public float explicitGoalDrive = 1f;
    [Range(0f, 3f)] public float deadlineStress = 1f;
    [Range(0f, 3f)] public float sociability = 1f;

    [Header("Choice Biases")]
    [Range(0.1f, 3f)] public float itemUsefulnessBias = 1f;
    [Range(0.1f, 3f)] public float travelCostBias = 1f;

    public float GetNeedPriority(NpcNeedType needType)
    {
        switch (needType)
        {
            case NpcNeedType.Hunger:
                return hungerPriority;
            case NpcNeedType.Boredom:
                return boredomPriority;
            case NpcNeedType.Tiredness:
                return tirednessPriority;
            default:
                return 1f;
        }
    }
}
