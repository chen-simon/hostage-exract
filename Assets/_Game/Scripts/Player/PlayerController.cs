using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Refs")]
    public Transform head;              // Cinemachine follows + copies rotation of this
    public Camera viewmodelCamera;      // overlay camera, stacked onto Camera.main at runtime

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 6.5f;
    public float crouchSpeed = 2f;
    public float jumpHeight = 0.9f;
    public float gravity = -20f;
    public float acceleration = 12f;

    [Header("Look")]
    public float lookSensitivity = 0.08f;

    [Header("Stance")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float standEye = 1.65f;
    public float crouchEye = 0.9f;

    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded => cc.isGrounded;
    public Vector3 Velocity { get; private set; }
    public float HorizontalSpeed { get { var v = Velocity; v.y = 0; return v.magnitude; } }
    public Vector3 CenterPosition => transform.position + Vector3.up * cc.height * 0.5f;
    public Vector3 HeadPosition => head.position;
    public bool Alive => health == null || !health.IsDead;

    CharacterController cc;
    Health health;
    float yaw, pitch, pendingRecoilPitch, pendingRecoilYaw;
    Vector3 planarVelocity;
    float verticalVelocity;
    bool wasGrounded = true;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<Health>();
    }

    void Start()
    {
        yaw = transform.eulerAngles.y;
        if (health != null) health.Died += OnDied;

        var main = Camera.main;
        if (main != null && viewmodelCamera != null)
        {
            var data = main.GetUniversalAdditionalCameraData();
            if (!data.cameraStack.Contains(viewmodelCamera)) data.cameraStack.Add(viewmodelCamera);
        }
    }

    void OnDied()
    {
        GameManager.I?.PlayerDied();
    }

    void Update()
    {
        bool canAct = Alive && GameManager.I != null && GameManager.I.IsPlaying;
        if (!Alive)
        {
            // Slump to the floor.
            head.localPosition = Vector3.Lerp(head.localPosition, new Vector3(0, 0.3f, 0), Time.deltaTime * 3f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-10, 0, 40), Time.deltaTime * 3f);
            return;
        }

        if (canAct && Cursor.lockState == CursorLockMode.Locked) Look();
        Move(canAct);
        UpdateStance(canAct);
    }

    void Look()
    {
        Vector2 look = GameInput.Look * lookSensitivity;
        yaw += look.x;
        pitch -= look.y;

        // Apply recoil smoothly over a few frames.
        float k = Mathf.Min(1f, Time.deltaTime * 25f);
        pitch -= pendingRecoilPitch * k; pendingRecoilPitch -= pendingRecoilPitch * k;
        yaw += pendingRecoilYaw * k; pendingRecoilYaw -= pendingRecoilYaw * k;

        pitch = Mathf.Clamp(pitch, -85f, 85f);
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        head.localRotation = Quaternion.Euler(pitch, 0, 0);
    }

    public void AddRecoil(float up, float side)
    {
        pendingRecoilPitch += up;
        pendingRecoilYaw += side;
    }

    void Move(bool canAct)
    {
        Vector2 input = canAct ? GameInput.Move : Vector2.zero;
        if (input.sqrMagnitude > 1f) input.Normalize();

        if (canAct && GameInput.CrouchPressed)
        {
            if (IsCrouching) TryStand(); else IsCrouching = true;
        }

        IsSprinting = canAct && GameInput.SprintHeld && input.y > 0.3f;
        if (IsSprinting && IsCrouching && !TryStand()) IsSprinting = false;

        float speed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
        Vector3 wish = (transform.forward * input.y + transform.right * input.x) * speed;
        planarVelocity = Vector3.MoveTowards(planarVelocity, wish, acceleration * speed * Time.deltaTime);

        bool grounded = cc.isGrounded;
        if (grounded)
        {
            if (!wasGrounded && verticalVelocity < -6f)
                NoiseSystem.Emit(transform.position, 40f, gameObject, true); // heavy landing
            verticalVelocity = -2f;
            if (canAct && GameInput.JumpPressed && !IsCrouching)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;
        wasGrounded = grounded;

        Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
        Vector3 before = transform.position;
        cc.Move(motion * Time.deltaTime);
        Velocity = (transform.position - before) / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    bool TryStand()
    {
        if (!IsCrouching) return true;
        float r = cc.radius * 0.9f;
        Vector3 bottom = transform.position + Vector3.up * (r + 0.05f);
        Vector3 top = transform.position + Vector3.up * (standHeight - r);
        if (Physics.CheckCapsule(bottom, top, r, Layers.OccluderMask, QueryTriggerInteraction.Ignore)) return false;
        IsCrouching = false;
        return true;
    }

    void UpdateStance(bool canAct)
    {
        float targetH = IsCrouching ? crouchHeight : standHeight;
        cc.height = Mathf.MoveTowards(cc.height, targetH, Time.deltaTime * 6f);
        cc.center = new Vector3(0, cc.height * 0.5f, 0);
        float eye = IsCrouching ? crouchEye : standEye;
        var hp = head.localPosition;
        hp.y = Mathf.MoveTowards(hp.y, eye, Time.deltaTime * 5f);
        head.localPosition = hp;
    }
}
