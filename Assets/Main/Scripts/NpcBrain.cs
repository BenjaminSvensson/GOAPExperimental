using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(NpcWorldObject))]
public class NpcBrain : MonoBehaviour
{
    private const string WanderPlanLabel = "Idle/Wander";

    [Serializable] private class Memory { public NpcWorldObject obj; public Vector3 lastPos; public bool visible; public float lastSeen; }
    private class GoalState { public float startTime; public float lastDoneTime = -999f; public bool completed; public bool failed; }
    private enum PlanAction { Move, Interact, Pickup, Socialize }
    private class PlanTask
    {
        public NpcGoalDefinition goal;
        public GoalState state;
        public NpcNeedType need;
        public NpcWorldObject target;
        public NpcBrain socialTarget;
        public PlanAction action;
        public Vector3 destination;
        public float score;
        public string label;
    }

    [Header("Perception")]
    [SerializeField] private Transform eyePoint;
    [SerializeField, Min(0.1f)] private float visionRange = 15f;
    [SerializeField, Range(1f, 360f)] private float visionAngle = 120f;
    [SerializeField] private bool useLineOfSight = true;
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;
    [SerializeField, Min(0.05f)] private float perceptionInterval = 0.25f;
    [SerializeField] private List<NpcWorldObject> initialKnownObjects = new List<NpcWorldObject>();

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2.4f;
    [SerializeField, Min(0.1f)] private float stoppingDistance = 1.25f;
    [SerializeField, Min(0.1f)] private float turnSpeed = 8f;
    [SerializeField, Min(0.1f)] private float interactionReachDistance = 1.75f;
    [SerializeField, Min(0f)] private float forgetMissingObjectAfterSeconds = 1.5f;
    [SerializeField] private bool alignToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 3f;
    [SerializeField, Min(0f)] private float groundProbeStartHeight = 1.1f;
    [SerializeField, Min(0f)] private float groundSnapOffset = 0.05f;
    [SerializeField, Min(0.1f)] private float maxVerticalSnapSpeed = 6f;
    [SerializeField, Min(0f)] private float groundedGraceTime = 0.2f;
    [SerializeField, Range(0f, 89f)] private float maxClimbSlope = 55f;
    [SerializeField, Range(0f, 89f)] private float maxGroundTiltSlope = 50f;
    [SerializeField, Min(0.1f)] private float groundAlignSpeed = 10f;
    [SerializeField] private bool enableUnstuck = true;
    [SerializeField, Min(0.1f)] private float stuckCheckInterval = 0.5f;
    [SerializeField, Min(0f)] private float stuckMinTravelDistance = 0.08f;
    [SerializeField, Min(1)] private int stuckChecksBeforeRecovery = 3;
    [SerializeField, Min(0.1f)] private float unstuckDuration = 0.7f;

    [Header("Needs")]
    [SerializeField] private NpcNeedSettings needSettings = new NpcNeedSettings();
    [SerializeField, Range(0f, 100f)] private float hunger = 10f;
    [SerializeField, Range(0f, 100f)] private float boredom = 10f;
    [SerializeField, Range(0f, 100f)] private float tiredness = 10f;

    [Header("Personality")]
    [SerializeField] private NpcPersonalityProfile personality = new NpcPersonalityProfile();

    [Header("Designer Goals")]
    [SerializeField] private List<NpcGoalDefinition> goals = new List<NpcGoalDefinition>();

    [Header("Social")]
    [SerializeField, Min(0f)] private float socialInteractionDuration = 2f;
    [SerializeField, Min(0f)] private float socialBoredomRelief = 30f;
    [SerializeField, Min(0f)] private float socialBoredomReliefForTarget = 20f;

    [Header("Idle")]
    [SerializeField] private bool wanderWhenIdle = true;
    [SerializeField, Min(0f)] private float wanderRadius = 8f;

    [Header("Planner")]
    [SerializeField, Min(0.05f)] private float replanningInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float destinationHorizontalTolerance = 1.5f;
    [SerializeField, Min(1)] private int maxUnreachableAttempts = 3;
    [SerializeField, Min(0f)] private float unreachableCooldownSeconds = 2f;
    [SerializeField] private bool verboseDebug;

