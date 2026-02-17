using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class NpcInteractable : MonoBehaviour
{
    [SerializeField, Min(0f)] private float interactionDuration = 1.5f;

    [Header("Need Deltas (negative values satisfy need)")]
    [SerializeField] private float hungerDelta = -35f;
    [SerializeField] private float boredomDelta = -10f;
    [SerializeField] private float tirednessDelta = -20f;

    [Header("Requirements")]
    [SerializeField] private bool requiresItem;
    [SerializeField] private string requiredItemType = string.Empty;
    [SerializeField] private bool consumeRequiredItem;

    [Header("Lifecycle")]
    [SerializeField] private bool oneShot;
    [SerializeField] private bool drawDebugGizmo = true;

    private bool hasBeenUsed;

    public float InteractionDuration => interactionDuration;
    public bool IsAvailable => !oneShot || !hasBeenUsed;

    public float GetNeedRelief(NpcNeedType needType)
    {
        float delta = GetNeedDelta(needType);
        return delta < 0f ? -delta : 0f;
    }

    public float GetNeedDelta(NpcNeedType needType)
    {
        switch (needType)
        {
            case NpcNeedType.Hunger:
                return hungerDelta;
            case NpcNeedType.Boredom:
                return boredomDelta;
            case NpcNeedType.Tiredness:
                return tirednessDelta;
            default:
                return 0f;
        }
    }

    public bool CanInteract(NpcBrain npc)
    {
        if (npc == null || !IsAvailable)
        {
            return false;
        }

        if (!requiresItem)
        {
            return true;
        }

        return npc.HasItem(requiredItemType);
    }

    public bool Apply(NpcBrain npc)
    {
        if (!CanInteract(npc))
        {
            return false;
        }

        npc.AdjustNeed(NpcNeedType.Hunger, hungerDelta);
        npc.AdjustNeed(NpcNeedType.Boredom, boredomDelta);
        npc.AdjustNeed(NpcNeedType.Tiredness, tirednessDelta);

        if (requiresItem && consumeRequiredItem)
        {
            npc.ConsumeItem(requiredItemType);
        }

        if (oneShot)
        {
            hasBeenUsed = true;
            gameObject.SetActive(false);
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmo) return;

        Color c = IsAvailable ? new Color(0.2f, 1f, 0.3f, 0.95f) : new Color(0.7f, 0.1f, 0.1f, 0.95f);
        Vector3 pos = transform.position + Vector3.up * 0.1f;
        Gizmos.color = c;
        Gizmos.DrawWireCube(pos, new Vector3(0.35f, 0.35f, 0.35f));
        Gizmos.DrawLine(pos, pos + Vector3.up * Mathf.Clamp(interactionDuration * 0.15f, 0.1f, 0.8f));

#if UNITY_EDITOR
        string details = $"Interact ({(IsAvailable ? "Ready" : "Used")})\n";
        details += $"H:{hungerDelta:+0;-0;0} B:{boredomDelta:+0;-0;0} T:{tirednessDelta:+0;-0;0}";
        if (requiresItem) details += $"\nNeeds item: {requiredItemType}";
        Handles.color = Color.white;
        Handles.Label(pos + Vector3.up * 0.6f, details);
#endif
    }
}
