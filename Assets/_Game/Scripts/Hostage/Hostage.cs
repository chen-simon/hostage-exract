using UnityEngine;
using UnityEngine.AI;

public enum HostageState { Tied, Following, Holding, Dead, Extracted }

// Lies tied on the floor. Hold E to free, then follows the player via NavMesh.
[RequireComponent(typeof(Health))]
public class Hostage : MonoBehaviour, IInteractable
{
    public Transform visual;        // rotated flat while tied / dead
    public GameObject ropes;
    public float freeTime = 2f;
    public float followDistance = 2.5f;
    public float walkSpeed = 3.5f;
    public float runSpeed = 6f;

    public HostageState State { get; private set; } = HostageState.Tied;
    public bool IsDead => State == HostageState.Dead;
    public bool IsFreed => State != HostageState.Tied;

    public static int FollowingCount
    {
        get
        {
            if (GameManager.I == null) return 0;
            int n = 0;
            foreach (var h in GameManager.I.hostages) if (h.State == HostageState.Following) n++;
            return n;
        }
    }

    NavMeshAgent agent;
    Health health;
    PlayerController player;
    int followIndex;
    float nextRepath;
    Vector3 visualStandPos;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        health.Died += Die;
        if (agent != null) agent.enabled = false;
        visualStandPos = visual != null ? visual.localPosition : Vector3.zero;
        LieDown();
    }

    void Start()
    {
        player = GameManager.I != null ? GameManager.I.player : FindFirstObjectByType<PlayerController>();
    }

    void LieDown()
    {
        if (visual == null) return;
        visual.localRotation = Quaternion.Euler(-90f, 0, 0);
        visual.localPosition = visualStandPos + Vector3.up * 0.25f;
    }

    // ---------------------------------------------------------------- IInteractable

    public float HoldTime => State == HostageState.Tied ? freeTime : 0.25f;

    public string GetPrompt(out bool usable)
    {
        usable = true;
        switch (State)
        {
            case HostageState.Tied: return "Free hostage";
            case HostageState.Following: return "Tell hostage to hold position";
            case HostageState.Holding: return "Tell hostage to follow";
            default: usable = false; return "";
        }
    }

    public void Interact()
    {
        switch (State)
        {
            case HostageState.Tied: Free(); break;
            case HostageState.Following: State = HostageState.Holding; StopAgent(); break;
            case HostageState.Holding: State = HostageState.Following; break;
        }
    }

    void Free()
    {
        followIndex = GameManager.I != null ? GameManager.I.HostagesFreed : 0; // freed before this one
        State = HostageState.Following;
        if (ropes != null) ropes.SetActive(false);
        if (visual != null) { visual.localRotation = Quaternion.identity; visual.localPosition = visualStandPos; }
        if (agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }
        GameManager.I?.HostageFreed(this);
    }

    // ---------------------------------------------------------------- update

    void Update()
    {
        if (State != HostageState.Following || player == null) return;
        bool playing = GameManager.I != null && GameManager.I.IsPlaying;

        // Crouch with the player.
        if (visual != null)
        {
            float targetY = player.IsCrouching ? 0.65f : 1f;
            var s = visual.localScale;
            s.y = Mathf.MoveTowards(s.y, targetY, Time.deltaTime * 3f);
            visual.localScale = s;
        }

        if (!playing || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        float dist = Vector3.Distance(transform.position, player.transform.position);
        float keep = followDistance + followIndex * 0.6f;
        if (dist <= keep) { StopAgent(); return; }

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + 0.3f;
            Vector3 behind = -player.transform.forward * keep;
            Vector3 side = player.transform.right * ((followIndex % 2 == 0) ? 0.7f : -0.7f);
            Vector3 target = player.transform.position + behind + side;
            if (!NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas) &&
                !NavMesh.SamplePosition(player.transform.position, out hit, 4f, NavMesh.AllAreas))
                return;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
        float speed = dist > 7f ? runSpeed : walkSpeed;
        if (player.IsCrouching) speed *= 0.6f;
        agent.speed = speed;
    }

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            if (agent.hasPath) agent.ResetPath();
        }
    }

    public void Extract()
    {
        if (IsDead) return;
        State = HostageState.Extracted;
        StopAgent();
    }

    void Die()
    {
        bool wasTied = State == HostageState.Tied;
        State = HostageState.Dead;
        if (agent != null) agent.enabled = false;
        if (!wasTied) LieDown();
        GameManager.I?.HostageKilled(this);
    }
}
