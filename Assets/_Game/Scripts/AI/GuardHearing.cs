using UnityEngine;

// Receives noise from NoiseSystem and forwards it to the Guard's state machine.
public class GuardHearing : MonoBehaviour
{
    public float threshold = 30f;
    public float loudLevel = 55f;   // received level that skips Suspicious and goes straight to Investigate

    Guard guard;

    void Awake() => guard = GetComponent<Guard>();

    public void OnNoise(Vector3 position, float receivedDb, bool overLimit, bool fromPlayer)
    {
        guard.HearNoise(position, receivedDb, overLimit || receivedDb >= loudLevel);
    }
}
