using UnityEngine;

// Emits footstep noise into the NoiseSystem. Following hostages make every step louder.
public class PlayerNoise : MonoBehaviour
{
    public float crouchDb = 20f;
    public float walkDb = 35f;
    public float sprintDb = 55f;
    public float perFollowerDb = 5f;

    public float crouchStride = 0.9f;
    public float walkStride = 1.6f;
    public float sprintStride = 2.3f;

    PlayerController pc;
    float distanceSinceStep;

    void Awake() => pc = GetComponent<PlayerController>();

    void Update()
    {
        if (!pc.Alive || !pc.IsGrounded) return;
        float speed = pc.HorizontalSpeed;
        if (speed < 0.5f) { distanceSinceStep = Mathf.Min(distanceSinceStep, 0.5f); return; }

        distanceSinceStep += speed * Time.deltaTime;
        float stride = pc.IsCrouching ? crouchStride : (pc.IsSprinting ? sprintStride : walkStride);
        if (distanceSinceStep < stride) return;
        distanceSinceStep = 0f;

        float db = pc.IsCrouching ? crouchDb : (pc.IsSprinting ? sprintDb : walkDb);
        db += perFollowerDb * Hostage.FollowingCount;
        NoiseSystem.Emit(transform.position, db, gameObject, true);
    }
}
