using UnityEngine;

[DisallowMultipleComponent]
public class NpcPickupItem : MonoBehaviour
{
    [SerializeField] private string itemType = "GenericItem";
    [SerializeField, Min(0f)] private float usefulness = 1f;
    [SerializeField] private bool disableObjectWhenPickedUp = true;
    [SerializeField] private bool parentToPickerWhenPickedUp;

    public string ItemType => itemType;
    public float Usefulness => usefulness;
    public bool IsPickedUp { get; private set; }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            itemType = "GenericItem"; 
        }
    }

    public bool TryPickUp(NpcBrain picker)
    {
        if (IsPickedUp || picker == null)
        {
            return false;
        }

        IsPickedUp = true;
        picker.RegisterPickedUpItem(this);

        if (parentToPickerWhenPickedUp)
        {
            transform.SetParent(picker.transform, true);
        }

        if (disableObjectWhenPickedUp)
        {
            gameObject.SetActive(false);
        }

        return true;
    }
}
