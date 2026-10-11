using UnityEngine;

// A guard that stays in Combat for `combatTimeToAlarm` seconds raises the facility alarm. Irreversible.
public class AlarmSystem : MonoBehaviour
{
    public static AlarmSystem I { get; private set; }

    public float combatTimeToAlarm = 5f;
    [Tooltip("Where interior guards reposition when the alarm goes off (stairwells, roof).")]
    public Transform[] rallyPoints;
    public AudioSource siren; // hook for a siren clip

    public bool Raised { get; private set; }
    int nextRally;

    void Awake() => I = this;

    void Update()
    {
        if (Raised || GameManager.I == null || !GameManager.I.IsPlaying) return;
        foreach (var g in Guard.All)
            if (g != null && !g.IsDead && g.CombatTime >= combatTimeToAlarm) { Raise(); break; }
    }

    public void Raise()
    {
        if (Raised) return;
        Raised = true;
        if (GameManager.I != null) GameManager.I.AlarmRaised = true;
        if (siren != null) siren.Play();
        MissionUI.I?.Flash("Alarm raised! The whole facility is coming for you.", new Color(1f, 0.3f, 0.2f), 4f);
        foreach (var g in Guard.All)
        {
            if (g == null || g.IsDead) continue;
            Vector3? rally = null;
            if (g.interior && rallyPoints != null && rallyPoints.Length > 0)
                rally = rallyPoints[nextRally++ % rallyPoints.Length].position;
            g.OnAlarm(rally);
        }
    }

    // Used by the extraction: if the alarm is on, everyone pushes the roof.
    public void PushTo(Vector3 position)
    {
        if (!Raised) return;
        foreach (var g in Guard.All)
            if (g != null && !g.IsDead) g.OnAlarm(position + Random.insideUnitSphere.WithY(0) * 6f);
    }
}

public static class VectorExt
{
    public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
}
