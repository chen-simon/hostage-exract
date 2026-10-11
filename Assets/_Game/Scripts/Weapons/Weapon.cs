using Unity.Cinemachine;
using UnityEngine;

// Hitscan weapon with a greybox viewmodel. Lives under the player's head.
public class Weapon : MonoBehaviour
{
    public WeaponData data;
    public Transform model;          // viewmodel root (kicked/lowered procedurally)
    public Transform muzzle;
    public Light muzzleLight;
    public GameObject muzzleFlash;
    public CinemachineImpulseSource impulse;

    public int AmmoInMag { get; private set; }
    public int Reserve { get; private set; }
    public bool IsReloading { get; private set; }

    PlayerController pc;
    float nextFireTime, reloadEndTime, flashUntil, kick, raise;
    Vector3 restPos;
    Quaternion restRot;

    void Awake()
    {
        pc = GetComponentInParent<PlayerController>();
        AmmoInMag = data.magSize;
        Reserve = data.reserveAmmo;
        if (model != null) { restPos = model.localPosition; restRot = model.localRotation; }
    }

    void OnEnable()
    {
        raise = 0f;
        IsReloading = false;
        nextFireTime = Time.time + 0.35f; // can't fire while drawing
        SetFlash(false);
    }

    void Update()
    {
        var gm = GameManager.I;
        bool canAct = pc != null && pc.Alive && gm != null && gm.IsPlaying && Cursor.lockState == CursorLockMode.Locked;

        if (IsReloading && Time.time >= reloadEndTime) FinishReload();

        if (canAct)
        {
            bool trigger = data.automatic ? GameInput.FireHeld : GameInput.FirePressed;
            if (trigger && !IsReloading && Time.time >= nextFireTime)
            {
                if (AmmoInMag > 0) Fire();
                else if (Reserve > 0) StartReload();
            }
            if (GameInput.ReloadPressed) StartReload();
        }

        if (Time.time > flashUntil) SetFlash(false);
        Animate();
    }

    void Fire()
    {
        AmmoInMag--;
        nextFireTime = Time.time + 60f / data.fireRate;

        // Spread grows with movement, shrinks when crouched.
        float spread = data.spread * (1f + pc.HorizontalSpeed * 0.35f) * (pc.IsCrouching ? 0.6f : 1f);
        Vector2 r = Random.insideUnitCircle * spread;
        Transform head = pc.head;
        Vector3 dir = Quaternion.AngleAxis(r.x, head.up) * Quaternion.AngleAxis(r.y, head.right) * head.forward;

        Vector3 end = head.position + dir * data.range;
        if (Physics.Raycast(head.position, dir, out var hit, data.range, Layers.PlayerShotMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var hb = hit.collider.GetComponent<Hitbox>();
            if (hb != null)
            {
                bool wasDead = hb.health != null && hb.health.IsDead;
                hb.Hit(data.damage, dir);
                if (!wasDead) MissionUI.I?.ShowHitMarker(hb.health != null && hb.health.IsDead);
            }
            Tracer.Impact(hit);
        }
        Tracer.Spawn(muzzle != null ? muzzle.position : head.position, end, new Color(1f, 0.85f, 0.5f, 0.8f));

        NoiseSystem.Emit(head.position, data.noiseDb, pc.gameObject, true);
        pc.AddRecoil(data.recoilPitch, Random.Range(-data.recoilYaw, data.recoilYaw));
        if (impulse != null) impulse.GenerateImpulse(data.impulseForce);
        kick = 1f;
        flashUntil = Time.time + 0.04f;
        SetFlash(true);
    }

    public void StartReload()
    {
        if (IsReloading || AmmoInMag >= data.magSize || Reserve <= 0) return;
        IsReloading = true;
        reloadEndTime = Time.time + data.reloadTime;
        NoiseSystem.Emit(transform.position, data.reloadNoiseDb, pc.gameObject, true);
    }

    void FinishReload()
    {
        IsReloading = false;
        int take = Mathf.Min(data.magSize - AmmoInMag, Reserve);
        AmmoInMag += take;
        Reserve -= take;
    }

    void SetFlash(bool on)
    {
        if (muzzleLight != null) muzzleLight.enabled = on;
        if (muzzleFlash != null) muzzleFlash.SetActive(on);
    }

    void Animate()
    {
        if (model == null) return;
        raise = Mathf.MoveTowards(raise, 1f, Time.deltaTime * 3f);
        kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 12f);

        float bob = pc != null && pc.IsGrounded ? Mathf.Sin(Time.time * (pc.IsSprinting ? 14f : 9f)) * Mathf.Clamp01(pc.HorizontalSpeed / 6f) * 0.015f : 0f;
        float reloadDip = 0f;
        if (IsReloading)
        {
            float t = 1f - (reloadEndTime - Time.time) / data.reloadTime;
            reloadDip = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        }
        float sprintTilt = pc != null && pc.IsSprinting ? 1f : 0f;

        model.localPosition = restPos
            + new Vector3(0, bob - (1f - raise) * 0.3f - reloadDip * 0.12f, -kick * 0.06f);
        model.localRotation = restRot * Quaternion.Euler(-kick * 4f + reloadDip * 25f + sprintTilt * 10f, sprintTilt * -25f, reloadDip * 20f);
    }
}
