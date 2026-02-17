using System;
using UnityEngine;

public enum NpcGoalType
{
    AcquireItem,
    GoToObject,
    InteractWithObject,
    Socialize
}

[Serializable]
public class NpcGoalDefinition
{
    public string goalName = "New Goal";
    public NpcGoalType goalType = NpcGoalType.AcquireItem;

    [Range(0f, 100f)] public float basePriority = 50f;
    public bool repeatable;
    [Min(0f)] public float repeatCooldownSeconds = 2f;

    [Header("Deadline")]
    public bool hasDeadline;
    [Min(0f)] public float deadlineSecondsFromStart = 30f;
    public bool failWhenDeadlineMissed = true;

    [Header("Target")]
    public NpcWorldObject specificTarget;
    [Tooltip("Used for item goals when no specific target is set.")]
    public string requiredItemType = string.Empty;
    public NpcNeedType preferredNeed = NpcNeedType.Hunger;
}
