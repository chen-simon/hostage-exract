using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum GuardState { Idle, Patrol, Suspicious, Investigate, Combat, Dead }

// One-file guard brain: Unaware (Idle/Patrol) → Suspicious → Investigate → Combat. Spaghetti by design.
[RequireComponent(typeof(Health), typeof(GuardVision), typeof(GuardHearing))]
public class Guard : MonoBehaviour
{
    public static readonly List<Guard> All = new List<Guard>();

    [Header("Setup")]
    public PatrolRoute route;
    [Tooltip("Never moves (watchtowers). No NavMeshAgent needed.")]
    public bool stationary;
    [Tooltip("Inside the main building: repositions to rally points on alarm.")]
    public bool interior;
    [Tooltip("Idle head sweep in degrees (towers).")]
    public float scanAngle;

    [Header("Movement")]
    public float walkSpeed = 1.8f;
    public float runSpeed = 4.5f;

    [Header("Combat")]
    public Transform muzzle;
    public float damage = 9f;
    public int burstCount = 3;
    public float burstInterval = 0.11f;
    public float burstPause = 0.9f;
    public float aimSpread = 2.5f;
    public float preferredRange = 12f;
    public float shotNoiseDb = 115f;

    [Header("Timers")]
    public float suspiciousTime = 3f;
    public float lookAroundTime = 6f;
    public float bodyToCombatDelay = 2f;

    [Header("UI")]
    public TextMesh indicator;

    public GuardState State { get; private set; }
    public float CombatTime { get; private set; }
    public bool IsDead => State == GuardState.Dead;
    public Vector3 EyePosition => transform.position + transform.up * 1.6f;
    public GuardVision Vision { get; private set; }
    public GuardHearing Hearing { get; private set; }
    public float Detection { get; private set; }

    NavMeshAgent agent;
    Health health;
    PlayerController player;
    Vector3 postPos;
    Quaternion postRot;
    int waypoint;
    float waitUntil, stateTimer, nextShot, lastSeenTime, nextBodyCheck, bodyCombatAt = -1f, nextHuntRefresh;
    bool arrivedAtStimulus, alarmMode;
    int burstLeft;
    Vector3 stimulus, lastKnown, lastDestination = Vector3.positiveInfinity;
    Vector3? rallyPoint;
    readonly HashSet<Guard> seenBodies = new HashSet<Guard>();

    bool HasAgent => agent != null && agent.enabled && agent.isOnNavMesh;
    GuardState BaseState => route != null && route.Count > 0 && !stationary ? GuardState.Patrol : GuardState.Idle;

    void Awake()
    {
        All.Add(this);
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        Vision = GetComponent<GuardVision>();
        Hearing = GetComponent<GuardHearing>();
        postPos = transform.position;
        postRot = transform.rotation;
        health.Died += Die;
        health.Damaged += OnDamaged;
    }

    void OnDestroy() => All.Remove(this);

    void Start()
    {
        player = GameManager.I != null ? GameManager.I.player : FindFirstObjectByType<PlayerController>();
        if (agent != null)
        {
            if (stationary) agent.enabled = false;
            else
            {
                agent.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
                agent.speed = walkSpeed;
            }
        }
        State = BaseState;
    }

    // ---------------------------------------------------------------- stimuli

    public void HearNoise(Vector3 pos, float db, bool loud)
    {
        if (IsDead || State == GuardState.Combat) return;
        switch (State)
        {
            case GuardState.Idle:
            case GuardState.Patrol:
                if (loud) EnterInvestigate(pos); else EnterSuspicious(pos);
                break;
            case GuardState.Suspicious:
                EnterInvestigate(pos);
                break;
            case GuardState.Investigate:
                EnterInvestigate(pos);
                break;
        }
    }

    void OnDamaged(float amount, Vector3 dir)
    {
        if (IsDead) return;
        Vector3 guess = player != null ? player.CenterPosition : transform.position - dir * 10f;
        EnterCombat(guess);
    }

    public void OnAlarm(Vector3? rally)
    {
        if (IsDead) return;
        alarmMode = true;
        if (rally.HasValue) rallyPoint = rally;
        EnterCombat(player != null ? player.CenterPosition : transform.position);
    }

    // ---------------------------------------------------------------- state transitions

    void EnterSuspicious(Vector3 pos)
    {
        State = GuardState.Suspicious;
        stimulus = pos;
        stateTimer = suspiciousTime;
        Stop();
    }

    void EnterInvestigate(Vector3 pos)
    {
        State = GuardState.Investigate;
        stimulus = pos;
        arrivedAtStimulus = false;
        stateTimer = lookAroundTime;
    }

    void EnterCombat(Vector3 knownPos)
    {
        if (IsDead) return;
        if (State != GuardState.Combat) nextShot = Time.time + Random.Range(0.4f, 0.8f); // reaction time
        State = GuardState.Combat;
        lastKnown = knownPos;
        Detection = 1f;
    }

    void ReturnToBase()
    {
        State = BaseState;
        Detection = Mathf.Min(Detection, 0.2f);
    }

    // ---------------------------------------------------------------- update