    [Header("Debug Output")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool debugVerboseLogs;
    [SerializeField, Min(1)] private int debugMaxRecentEvents = 20;
    [SerializeField, Min(0.2f)] private float debugStatusLogInterval = 3f;

    [Header("Debug Gizmos")]
    [SerializeField] private bool drawVisionGizmos = true;
    [SerializeField] private bool drawVisionCone = true;
    [SerializeField] private bool drawKnownObjectGizmos = true;
    [SerializeField] private bool drawCurrentTaskGizmo = true;
    [SerializeField] private bool drawNeedsGizmo = true;
    [SerializeField] private bool drawGroundingGizmos = true;
    [SerializeField] private bool drawLabelGizmos = true;
    [SerializeField, Min(0.05f)] private float memoryMarkerRadius = 0.2f;

    [Header("Runtime Debug")]
    [SerializeField] private string activePlan = "None";
    [SerializeField] private string activeAction = "Idle";
    [SerializeField] private string activeTarget = "None";
    [SerializeField] private float activeTaskScore;
    [SerializeField] private float activeTaskDistance;
    [SerializeField] private int visibleMemories;
    [SerializeField] private int totalMemories;
    [SerializeField] private bool isGrounded;
    [SerializeField] private float groundSlope;
    [SerializeField] private int stuckCounter;
    [SerializeField] private bool isRecoveringFromStuck;
    [SerializeField] private List<string> inventoryDebug = new List<string>();
    [SerializeField] private List<string> memoryDebug = new List<string>();
    [SerializeField] private List<string> recentDebugEvents = new List<string>();

    private readonly List<Memory> memories = new List<Memory>();
    private readonly List<NpcPickupItem> inventory = new List<NpcPickupItem>();
    private readonly Dictionary<NpcGoalDefinition, GoalState> goalStates = new Dictionary<NpcGoalDefinition, GoalState>();
    private readonly Dictionary<NpcWorldObject, int> unreachableAttempts = new Dictionary<NpcWorldObject, int>();
    private readonly Dictionary<NpcWorldObject, float> blockedTargetsUntil = new Dictionary<NpcWorldObject, float>();

    private PlanTask currentTask;
    private NpcInteractable pendingInteractable;
    private NpcBrain pendingSocialTarget;
    private bool performingAction;
    private float actionTimer;
    private float nextPerceptionTime;
    private float nextPlanTime;
    private float nextStatusLogTime;
    private float nextStuckCheckTime;
    private float unstuckUntilTime;
    private Vector3 unstuckDirection;
    private Vector3 lastStuckCheckPosition;
    private float lastGroundedTime;
    private Vector3 currentGroundNormal = Vector3.up;
    private NpcWorldObject selfWorldObject;

    public NpcWorldObject WorldObject => selfWorldObject;

    private void Awake()
    {
        if (eyePoint == null) eyePoint = transform;
        selfWorldObject = GetComponent<NpcWorldObject>();

        goalStates.Clear();
        for (int i = 0; i < goals.Count; i++)
        {
            NpcGoalDefinition goal = goals[i];
            if (goal == null || goalStates.ContainsKey(goal)) continue;
            goalStates.Add(goal, new GoalState { startTime = Time.time });
        }

        memories.Clear();
        for (int i = 0; i < initialKnownObjects.Count; i++)
        {
            NpcWorldObject worldObject = initialKnownObjects[i];
            if (worldObject == null || worldObject == selfWorldObject) continue;
            Memory m = GetOrCreateMemory(worldObject);
            m.lastPos = worldObject.transform.position;
            m.lastSeen = -1f;
            m.visible = false;
        }

        lastStuckCheckPosition = transform.position;
        DebugEvent($"Initialized with {goals.Count} goals and {memories.Count} seeded memories.");
        UpdateRuntimeDebugViews();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        hunger = Mathf.Clamp(hunger + needSettings.hungerIncreasePerSecond * dt, 0f, 100f);
        boredom = Mathf.Clamp(boredom + needSettings.boredomIncreasePerSecond * dt, 0f, 100f);
        tiredness = Mathf.Clamp(tiredness + needSettings.tirednessIncreasePerSecond * dt, 0f, 100f);

        UpdateGroundState(true, dt);
        TickGoalDeadlines();
        if (Time.time >= nextPerceptionTime) { ScanVision(); nextPerceptionTime = Time.time + perceptionInterval; }
        if (!performingAction && Time.time >= nextPlanTime) { Replan(); nextPlanTime = Time.time + replanningInterval; }
        ExecuteTask(dt);

        if (debugVerboseLogs && Time.time >= nextStatusLogTime)
        {
            nextStatusLogTime = Time.time + debugStatusLogInterval;
            DebugEvent($"Status H:{hunger:F0} B:{boredom:F0} T:{tiredness:F0} | Plan: {activePlan}", true);
        }

        UpdateRuntimeDebugViews();
    }

    private void TickGoalDeadlines()
    {
        foreach (KeyValuePair<NpcGoalDefinition, GoalState> pair in goalStates)
        {
            NpcGoalDefinition goal = pair.Key;
            GoalState state = pair.Value;
            if (goal == null || state == null || state.failed) continue;
            if (state.completed && !goal.repeatable) continue;
            if (!goal.hasDeadline || goal.deadlineSecondsFromStart <= 0f) continue;
            if (Time.time - state.startTime <= goal.deadlineSecondsFromStart) continue;
            if (!goal.failWhenDeadlineMissed) continue;
            state.failed = true;
            DebugEvent($"Deadline missed for goal '{SafeGoalName(goal)}'.");
            if (currentTask != null && currentTask.goal == goal) ClearTask();
        }
    }

    private void ScanVision()
    {
        for (int i = memories.Count - 1; i >= 0; i--)
        {
            Memory m = memories[i];
            if (m == null || m.obj == null) { memories.RemoveAt(i); continue; }
            m.visible = false;
        }

        IReadOnlyList<NpcWorldObject> objects = NpcWorldObject.RegisteredObjects;
        for (int i = 0; i < objects.Count; i++)
        {
            NpcWorldObject obj = objects[i];
            if (obj == null || obj == selfWorldObject || !obj.VisibleToNpc) continue;
            if (!CanSee(obj)) continue;

            bool knownBefore = FindMemory(obj) != null;
            Memory m = GetOrCreateMemory(obj);
            bool wasVisible = m.visible;
            m.visible = true;
            m.lastSeen = Time.time;
            Vector3 oldPosition = m.lastPos;
            m.lastPos = obj.transform.position;

            if (!knownBefore)
            {
                DebugEvent($"Discovered '{obj.name}' at {FormatVec(m.lastPos)}.");
            }
            else if (!wasVisible)
            {
                DebugEvent($"Saw '{obj.name}' again at {FormatVec(m.lastPos)}.", true);
            }
            else if ((oldPosition - m.lastPos).sqrMagnitude > 0.25f)
            {
                DebugEvent($"Updated position for '{obj.name}' -> {FormatVec(m.lastPos)}.", true);
            }
        }
    }

    private bool CanSee(NpcWorldObject obj)
    {
        Vector3 eye = eyePoint != null ? eyePoint.position : transform.position;
        Vector3 toTarget = obj.transform.position - eye;
        if (toTarget.sqrMagnitude > visionRange * visionRange) return false;
        if (visionAngle < 360f && Vector3.Angle(transform.forward, toTarget) > visionAngle * 0.5f) return false;
        if (!useLineOfSight) return true;

        Vector3 direction = toTarget.normalized;
        float distance = toTarget.magnitude;
        Vector3 origin = eye + direction * 0.05f;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore)) return true;
        return hit.transform.IsChildOf(obj.transform);
    }

    private void Replan()
    {
        PlanTask best = BuildBestTask();
        if (best == null) return;
        if (currentTask == null || !IsTaskValid(currentTask) || best.score > currentTask.score + 0.05f)
        {
            string oldPlan = currentTask != null ? currentTask.label : "None";
            currentTask = best;
            activePlan = best.label;
            activeAction = best.action.ToString();
            activeTarget = best.target != null ? best.target.name : "None";
            activeTaskScore = best.score;
            DebugEvent($"Plan -> {best.label} (score {best.score:F2}, prev {oldPlan}).");
            if (verboseDebug) Debug.Log($"{name} selected: {best.label} ({best.score:F2})", this);
        }
    }

    private PlanTask BuildBestTask()
    {
        PlanTask best = null;
        float bestScore = float.MinValue;

        Promote(EvaluateNeedTask(NpcNeedType.Hunger), ref best, ref bestScore);
        Promote(EvaluateNeedTask(NpcNeedType.Boredom), ref best, ref bestScore);
        Promote(EvaluateNeedTask(NpcNeedType.Tiredness), ref best, ref bestScore);

        for (int i = 0; i < goals.Count; i++)
        {
            NpcGoalDefinition goal = goals[i];
            if (goal == null || !goalStates.TryGetValue(goal, out GoalState state) || state.failed) continue;
            if (state.completed && !goal.repeatable) continue;
            if (goal.repeatable && Time.time - state.lastDoneTime < goal.repeatCooldownSeconds) continue;

            float deadlineBonus = DeadlineUrgency(goal, state, out bool missed);
            if (missed && goal.failWhenDeadlineMissed) { state.failed = true; continue; }

            PlanTask task = BuildGoalTask(goal, state);
            if (task == null) continue;
            task.goal = goal;
            task.state = state;
            task.score += Mathf.Clamp01(goal.basePriority / 100f) * personality.explicitGoalDrive;
            task.score += deadlineBonus * personality.deadlineStress;
            Promote(task, ref best, ref bestScore);
        }

        if (best == null && wanderWhenIdle && wanderRadius > 0f)
        {
            Vector2 random = UnityEngine.Random.insideUnitCircle * wanderRadius;
            best = new PlanTask
            {
                action = PlanAction.Move,
                destination = transform.position + new Vector3(random.x, 0f, random.y),
                score = 0.01f,
                label = "Idle/Wander"
            };
        }

        return best;
    }

    private void Promote(PlanTask candidate, ref PlanTask best, ref float bestScore)
    {
        if (candidate == null || candidate.score <= bestScore) return;
        best = candidate;
        bestScore = candidate.score;
    }

    private PlanTask EvaluateNeedTask(NpcNeedType need)
    {
        float value = GetNeedValue(need);
        if (value < needSettings.considerNeedAbove) return null;

        float urgency = Mathf.InverseLerp(needSettings.considerNeedAbove, 100f, value);
        float needWeight = personality.GetNeedPriority(need);
        PlanTask bestNeedTask = null;
        float bestNeedScore = float.MinValue;

        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null) continue;            NpcInteractable interactable = memory.obj.Interactable;
            if (interactable == null || !interactable.CanInteract(this)) continue;
            if (memory.visible && !interactable.IsAvailable) { Forget(memory.obj); continue; }

            float relief = interactable.GetNeedRelief(need);
            if (relief <= 0f) continue;

            float score = urgency * needWeight;
            score += Mathf.Clamp01(relief / 100f) * 0.8f;
            score += DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) * 0.5f;
            score += memory.obj.Desirability * 0.15f;
            if (memory.visible) score += 0.2f;
            if (score <= bestNeedScore) continue;

            bestNeedScore = score;
            bestNeedTask = new PlanTask 
            {
                need = need,
                action = PlanAction.Interact,
                target = memory.obj,
                destination = memory.lastPos,
                score = score,
                label = $"Need/{need} -> Interact {memory.obj.name}"
            };
        }

        if (need != NpcNeedType.Boredom || personality.sociability <= 0f) return bestNeedTask;
        PlanTask socialTask = EvaluateSocialTask(urgency, needWeight);
        if (socialTask == null) return bestNeedTask;
        return bestNeedTask == null || socialTask.score > bestNeedTask.score ? socialTask : bestNeedTask;
    }

    private PlanTask EvaluateSocialTask(float urgency, float needWeight)
    {
        PlanTask best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null || !memory.obj.TryGetComponent(out NpcBrain other) || other == this) continue;

            float score = urgency * needWeight * personality.sociability;
            score += DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) * 0.7f;
            if (memory.visible) score += 0.3f;
            if (score <= bestScore) continue;

            bestScore = score;
            best = new PlanTask
            {
                need = NpcNeedType.Boredom,
                action = PlanAction.Socialize,
                target = memory.obj,
                socialTarget = other,
                destination = memory.lastPos,
                score = score,
                label = $"Need/Boredom -> Socialize with {other.name}"
            };
        }

        return best;
    }

    private PlanTask BuildGoalTask(NpcGoalDefinition goal, GoalState state)
    {
        switch (goal.goalType)
        {
            case NpcGoalType.AcquireItem: return BuildAcquireTask(goal, state);
            case NpcGoalType.GoToObject: return BuildGoTask(goal, state);
            case NpcGoalType.InteractWithObject: return BuildInteractTask(goal);
            case NpcGoalType.Socialize: return BuildSocialGoalTask(goal);
            default: return null; 
        }
    }

    private PlanTask BuildAcquireTask(NpcGoalDefinition goal, GoalState state)
    {
        if (IsAcquireSatisfied(goal)) { MarkGoalComplete(goal, state); return null; }

        if (goal.specificTarget != null)
        {
            Memory memory = FindMemory(goal.specificTarget);
            NpcPickupItem targetPickup = goal.specificTarget.PickupItem;
            if (memory == null || targetPickup == null) return null;
            if (IsTargetTemporarilyBlocked(goal.specificTarget)) return null;
            if (targetPickup.IsPickedUp)
            {
                Forget(goal.specificTarget);
                return null;
            }

            return new PlanTask
            {
                action = PlanAction.Pickup,
                target = goal.specificTarget,
                destination = memory.lastPos,
                score = DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) + goal.specificTarget.Desirability * 0.2f + (memory.visible ? 0.2f : 0f),
                label = $"Goal/{goal.goalName} -> Pick up {goal.specificTarget.name}"
            };
        }

        if (string.IsNullOrWhiteSpace(goal.requiredItemType)) return null;

        PlanTask best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null) continue;            NpcPickupItem pickup = memory.obj.PickupItem;
            if (pickup == null || !EqualsIgnoreCase(pickup.ItemType, goal.requiredItemType)) continue;
            if (pickup.IsPickedUp) { Forget(memory.obj); continue; }

            float score = pickup.Usefulness * personality.itemUsefulnessBias + memory.obj.Desirability;
            score += DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) * (1f / personality.travelCostBias);
            if (memory.visible) score += 0.2f;
            if (score <= bestScore) continue;

            bestScore = score;
            best = new PlanTask
            {
                action = PlanAction.Pickup,
                target = memory.obj,
                destination = memory.lastPos,
                score = score,
                label = $"Goal/{goal.goalName} -> Pick up best {goal.requiredItemType}"
            };
        }

        return best;
    }

    private PlanTask BuildGoTask(NpcGoalDefinition goal, GoalState state)
    {
        if (goal.specificTarget == null) return null;
        Memory memory = FindMemory(goal.specificTarget);
        if (memory == null) return null;
        if (IsTargetTemporarilyBlocked(goal.specificTarget)) return null;

        float distance = Vector3.Distance(transform.position, memory.lastPos);
        if (distance <= stoppingDistance && memory.visible) { MarkGoalComplete(goal, state); return null; }

        return new PlanTask
        {
            action = PlanAction.Move,
            target = goal.specificTarget,
            destination = memory.lastPos,
            score = DistanceScore(distance) + goal.specificTarget.Desirability * 0.15f,
            label = $"Goal/{goal.goalName} -> Go to {goal.specificTarget.name}"
        };
    }

    private PlanTask BuildInteractTask(NpcGoalDefinition goal)
    {
        if (goal.specificTarget != null)
        {
            Memory memory = FindMemory(goal.specificTarget);
            NpcInteractable interactable = goal.specificTarget.Interactable;
            if (memory == null || interactable == null || !interactable.CanInteract(this)) return null;
            if (memory.visible && !interactable.IsAvailable) { Forget(goal.specificTarget); return null; }

            float score = DistanceScore(Vector3.Distance(transform.position, memory.lastPos));
            score += Mathf.Clamp01(interactable.GetNeedRelief(goal.preferredNeed) / 100f);

            return new PlanTask
            {
                action = PlanAction.Interact,
                target = goal.specificTarget,
                destination = memory.lastPos,
                score = score,
                label = $"Goal/{goal.goalName} -> Interact {goal.specificTarget.name}"
            };
        }

        PlanTask best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null) continue;            NpcInteractable interactable = memory.obj.Interactable;
            if (interactable == null || !interactable.CanInteract(this)) continue;
            if (memory.visible && !interactable.IsAvailable) { Forget(memory.obj); continue; }

            float relief = interactable.GetNeedRelief(goal.preferredNeed);
            if (relief <= 0f) continue;

            float score = Mathf.Clamp01(relief / 100f) + DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) + memory.obj.Desirability * 0.1f;
            if (score <= bestScore) continue;

            bestScore = score;
            best = new PlanTask
            {
                action = PlanAction.Interact,
                target = memory.obj,
                destination = memory.lastPos,
                score = score,
                label = $"Goal/{goal.goalName} -> Interact best for {goal.preferredNeed}"
            };
        }

        return best;
    }

    private PlanTask BuildSocialGoalTask(NpcGoalDefinition goal)
    {
        PlanTask best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null || !memory.obj.TryGetComponent(out NpcBrain other) || other == this) continue;
            if (goal.specificTarget != null && memory.obj != goal.specificTarget) continue;

            float score = DistanceScore(Vector3.Distance(transform.position, memory.lastPos)) + (memory.visible ? 0.3f : 0f);
            if (score <= bestScore) continue;

            bestScore = score;
            best = new PlanTask
            {
                action = PlanAction.Socialize,
                target = memory.obj,
                socialTarget = other,
                destination = memory.lastPos,
                score = score,
                label = $"Goal/{goal.goalName} -> Socialize with {other.name}"
            };
        }

        return best;
    }

    private void ExecuteTask(float dt)
    {
        if (performingAction)
        {
            actionTimer -= dt;
            activeAction = pendingInteractable != null ? "PerformingInteract" : (pendingSocialTarget != null ? "PerformingSocial" : "PerformingAction");
            if (actionTimer <= 0f) FinishAction();
            return;
        }

        if (currentTask == null)
        {
            activePlan = "None";
            activeAction = "Idle";
            activeTarget = "None";
            activeTaskScore = 0f;
            activeTaskDistance = 0f;
            return;
        }

        if (!IsTaskValid(currentTask))
        {
            DebugEvent($"Task invalidated: {currentTask.label}", true);
            HandleLostTarget(currentTask.target);
            ClearTask();
            return;
        }

        Vector3 destination = ResolveDestination(currentTask);
        activeAction = currentTask.action.ToString();
        activeTarget = currentTask.target != null ? currentTask.target.name : "None";
        activeTaskScore = currentTask.score;
        activeTaskDistance = HorizontalDistanceTo(destination);
        StepMove(destination, dt);
        UpdateStuckRecovery(destination);
        float reachDistance = Mathf.Max(GetActionReachDistance(currentTask.action), destinationHorizontalTolerance);

        if (HorizontalDistanceTo(destination) > reachDistance) return;

        switch (currentTask.action)
        {
            case PlanAction.Move:
                if (currentTask.target == null || IsVisible(currentTask.target))
                {
                    CompleteTask(currentTask);
                }
                else
                {
                    RegisterUnreachableAttempt(currentTask.target, "move target not visible at arrival");
                    HandleLostTarget(currentTask.target);
                    ClearTask();
                }

                break;
            case PlanAction.Pickup:
                TryDoPickup(currentTask);
                break;
            case PlanAction.Interact:
                TryStartInteract(currentTask);
                break;
            case PlanAction.Socialize:
                TryStartSocial(currentTask);
                break;
        }
    }

    private bool IsTaskValid(PlanTask task)
    {
        if (task == null) return false;
        if (task.goal != null && task.state != null)
        {
            if (task.state.failed) return false;
            if (task.state.completed && !task.goal.repeatable) return false;
        }

        if (task.target != null && FindMemory(task.target) == null) return false;
        if (task.target != null && IsTargetTemporarilyBlocked(task.target)) return false;
        return task.action != PlanAction.Socialize || task.socialTarget != null;
    }

    private Vector3 ResolveDestination(PlanTask task)
    {
        if (task.target == null) return task.destination;
        Memory memory = FindMemory(task.target);
        if (memory == null) return task.destination;

        task.destination = memory.visible ? task.target.transform.position : memory.lastPos;
        return task.destination;
    }

    private void StepMove(Vector3 destination, float dt)
    {
        Vector3 from = transform.position;
        Vector3 toTarget = destination - from;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            AlignToGroundRotation(dt, transform.forward);
            return;
        }

        Vector3 moveDirection = toTarget.normalized;
        if (enableUnstuck && Time.time < unstuckUntilTime)
        {
            moveDirection = unstuckDirection;
            isRecoveringFromStuck = true;
        }
        else
        {
            isRecoveringFromStuck = false;
        }

        if (alignToGround && isGrounded)
        {
            moveDirection = Vector3.ProjectOnPlane(moveDirection, currentGroundNormal).normalized;
            if (moveDirection.sqrMagnitude < 0.001f)
            {
                moveDirection = Vector3.ProjectOnPlane(transform.forward, currentGroundNormal).normalized;
            }
        }

        if (isGrounded && groundSlope > maxClimbSlope)
        {
            moveDirection = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;
        }

        if (moveDirection.sqrMagnitude < 0.0001f)
        {
            AlignToGroundRotation(dt, transform.forward);
            return;
        }

        transform.position += moveDirection * (moveSpeed * dt);
        UpdateGroundState(true, dt);
        AlignToGroundRotation(dt, moveDirection);
    }

    private float GetActionReachDistance(PlanAction action)
    {
        if (action == PlanAction.Interact || action == PlanAction.Pickup || action == PlanAction.Socialize)
        {
            return Mathf.Max(stoppingDistance, interactionReachDistance);
        }

        return stoppingDistance;
    }
    private bool TryGetGroundHit(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * groundProbeStartHeight;
        float rayLength = groundProbeStartHeight + groundProbeDistance;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayLength, groundMask, QueryTriggerInteraction.Ignore);
        hit = default;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.collider == null)
            {
                continue;
            }

            if (candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                hit = candidate;
            }
        }

        return bestDistance < float.MaxValue;
    }

    private void UpdateGroundState(bool snapToGround, float dt)
    {
        if (!TryGetGroundHit(out RaycastHit hit))
        {
            bool withinGrace = Time.time - lastGroundedTime <= groundedGraceTime;
            isGrounded = withinGrace;
            if (!withinGrace)
            {
                groundSlope = 0f;
                currentGroundNormal = Vector3.up;
            }

            return;
        }

        isGrounded = true;
        lastGroundedTime = Time.time;
        currentGroundNormal = hit.normal.normalized;
        groundSlope = Vector3.Angle(currentGroundNormal, Vector3.up);

        if (!snapToGround)
        {
            return;
        }

        float targetY = hit.point.y + groundSnapOffset;
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, Mathf.Max(0.01f, maxVerticalSnapSpeed) * dt);
        transform.position = pos;
    }

    private void AlignToGroundRotation(float dt, Vector3 forwardDirection)
    {
        bool allowTilt = alignToGround && isGrounded && groundSlope <= maxGroundTiltSlope;
        Vector3 up = allowTilt ? currentGroundNormal : Vector3.up;
        Vector3 forward = forwardDirection;

        if (alignToGround && isGrounded)
        {
            forward = Vector3.ProjectOnPlane(forwardDirection, up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, up);
            }
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion desired = Quaternion.LookRotation(forward.normalized, up);
        float speed = Mathf.Max(turnSpeed, groundAlignSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, speed * dt);
    }

    private void UpdateStuckRecovery(Vector3 destination)
    {
        if (!enableUnstuck || performingAction || currentTask == null)
        {
            return;
        }

        if (Time.time < unstuckUntilTime)
        {
            isRecoveringFromStuck = true;
            return;
        }

        isRecoveringFromStuck = false;

        if (Time.time < nextStuckCheckTime)
        {
            return;
        }

        nextStuckCheckTime = Time.time + stuckCheckInterval;

        float remaining = HorizontalDistanceTo(destination);
        float reach = Mathf.Max(GetActionReachDistance(currentTask.action), destinationHorizontalTolerance);
        if (remaining <= reach + 0.2f)
        {
            stuckCounter = 0;
            lastStuckCheckPosition = transform.position;
            return;
        }

        float moved = Vector3.Distance(transform.position, lastStuckCheckPosition);
        lastStuckCheckPosition = transform.position;

        if (moved >= stuckMinTravelDistance)
        {
            stuckCounter = 0;
            return;
        }

        stuckCounter++;
        DebugEvent($"Low movement detected ({moved:F2}m). Stuck {stuckCounter}/{stuckChecksBeforeRecovery}.", true);
        if (stuckCounter < stuckChecksBeforeRecovery)
        {
            return;
        }

        RegisterUnreachableAttempt(currentTask.target, "movement stuck");

        Vector3 toTarget = destination - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f)
        {
            toTarget = transform.forward;
        }

        Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
        if (side.sqrMagnitude < 0.001f)
        {
            side = transform.right;
        }

        float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        Vector3 recovery = (toTarget.normalized + side.normalized * sign).normalized;
        if (alignToGround && isGrounded)
        {
            recovery = Vector3.ProjectOnPlane(recovery, currentGroundNormal).normalized;
            if (recovery.sqrMagnitude < 0.001f)
            {
                recovery = side.normalized * sign;
            }
        }

        unstuckDirection = recovery;
        unstuckUntilTime = Time.time + unstuckDuration;
        stuckCounter = 0;
        isRecoveringFromStuck = true;
        DebugEvent($"Unstuck recovery triggered for {unstuckDuration:F1}s.");
    }

    private void TryDoPickup(PlanTask task)
    {
        if (task.target == null) { DebugEvent("Pickup failed: no target."); ClearTask(); return; }
        Memory memory = FindMemory(task.target);
        if (memory == null) { DebugEvent($"Pickup failed for '{task.target.name}': no memory."); RegisterUnreachableAttempt(task.target, "no memory for pickup"); ClearTask(); return; }

        float reach = GetActionReachDistance(PlanAction.Pickup);
        float distanceToKnownPosition = Vector3.Distance(transform.position, memory.lastPos);
        bool canPickupFromKnownPosition = distanceToKnownPosition <= reach;
        if (!memory.visible && !canPickupFromKnownPosition)
        {
            DebugEvent($"Pickup failed for '{task.target.name}': target not visible and out of reach ({distanceToKnownPosition:F2}m).");
            RegisterUnreachableAttempt(task.target, "pickup out of reach");
            HandleLostTarget(task.target);
            ClearTask();
            return;
        }

        if (!memory.visible && canPickupFromKnownPosition)
        {
            DebugEvent($"Trying pickup for '{task.target.name}' from memory at close range ({distanceToKnownPosition:F2}m).", true);
        }

        NpcPickupItem pickup = task.target.PickupItem;
        if (pickup == null)
        {
            DebugEvent($"Pickup failed for '{task.target.name}': no pickup component.");
            RegisterUnreachableAttempt(task.target, "missing pickup component");
            HandleLostTarget(task.target);
            ClearTask();
            return;
        }

        if (!pickup.TryPickUp(this))
        {
            if (pickup.IsPickedUp)
            {
                DebugEvent($"Pickup failed for '{task.target.name}': already picked up.");
                Forget(task.target);
            }
            else
            {
                DebugEvent($"Pickup failed at '{task.target.name}'.");
                RegisterUnreachableAttempt(task.target, "pickup interaction failed");
            }

            ClearTask();
            return;
        }

        DebugEvent($"Picked up '{pickup.ItemType}' from '{task.target.name}'.");
        CompleteTask(task);
    }

    private void TryStartInteract(PlanTask task)
    {
        if (task.target == null) { DebugEvent("Interact failed: no target."); ClearTask(); return; }
        Memory memory = FindMemory(task.target);
        NpcInteractable interactable = task.target.Interactable;

        if (memory == null)
        {
            DebugEvent($"Interact failed for '{task.target.name}': no memory.");
            RegisterUnreachableAttempt(task.target, "no memory for interact");
            ClearTask();
            return;
        }

        if (interactable == null)
        {
            DebugEvent($"Interact failed for '{task.target.name}': no interactable component.");
            RegisterUnreachableAttempt(task.target, "missing interactable component");
            HandleLostTarget(task.target);
            ClearTask();
            return;
        }

        if (!interactable.CanInteract(this))
        {
            DebugEvent($"Interact failed for '{task.target.name}': requirements not met.");
            ClearTask();
            return;
        }

        float reach = Mathf.Max(stoppingDistance, interactionReachDistance);
        float distanceToKnownPosition = Vector3.Distance(transform.position, memory.lastPos);
        bool canInteractFromKnownPosition = distanceToKnownPosition <= reach;

        if (!memory.visible && !canInteractFromKnownPosition)
        {
            DebugEvent($"Interact failed for '{task.target.name}': target not visible and out of reach ({distanceToKnownPosition:F2}m).");
            RegisterUnreachableAttempt(task.target, "interact out of reach");
            HandleLostTarget(task.target);
            ClearTask();
            return;
        }

        if (!memory.visible && canInteractFromKnownPosition)
        {
            DebugEvent($"Interacting with '{task.target.name}' from memory at close range ({distanceToKnownPosition:F2}m).", true);
        }

        pendingInteractable = interactable;
        pendingSocialTarget = null;
        performingAction = true;
        actionTimer = Mathf.Max(0.01f, interactable.InteractionDuration);
        activePlan = $"{task.label} (interacting)";
        DebugEvent($"Started interaction with '{task.target.name}' for {actionTimer:F1}s.");
    }

    private void TryStartSocial(PlanTask task)
    {
        if (task.socialTarget == null || task.target == null) { DebugEvent("Social failed: no target NPC."); ClearTask(); return; }
        Memory memory = FindMemory(task.target);
        if (memory == null || !memory.visible) { DebugEvent($"Social failed: '{task.target.name}' not visible."); RegisterUnreachableAttempt(task.target, "social target not visible"); HandleLostTarget(task.target); ClearTask(); return; }

        pendingInteractable = null;
        pendingSocialTarget = task.socialTarget;
        performingAction = true;
        actionTimer = Mathf.Max(0.01f, socialInteractionDuration);
        activePlan = $"{task.label} (socializing)";
        DebugEvent($"Started social interaction with '{task.socialTarget.name}' for {actionTimer:F1}s.");
    }

    private void FinishAction()
    {
        performingAction = false;
        bool success = false;

        if (pendingInteractable != null)
        {
            success = pendingInteractable.Apply(this);
            DebugEvent(success ? "Interaction effect applied." : "Interaction failed to apply.");
            pendingInteractable = null;
        }
        else if (pendingSocialTarget != null)
        {
            AdjustNeed(NpcNeedType.Boredom, -socialBoredomRelief);
            pendingSocialTarget.AdjustNeed(NpcNeedType.Boredom, -socialBoredomReliefForTarget);
            DebugEvent($"Socialized with '{pendingSocialTarget.name}'. Boredom reduced by {socialBoredomRelief:F0}.");
            pendingSocialTarget = null;
            success = true;
        }

        if (success && currentTask != null) CompleteTask(currentTask);
        else ClearTask();
    }

    private void CompleteTask(PlanTask task)
    {
        if (task != null) DebugEvent($"Task complete: {task.label}");
        if (task != null && task.goal != null && task.state != null) MarkGoalComplete(task.goal, task.state);
        ClearTask();
    }

    private void MarkGoalComplete(NpcGoalDefinition goal, GoalState state)
    {
        state.lastDoneTime = Time.time;
        if (goal.repeatable)
        {
            state.completed = false;
            state.failed = false;
            if (goal.hasDeadline) state.startTime = Time.time;
        }
        else
        {
            state.completed = true;
        }

        DebugEvent($"Goal complete: '{SafeGoalName(goal)}' (repeatable: {goal.repeatable}).");
    }

    private bool IsAcquireSatisfied(NpcGoalDefinition goal)
    {
        if (goal.specificTarget != null)
        {
            NpcPickupItem targetItem = goal.specificTarget.PickupItem;
            return targetItem != null && inventory.Contains(targetItem);
        }

        return !string.IsNullOrWhiteSpace(goal.requiredItemType) && HasItem(goal.requiredItemType);
    }

    private float DeadlineUrgency(NpcGoalDefinition goal, GoalState state, out bool missed)
    {
        missed = false;
        if (!goal.hasDeadline || goal.deadlineSecondsFromStart <= 0f) return 0f;

        float remaining = goal.deadlineSecondsFromStart - (Time.time - state.startTime);
        if (remaining <= 0f) { missed = true; return 1f; }
        return 1f - Mathf.Clamp01(remaining / goal.deadlineSecondsFromStart);
    }

    private float GetNeedValue(NpcNeedType need)
    {
        switch (need)
        {
            case NpcNeedType.Hunger: return hunger;
            case NpcNeedType.Boredom: return boredom;
            case NpcNeedType.Tiredness: return tiredness;
            default: return 0f;
        }
    }

    private float DistanceScore(float distance) => 1f / (1f + distance * 0.2f * personality.travelCostBias);

    private float HorizontalDistanceTo(Vector3 worldPosition)
    {
        Vector3 a = transform.position;
        a.y = 0f;
        Vector3 b = worldPosition;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool IsTargetTemporarilyBlocked(NpcWorldObject target)
    {
        if (target == null)
        {
            return false;
        }

        if (!blockedTargetsUntil.TryGetValue(target, out float untilTime))
        {
            return false;
        }

        if (Time.time >= untilTime)
        {
            blockedTargetsUntil.Remove(target);
            return false;
        }

        return true;
    }

    private void RegisterUnreachableAttempt(NpcWorldObject target, string reason)
    {
        if (target == null)
        {
            return;
        }

        int attempts = 0;
        unreachableAttempts.TryGetValue(target, out attempts);
        attempts++;
        unreachableAttempts[target] = attempts;

        DebugEvent($"Unreachable attempt {attempts}/{maxUnreachableAttempts} for '{target.name}' ({reason}).", true);
        if (attempts < maxUnreachableAttempts)
        {
            return;
        }

        unreachableAttempts[target] = 0;
        blockedTargetsUntil[target] = Time.time + Mathf.Max(0f, unreachableCooldownSeconds);
        DebugEvent($"Temporarily abandoning '{target.name}' for {unreachableCooldownSeconds:F1}s.");
    }

    private Memory FindMemory(NpcWorldObject worldObject)
    {
        if (worldObject == null) return null;
        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory != null && memory.obj == worldObject) return memory;
        }

        return null;
    }

    private Memory GetOrCreateMemory(NpcWorldObject worldObject)
    {
        Memory memory = FindMemory(worldObject);
        if (memory != null) return memory;

        memory = new Memory { obj = worldObject, lastPos = worldObject.transform.position, visible = false, lastSeen = -1f };
        memories.Add(memory);
        return memory;
    }

    private void Forget(NpcWorldObject worldObject)
    {
        for (int i = memories.Count - 1; i >= 0; i--)
        {
            Memory memory = memories[i];
            if (memory != null && memory.obj == worldObject)
            {
                memories.RemoveAt(i);
                DebugEvent($"Forgot location of '{worldObject.name}'.");
            }
        }
    }

    private bool IsVisible(NpcWorldObject worldObject)
    {
        Memory memory = FindMemory(worldObject);
        return memory != null && memory.visible;
    }

    private void HandleLostTarget(NpcWorldObject worldObject)
    {
        if (worldObject == null) return;
        Memory memory = FindMemory(worldObject);
        if (memory == null) return;

        if (Vector3.Distance(transform.position, memory.lastPos) <= stoppingDistance * 1.5f && !memory.visible)
        {
            float unseenFor = memory.lastSeen < 0f ? float.MaxValue : Time.time - memory.lastSeen;
            if (unseenFor < forgetMissingObjectAfterSeconds)
            {
                DebugEvent($"Holding memory for '{worldObject.name}' ({unseenFor:F1}s unseen).", true);
                return;
            }

            DebugEvent($"Target '{worldObject.name}' missing at expected position after {unseenFor:F1}s unseen.");
            Forget(worldObject);
        }
    }

    private bool EqualsIgnoreCase(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ClearTask()
    {
        string oldPlan = currentTask != null ? currentTask.label : activePlan;
        currentTask = null;
        pendingInteractable = null;
        pendingSocialTarget = null;
        performingAction = false;
        actionTimer = 0f;
        activePlan = "None";
        activeAction = "Idle";
        activeTarget = "None";
        activeTaskScore = 0f;
        activeTaskDistance = 0f;
        if (!string.IsNullOrWhiteSpace(oldPlan) && oldPlan != "None")
        {
            DebugEvent($"Cleared task: {oldPlan}", true);
        }
    }

    public void RegisterPickedUpItem(NpcPickupItem item)
    {
        if (item == null || inventory.Contains(item)) return;
        inventory.Add(item);
        DebugEvent($"Inventory + {item.ItemType}");
    }

    public bool HasItem(string itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType)) return inventory.Count > 0;

        for (int i = 0; i < inventory.Count; i++)
        {
            NpcPickupItem item = inventory[i];
            if (item != null && EqualsIgnoreCase(item.ItemType, itemType)) return true;
        }

        return false;
    }

    public bool ConsumeItem(string itemType)
    {
        if (inventory.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(itemType))
        {
            inventory.RemoveAt(0);
            DebugEvent("Consumed one inventory item.", true);
            return true;
        }

        for (int i = 0; i < inventory.Count; i++)
        {
            NpcPickupItem item = inventory[i];
            if (item != null && EqualsIgnoreCase(item.ItemType, itemType))
            {
                inventory.RemoveAt(i);
                DebugEvent($"Consumed item '{itemType}'.", true);
                return true;
            }
        }

        return false;
    }

    public void AdjustNeed(NpcNeedType need, float delta)
    {
        switch (need)
        {
            case NpcNeedType.Hunger: hunger = Mathf.Clamp(hunger + delta, 0f, 100f); break;
            case NpcNeedType.Boredom: boredom = Mathf.Clamp(boredom + delta, 0f, 100f); break;
            case NpcNeedType.Tiredness: tiredness = Mathf.Clamp(tiredness + delta, 0f, 100f); break;
        }

        DebugEvent($"Need {need} adjusted by {delta:+0.0;-0.0;0.0} -> {GetNeedValue(need):F1}", true);
    }

    private void UpdateRuntimeDebugViews()
    {
        totalMemories = memories.Count;
        visibleMemories = 0;

        inventoryDebug.Clear();
        for (int i = 0; i < inventory.Count; i++)
        {
            NpcPickupItem item = inventory[i];
            if (item == null) continue;
            inventoryDebug.Add(item.ItemType);
        }

        memoryDebug.Clear();
        for (int i = 0; i < memories.Count; i++)
        {
            Memory memory = memories[i];
            if (memory == null || memory.obj == null) continue;            if (memory.visible) visibleMemories++;
            float seenAgo = memory.lastSeen < 0f ? -1f : Time.time - memory.lastSeen;
            string seenText = memory.visible ? "Visible" : $"Memory ({seenAgo:F1}s ago)";
            memoryDebug.Add($"{memory.obj.name} [{seenText}] at {FormatVec(memory.lastPos)}");
        }

        if (currentTask != null)
        {
            activeTaskDistance = Vector3.Distance(transform.position, GetTaskDestinationForGizmo(currentTask));
        }
        else
        {
            activeTaskDistance = 0f;
        }
    }

    private void DebugEvent(string message, bool verboseOnly = false)
    {
        bool wanderActive = currentTask != null && currentTask.label == WanderPlanLabel;
        if (wanderActive || (!string.IsNullOrEmpty(message) && message.Contains(WanderPlanLabel)))
        {
            return;
        }

        if (verboseOnly && !debugVerboseLogs) return;

        string stamped = $"{Time.time:F1}s {message}";
        recentDebugEvents.Insert(0, stamped);
        if (recentDebugEvents.Count > debugMaxRecentEvents)
        {
            recentDebugEvents.RemoveAt(recentDebugEvents.Count - 1);
        }

        if (!debugLogs) return;
        if (verboseOnly && !debugVerboseLogs) return;
        Debug.Log($"[{name}] {message}", this);
    }

    private string SafeGoalName(NpcGoalDefinition goal)
    {
        if (goal == null) return "<null>";
        if (!string.IsNullOrWhiteSpace(goal.goalName)) return goal.goalName;
        return goal.goalType.ToString();
    }

    private string FormatVec(Vector3 v)
    {
        return $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
    }

    private Color TaskColor(PlanAction action)
    {
        switch (action)
        {
            case PlanAction.Move: return new Color(0.3f, 0.7f, 1f, 0.95f);
            case PlanAction.Interact: return new Color(0.2f, 1f, 0.45f, 0.95f);
            case PlanAction.Pickup: return new Color(1f, 0.85f, 0.2f, 0.95f);
            case PlanAction.Socialize: return new Color(1f, 0.4f, 0.8f, 0.95f);
            default: return Color.white;
        }
    }

    private Vector3 GetTaskDestinationForGizmo(PlanTask task)
    {
        if (task == null) return transform.position;
        if (task.target == null) return task.destination;

        Memory memory = FindMemory(task.target);
        if (memory == null) return task.destination;
        return memory.visible ? task.target.transform.position : memory.lastPos;
    }

    private void DrawNeedBar(Vector3 center, float value01, Color color)
    {
        float width = 1.1f;
        Vector3 left = center - transform.right * (width * 0.5f);
        Vector3 right = center + transform.right * (width * 0.5f);

        Gizmos.color = new Color(0f, 0f, 0f, 0.9f);
        Gizmos.DrawLine(left, right);

        Gizmos.color = color;
        Gizmos.DrawLine(left, Vector3.Lerp(left, right, Mathf.Clamp01(value01)));
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = eyePoint != null ? eyePoint.position : transform.position;
        Vector3 labelBase = transform.position + Vector3.up * 2.3f;

        if (drawVisionGizmos)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.65f);
            Gizmos.DrawWireSphere(eye, visionRange);

            if (drawVisionCone && visionAngle < 360f)
            {
                Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (forwardFlat.sqrMagnitude < 0.001f) forwardFlat = Vector3.forward;
                forwardFlat.Normalize();

                float half = visionAngle * 0.5f;
                int segments = 18;
                Vector3 prev = eye + Quaternion.AngleAxis(-half, Vector3.up) * forwardFlat * visionRange;
                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float angle = Mathf.Lerp(-half, half, t);
                    Vector3 next = eye + Quaternion.AngleAxis(angle, Vector3.up) * forwardFlat * visionRange;
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }

                Vector3 left = eye + Quaternion.AngleAxis(-half, Vector3.up) * forwardFlat * visionRange;
                Vector3 right = eye + Quaternion.AngleAxis(half, Vector3.up) * forwardFlat * visionRange;
                Gizmos.DrawLine(eye, left);
                Gizmos.DrawLine(eye, right);
            }
        }

        if (drawKnownObjectGizmos)
        {
            for (int i = 0; i < memories.Count; i++)
            {
                Memory memory = memories[i];
                if (memory == null || memory.obj == null) continue;
                Gizmos.color = memory.visible ? new Color(0.1f, 1f, 0.2f, 0.9f) : new Color(1f, 0.7f, 0.1f, 0.9f);
                Gizmos.DrawWireSphere(memory.lastPos + Vector3.up * 0.05f, memoryMarkerRadius);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, memory.lastPos + Vector3.up * 0.05f);
            }
        }

        if (drawCurrentTaskGizmo && currentTask != null)
        {
            Vector3 destination = GetTaskDestinationForGizmo(currentTask);
            Gizmos.color = TaskColor(currentTask.action);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, destination + Vector3.up * 0.2f);
            Gizmos.DrawSphere(destination + Vector3.up * 0.1f, memoryMarkerRadius * 0.75f);
        }

        if (drawGroundingGizmos)
        {
            Vector3 origin = transform.position + Vector3.up * 0.15f;
            Gizmos.color = isGrounded ? new Color(0.15f, 1f, 0.45f, 0.9f) : new Color(1f, 0.25f, 0.25f, 0.9f);
            Gizmos.DrawLine(origin, origin + currentGroundNormal * 1.1f);

            if (isRecoveringFromStuck)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.9f, 0.95f);
                Gizmos.DrawLine(origin, origin + unstuckDirection.normalized * 1.3f);
            }
        }

        if (drawNeedsGizmo)
        {
            DrawNeedBar(labelBase + Vector3.up * 0.20f, hunger / 100f, new Color(1f, 0.25f, 0.25f, 1f));
            DrawNeedBar(labelBase + Vector3.up * 0.08f, boredom / 100f, new Color(1f, 0.7f, 0.2f, 1f));
            DrawNeedBar(labelBase - Vector3.up * 0.04f, tiredness / 100f, new Color(0.3f, 0.55f, 1f, 1f));
        }

#if UNITY_EDITOR
        if (drawLabelGizmos)
        {
            Handles.color = Color.white;
            Handles.Label(labelBase + Vector3.up * 0.30f, $"{name}\n{activeAction}\n{activePlan}\nH:{hunger:F0} B:{boredom:F0} T:{tiredness:F0}\nGrounded:{isGrounded} Slope:{groundSlope:F1} Stuck:{stuckCounter}");

            if (drawKnownObjectGizmos)
            {
                for (int i = 0; i < memories.Count; i++)
                {
                    Memory memory = memories[i];
                    if (memory == null || memory.obj == null) continue;
                    float seenAgo = memory.lastSeen < 0f ? -1f : Time.time - memory.lastSeen;
                    string seenText = memory.visible ? "visible" : $"{seenAgo:F1}s ago";
                    Handles.Label(memory.lastPos + Vector3.up * 0.35f, $"{memory.obj.name}\n{seenText}");
                }
            }
        }
#endif
    }
}



















