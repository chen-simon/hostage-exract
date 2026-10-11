using UnityEngine;

// Waypoints are the child transforms, in order. Loops.
public class PatrolRoute : MonoBehaviour
{
    public float waitAtPoint = 2f;

    public int Count => transform.childCount;
    public Vector3 Get(int i) => transform.GetChild(i % transform.childCount).position;

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        for (int i = 0; i < transform.childCount; i++)
        {
            Gizmos.DrawWireSphere(Get(i), 0.3f);
            Gizmos.DrawLine(Get(i), Get(i + 1));
        }
    }
}