    void Update()
    {
        if (IsDead) return;
        var gm = GameManager.I;
        if (gm == null || !gm.IsPlaying) { Stop(); UpdateIndicator(); return; }

        bool sees = Vision.CanSeePlayer(player, out float dist);
        UpdateDetection(sees, dist);
        CheckBodies();

        switch (State)
        {
            case GuardState.Idle: UpdateIdle(); break;
            case GuardState.Patrol: UpdatePatrol(); break;
            case GuardState.Suspicious: UpdateSuspicious(); break;
            case GuardState.Investigate: UpdateInvestigate(); break;
            case GuardState.Combat: UpdateCombat(sees, dist); break;
        }
        UpdateIndicator();
    }

    void UpdateDetection(bool sees, float dist)
    {
        if (sees)
        {
            float stance = player.IsCrouching ? 0.55f : 1f;
            // Under a roof = darker = harder to see.
            float light = Physics.Raycast(player.HeadPosition, Vector3.up, 30f, Layers.OccluderMask, QueryTriggerInteraction.Ignore) ? 0.75f : 1f;
            float distFactor = Mathf.Clamp01(1.15f - dist / Vision.range);
            float rate = 2.2f * distFactor * stance * light;
            if (player.IsSprinting) rate *= 1.3f;
            if (State == GuardState.Suspicious || State == GuardState.Investigate) rate *= 2f;
            if (dist < Vision.closeRange) rate = 10f;
            Detection = Mathf.Clamp01(Detection + rate * Time.deltaTime);
            if (State == GuardState.Combat) { lastKnown = player.CenterPosition; lastSeenTime = Time.time; }
        }
        else Detection = Mathf.Clamp01(Detection - 0.2f * Time.deltaTime);

        if (State == GuardState.Combat || !sees) return;
        Vector3 ppos = player.transform.position;
        if (Detection >= 1f) EnterCombat(player.CenterPosition);
        else if ((State == GuardState.Idle || State == GuardState.Patrol) && Detection > 0.3f) EnterSuspicious(ppos);
        else if (State == GuardState.Suspicious && Detection > 0.6f) EnterInvestigate(ppos);
        else if (State == GuardState.Investigate) { stimulus = ppos; arrivedAtStimulus = false; }
    }

    void CheckBodies()
    {
        if (bodyCombatAt > 0f && Time.time >= bodyCombatAt)
        {
            bodyCombatAt = -1f;
            if (State != GuardState.Combat) EnterCombat(stimulus);
        }
        if (State == GuardState.Combat || Time.time < nextBodyCheck) return;
        nextBodyCheck = Time.time + 0.5f;
        foreach (var g in All)
        {
            if (g == this || !g.IsDead || seenBodies.Contains(g)) continue;
            if (!Vision.CanSee(g.transform.position + Vector3.up * 0.3f, out _)) continue;
            seenBodies.Add(g);
            EnterInvestigate(g.transform.position);
            bodyCombatAt = Time.time + bodyToCombatDelay;
            MissionUI.I?.Flash("A guard found a body!", new Color(1f, 0.7f, 0.3f));
            break;
        }
    }

