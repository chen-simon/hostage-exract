using UnityEngine;

// Vision cone + line of sight checks.
public class GuardVision : MonoBehaviour
{
    public float range = 25f;
    public float fov = 90f;
    public float closeRange = 3f;   // inside this, a 360° "feel" radius

    Guard guard;

    void Awake() => guard = GetComponent<Guard>();

    public bool CanSee(Vector3 target, out float distance)
    {
        Vector3 eye = guard.EyePosition;
        Vector3 to = target - eye;
        distance = to.magnitude;
        if (distance > range) return false;
        if (distance > closeRange)
        {
            Vector3 flat = to; flat.y = 0;
            Vector3 fwd = guard.transform.forward; fwd.y = 0;
            if (Vector3.Angle(fwd, flat) > fov * 0.5f) return false;
        }
        return !Physics.Linecast(eye, target, Layers.OccluderMask, QueryTriggerInteraction.Ignore);
    }

    // Is the player visible? Checks head and chest so a crouched player behind low cover still shows.
    public bool CanSeePlayer(PlayerController player, out float distance)
    {
        distance = float.MaxValue;
        if (player == null || !player.Alive) return false;
        if (CanSee(player.HeadPosition, out distance)) return true;
        return CanSee(player.CenterPosition, out distance);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Gizmos.DrawRay(eye, Quaternion.Euler(0, fov * 0.5f, 0) * transform.forward * range);
        Gizmos.DrawRay(eye, Quaternion.Euler(0, -fov * 0.5f, 0) * transform.forward * range);
    }
}
