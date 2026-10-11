using UnityEngine;

// Roof extraction area. Uses bounds checks (NavMeshAgents don't fire triggers without rigidbodies).
[RequireComponent(typeof(BoxCollider))]
public class ExtractionZone : MonoBehaviour
{
    BoxCollider box;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    public bool Contains(Vector3 worldPos)
    {
        if (box == null) box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPos) - box.center;
        Vector3 h = box.size * 0.5f;
        return Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.y) <= h.y && Mathf.Abs(local.z) <= h.z;
    }
}