    void UpdateIdle()
    {
        if (HasAgent && FlatDistance(transform.position, postPos) > 0.7f)
        {
            MoveTo(postPos, walkSpeed);
            return;
        }
        Stop();
        Quaternion target = postRot;
        if (scanAngle > 0f) target = postRot * Quaternion.Euler(0, Mathf.Sin(Time.time * 0.35f + postPos.x) * scanAngle, 0);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 2f);
    }

    void UpdatePatrol()
    {
        if (!HasAgent || route == null || route.Count == 0) { UpdateIdle(); return; }
        Vector3 target = route.Get(waypoint);
        if (FlatDistance(transform.position, target) < 0.8f)
        {
            if (waitUntil <= 0f) waitUntil = Time.time + route.waitAtPoint;
            Stop();
            if (Time.time >= waitUntil) { waypoint = (waypoint + 1) % route.Count; waitUntil = 0f; }
            return;
        }
        MoveTo(target, walkSpeed);
    }

    void UpdateSuspicious()
    {
        Stop();
        FaceTowards(stimulus, 4f);
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) ReturnToBase();
    }

    void UpdateInvestigate()
    {
        if (!arrivedAtStimulus)
        {
            if (!HasAgent || FlatDistance(transform.position, stimulus) < 1.5f) { arrivedAtStimulus = true; stateTimer = lookAroundTime; }
            else
            {
                MoveTo(stimulus, walkSpeed * 1.3f);
                // Destination unreachable (e.g. vent) – look from here.
                if (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete && agent.remainingDistance < 1f)
                    arrivedAtStimulus = true;
                return;
            }
        }
        Stop();
        stateTimer -= Time.deltaTime;
        if (!HasAgent) FaceTowards(stimulus, 4f);
        else transform.Rotate(0, Mathf.Sin(Time.time * 1.2f) * 70f * Time.deltaTime, 0);
        if (stateTimer <= 0f) ReturnToBase();
    }

    void UpdateCombat(bool sees, float dist)
    {
        CombatTime += Time.deltaTime;

        if (sees)
        {
            FaceTowards(player.transform.position, 8f);
            if (HasAgent && rallyPoint == null && dist > preferredRange) MoveTo(player.transform.position, runSpeed);
            else Stop();
            if (Time.time >= nextShot)
            {
                if (burstLeft <= 0) burstLeft = burstCount;
                ShootAtPlayer(dist);
                burstLeft--;
                nextShot = Time.time + (burstLeft > 0 ? burstInterval : burstPause * Random.Range(0.8f, 1.3f));
            }
            return;
        }

        // Lost sight. After the alarm, guards roughly know where the player is.
        if (alarmMode && Time.time >= nextHuntRefresh)
        {
            nextHuntRefresh = Time.time + 4f;
            if (player != null) lastKnown = player.CenterPosition;
        }
        else if (!alarmMode && Time.time - lastSeenTime > 20f && Time.time >= nextHuntRefresh)
        {
            nextHuntRefresh = Time.time + 8f;
            if (player != null) lastKnown = player.transform.position + Random.insideUnitSphere.WithY(0) * 8f;
        }

        if (!HasAgent) { FaceTowards(lastKnown, 3f); return; }
        Vector3 goal = rallyPoint ?? lastKnown;
        if (FlatDistance(transform.position, goal) > 1.5f) MoveTo(goal, runSpeed);
        else
        {
            Stop();
            if (rallyPoint.HasValue) FaceTowards(lastKnown, 3f);
            else transform.Rotate(0, 90f * Time.deltaTime, 0);
        }
    }

    // ---------------------------------------------------------------- actions

    void ShootAtPlayer(float dist)
    {
        Vector3 from = muzzle != null ? muzzle.position : EyePosition;
        Vector3 aim = player.CenterPosition + Vector3.up * 0.2f;
        float spread = aimSpread + dist * 0.05f + player.HorizontalSpeed * 0.4f + (player.IsCrouching ? 0.5f : 0f);
        Vector3 dir = (aim - from).normalized;
        dir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * dir;

        Vector3 end = from + dir * 150f;
        if (Physics.Raycast(from, dir, out var hit, 150f, Layers.GuardShotMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var hb = hit.collider.GetComponent<Hitbox>();
            if (hb != null) hb.Hit(damage, dir);
            else if (hit.collider.gameObject.layer == Layers.Player)
                hit.collider.GetComponentInParent<Health>()?.Damage(damage, dir);
            else Tracer.Impact(hit);
        }
        Tracer.Spawn(from, end, new Color(1f, 0.6f, 0.3f, 0.9f), 0.08f);
        NoiseSystem.Emit(from, shotNoiseDb, gameObject, false);
    }

    void MoveTo(Vector3 pos, float speed)
    {
        if (!HasAgent) return;
        agent.isStopped = false;
        agent.speed = speed;
        agent.updateRotation = true;
        if ((pos - lastDestination).sqrMagnitude > 0.25f || !agent.hasPath)
        {
            agent.SetDestination(pos);
            lastDestination = pos;
        }
    }

    void Stop()
    {
        if (!HasAgent) return;
        agent.isStopped = true;
        agent.updateRotation = false;
        if (agent.hasPath) agent.ResetPath();
        lastDestination = Vector3.positiveInfinity;
    }

    void FaceTowards(Vector3 pos, float speed)
    {
        Vector3 d = pos - transform.position; d.y = 0;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * speed);
    }

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    void UpdateIndicator()
    {
        if (indicator == null) return;
        string text = ""; Color c = Color.white;
        switch (State)
        {
            case GuardState.Idle:
            case GuardState.Patrol:
                if (Detection > 0.05f) { text = "?"; c = new Color(1, 1, 1, Detection); }
                break;
            case GuardState.Suspicious: text = "?"; c = new Color(1f, 0.9f, 0.2f); break;
            case GuardState.Investigate: text = "?"; c = new Color(1f, 0.55f, 0.1f); break;
            case GuardState.Combat: text = "!"; c = new Color(1f, 0.15f, 0.1f); break;
        }
        indicator.text = text;
        indicator.color = c;
        var cam = Camera.main;
        if (cam != null && text.Length > 0) indicator.transform.rotation = Quaternion.LookRotation(indicator.transform.position - cam.transform.position);
    }

    // ---------------------------------------------------------------- death

    void Die()
    {
        State = GuardState.Dead;
        if (agent != null) agent.enabled = false;
        if (indicator != null) indicator.text = "";
        GameManager.I?.GuardKilled(this);
        NoiseSystem.Emit(transform.position, 45f, gameObject, true); // body hitting the floor
        StartCoroutine(FallOver());
    }

    IEnumerator FallOver()
    {
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(-85f, 0, 0);
        Vector3 p0 = transform.position, p1 = p0 + Vector3.up * 0.3f;
        for (float t = 0; t < 1f; t += Time.deltaTime * 2.5f)
        {
            transform.rotation = Quaternion.Slerp(start, end, t * t);
            transform.position = Vector3.Lerp(p0, p1, t);
            yield return null;
        }
        transform.rotation = end;
        transform.position = p1;
    }
}
