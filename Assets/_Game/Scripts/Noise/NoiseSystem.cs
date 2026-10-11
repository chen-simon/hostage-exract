using UnityEngine;

// Static noise broadcaster. Every sound in the mission goes through Emit().
public static class NoiseSystem
{
    public const float WallLoss = 15f;        // dB lost per wall between source and listener
    public const float LimitDb = 65f;         // HUD red line: player noise above this is "heard"
    public const float LimitRadius = 25f;     // radius in which over-limit noise sends guards to Investigate

    // Loudest player-made noise recently (peak-hold, decayed by DecibelMeter).
    public static float PlayerPeakDb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PlayerPeakDb = 0f;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    public static void Emit(Vector3 position, float db, GameObject source, bool fromPlayer)
    {
        if (fromPlayer) PlayerPeakDb = Mathf.Max(PlayerPeakDb, db);

        bool overLimit = fromPlayer && db >= LimitDb;
        foreach (var guard in Guard.All)
        {
            if (guard == null || guard.IsDead || guard.gameObject == source) continue;
            var hearing = guard.Hearing;
            if (hearing == null) continue;

            Vector3 ear = guard.EyePosition;
            float dist = Vector3.Distance(position, ear);
            float received = ReceivedLevel(position, ear, db);

            if (overLimit && dist <= LimitRadius)
                hearing.OnNoise(position, Mathf.Max(received, hearing.threshold), true, fromPlayer);
            else if (received >= hearing.threshold)
                hearing.OnNoise(position, received, false, fromPlayer);
        }
    }

    public static float ReceivedLevel(Vector3 from, Vector3 to, float db)
    {
        float dist = Vector3.Distance(from, to);
        float level = db - 20f * Mathf.Log10(Mathf.Max(dist, 1f));
        if (dist > 0.1f)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.3f, (to - from).normalized, hitBuffer, dist, Layers.OccluderMask, QueryTriggerInteraction.Ignore);
            level -= WallLoss * n;
        }
        return level;
    }
}
