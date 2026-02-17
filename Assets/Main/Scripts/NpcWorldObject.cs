using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class NpcWorldObject : MonoBehaviour
{
    private static readonly List<NpcWorldObject> RegisteredObjectsInternal = new List<NpcWorldObject>();

    public static IReadOnlyList<NpcWorldObject> RegisteredObjects => RegisteredObjectsInternal;

    [SerializeField] private string objectType = "Object";
    [SerializeField, Min(0f)] private float desirability = 1f;
    [SerializeField] private bool visibleToNpc = true;
    [SerializeField] private bool drawDebugGizmo = true;

    private NpcInteractable cachedInteractable;
    private NpcPickupItem cachedPickupItem;

    public string ObjectType => objectType;
    public float Desirability => desirability;
    public bool VisibleToNpc => visibleToNpc;

    public NpcInteractable Interactable
    {
        get
        {
            CacheComponents();
            return cachedInteractable;
        }
    }

    public NpcPickupItem PickupItem
    {
        get
        {
            CacheComponents();
            return cachedPickupItem;
        }
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        if (!RegisteredObjectsInternal.Contains(this))
        {
            RegisteredObjectsInternal.Add(this);
        }
    }

    private void OnDisable()
    {
        RegisteredObjectsInternal.Remove(this);
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(objectType))
        {
            objectType = gameObject.name;
        }

        CacheComponents();
    }

    private void CacheComponents()
    {
        cachedInteractable = GetComponent<NpcInteractable>();
        if (cachedInteractable == null)
        {
            cachedInteractable = GetComponentInChildren<NpcInteractable>();
        }

        cachedPickupItem = GetComponent<NpcPickupItem>();
        if (cachedPickupItem == null)
        {
            cachedPickupItem = GetComponentInChildren<NpcPickupItem>();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmo) return;

        CacheComponents();
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        Color baseColor = visibleToNpc ? new Color(0.15f, 0.8f, 1f, 0.95f) : new Color(0.6f, 0.6f, 0.6f, 0.95f);
        Gizmos.color = baseColor;
        Gizmos.DrawWireSphere(origin, 0.25f);

        if (cachedInteractable != null)
        {
            Gizmos.color = new Color(0.25f, 1f, 0.4f, 0.95f);
            Gizmos.DrawWireCube(origin + Vector3.up * 0.25f, Vector3.one * 0.22f);
        }

        if (cachedPickupItem != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.95f);
            Gizmos.DrawWireCube(origin + Vector3.up * 0.45f, Vector3.one * 0.22f);
        }

#if UNITY_EDITOR
        string tags = $"{objectType} | D:{desirability:F1}";
        if (cachedInteractable != null) tags += " | Interact";
        if (cachedPickupItem != null) tags += " | Pickup";
        if (!visibleToNpc) tags += " | Hidden";
        Handles.color = Color.white;
        Handles.Label(origin + Vector3.up * 0.7f, tags);
#endif
    }
}
