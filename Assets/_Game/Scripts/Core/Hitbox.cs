using UnityEngine;

// Put on any collider that can take bullets. Routes damage to the owning Health.
public class Hitbox : MonoBehaviour
{
    public Health health;
    public float multiplier = 1f;
    public bool isHead;

    void Awake()
    {
        if (health == null) health = GetComponentInParent<Health>();
    }

    public void Hit(float damage, Vector3 direction)
    {
        if (health != null) health.Damage(damage * multiplier, direction);
    }
}
