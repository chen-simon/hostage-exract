using UnityEngine;

// Greybox heli: flies in from the horizon after the radio call, lands on the helipad.
public class Helicopter : MonoBehaviour
{
    public float arrivalTime = 45f;
    public float flightDuration = 25f;   // last N seconds of the countdown are spent flying in
    public Transform landingPoint;
    public Transform mainRotor;
    public Transform tailRotor;
    public float hoverHeight = 1.4f;

    public bool Called { get; private set; }
    public bool Landed { get; private set; }
    public float TimeRemaining => Called ? Mathf.Max(0f, arrivalTime - (Time.time - callTime)) : arrivalTime;

    float callTime;
    Vector3 startPos;

    void Awake()
    {
        SetVisible(false);
    }

    void SetVisible(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
    }

    public void Call()
    {
        if (Called) return;
        Called = true;
        callTime = Time.time;
        Vector3 land = LandPos();
        startPos = land + new Vector3(-250f, 60f, 300f);
        transform.position = startPos;
    }

    Vector3 LandPos() => (landingPoint != null ? landingPoint.position : transform.position) + Vector3.up * hoverHeight;

    void Update()
    {
        if (!Called) return;
        if (mainRotor != null) mainRotor.Rotate(0, 1400f * Time.deltaTime, 0, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(1800f * Time.deltaTime, 0, 0, Space.Self);

        float remaining = TimeRemaining;
        Vector3 land = LandPos();
        if (remaining > flightDuration) return;

        SetVisible(true);
        float t = 1f - remaining / flightDuration;               // 0 → 1 over the flight
        Vector3 above = land + Vector3.up * 12f;
        Vector3 pos;
        if (t < 0.85f) { float s = t / 0.85f; pos = Vector3.Lerp(startPos, above, 1f - (1f - s) * (1f - s)); } // cruise, ease out
        else pos = Vector3.Lerp(above, land, Mathf.SmoothStep(0f, 1f, (t - 0.85f) / 0.15f));              // descend
        if (remaining <= 0f) pos = land;

        Vector3 vel = pos - transform.position;
        transform.position = pos;
        vel.y = 0;
        if (vel.sqrMagnitude > 0.0004f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(vel) * Quaternion.Euler(12f, 0, 0), Time.deltaTime * 2f);
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, transform.eulerAngles.y, 0), Time.deltaTime * 2f);

        if (remaining <= 0f && !Landed)
        {
            Landed = true;
            MissionUI.I?.Flash("Helicopter on the pad — everyone aboard!", new Color(0.6f, 1f, 0.6f), 4f);
        }

        if (Landed) TryComplete();
    }

    void TryComplete()
    {
        var gm = GameManager.I;
        if (gm == null || !gm.IsPlaying || gm.extractionZone == null) return;
        if (!gm.extractionZone.Contains(gm.player.transform.position)) return;
        if (!gm.AllLivingHostagesInZone(out _, out _)) return;
        gm.Complete();
    }
}
