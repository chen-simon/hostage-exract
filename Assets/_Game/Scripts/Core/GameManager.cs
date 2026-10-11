using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MissionState { Playing, Failed, Complete }

[DefaultExecutionOrder(-500)]
public class GameManager : MonoBehaviour
{
    public static GameManager I { get; private set; }

    [Header("Scene refs (set by LevelBuilder)")]
    public PlayerController player;
    public ExtractionZone extractionZone;
    public Helicopter helicopter;

    [Header("Shared materials for runtime FX")]
    public Material tracerMaterial;
    public Material impactMaterial;
    public Material flashMaterial;

    [Header("Rules")]
    public int maxHostageDeaths = 3;

    public MissionState State { get; private set; } = MissionState.Playing;
    public readonly List<Hostage> hostages = new List<Hostage>();
    public int HostagesDead { get; private set; }
    public int Kills { get; private set; }
    public bool AlarmRaised { get; set; }
    public bool RadioCalled { get; set; }
    public float StartTime { get; private set; }
    public float EndTime { get; private set; }
    public string EndReason { get; private set; }

    public bool IsPlaying => State == MissionState.Playing;
    public int HostageTotal => hostages.Count;
    public int HostagesAlive => hostages.Count - HostagesDead;
    public int HostagesFreed { get { int n = 0; foreach (var h in hostages) if (h.IsFreed && !h.IsDead) n++; return n; } }

    void Awake()
    {
        I = this;
        StartTime = Time.time;
        hostages.AddRange(FindObjectsByType<Hostage>(FindObjectsSortMode.None));
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (extractionZone == null) extractionZone = FindFirstObjectByType<ExtractionZone>();
        if (helicopter == null) helicopter = FindFirstObjectByType<Helicopter>();
    }

    void Start() => LockCursor(true);

    void Update()
    {
        if (IsPlaying)
        {
            if (GameInput.EscapePressed) LockCursor(false);
            else if (GameInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
        }
    }

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    public void HostageKilled(Hostage h)
    {
        if (!IsPlaying) return;
        HostagesDead++;
        MissionUI.I?.Flash($"Hostage killed ({HostagesDead}/{maxHostageDeaths})", new Color(1f, 0.3f, 0.2f));
        if (HostagesDead >= maxHostageDeaths) Fail("Too many hostages lost.");
    }

    public void HostageFreed(Hostage h)
    {
        MissionUI.I?.Flash("Hostage freed — they will follow you", new Color(0.6f, 1f, 0.6f));
    }

    public void GuardKilled(Guard g) => Kills++;

    public void PlayerDied() => Fail("You were killed in action.");

    public void Fail(string reason)
    {
        if (!IsPlaying) return;
        State = MissionState.Failed;
        EndReason = reason;
        EndTime = Time.time;
        LockCursor(false);
        MissionUI.I?.ShowEndScreen(false);
    }

    public void Complete()
    {
        if (!IsPlaying) return;
        State = MissionState.Complete;
        EndReason = "Hostages extracted.";
        EndTime = Time.time;
        foreach (var h in hostages) if (!h.IsDead) h.Extract();
        LockCursor(false);
        MissionUI.I?.ShowEndScreen(true);
    }

    public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

    // True when every living hostage is freed and standing in the extraction zone.
    public bool AllLivingHostagesInZone(out int inside, out int alive)
    {
        inside = 0; alive = 0;
        foreach (var h in hostages)
        {
            if (h.IsDead) continue;
            alive++;
            if (h.IsFreed && extractionZone != null && extractionZone.Contains(h.transform.position)) inside++;
        }
        return alive > 0 && inside == alive;
    }

    public string ObjectiveText()
    {
        int freed = HostagesFreed, alive = HostagesAlive;
        if (helicopter != null && helicopter.Landed) return "Get everyone onto the helipad!";
        if (RadioCalled) return $"Hold the roof — helicopter ETA {Mathf.CeilToInt(helicopter != null ? helicopter.TimeRemaining : 0)}s";
        if (freed < alive) return $"Find and free the hostages ({freed}/{alive})";
        AllLivingHostagesInZone(out int inside, out _);
        if (inside < alive) return $"Escort the hostages to the roof extraction zone ({inside}/{alive})";
        return "Call the helicopter on the roof radio";
    }
}
