using UnityEngine;

// Roof radio. Only usable once every living hostage is freed and inside the extraction zone.
public class Radio : MonoBehaviour, IInteractable
{
    public float HoldTime => 1f;

    public string GetPrompt(out bool usable)
    {
        var gm = GameManager.I;
        usable = false;
        if (gm == null) return "";
        if (gm.RadioCalled) return "Helicopter inbound";
        bool ready = gm.AllLivingHostagesInZone(out int inside, out int alive);
        if (!ready) return $"Radio: bring all hostages to the extraction zone ({inside}/{alive})";
        usable = true;
        return "Call extraction helicopter";
    }

    public void Interact()
    {
        var gm = GameManager.I;
        if (gm == null || gm.RadioCalled) return;
        gm.RadioCalled = true;
        if (gm.helicopter != null) gm.helicopter.Call();
        MissionUI.I?.Flash("\"Overlord, this is Dry Wire. Package secure, requesting exfil.\"", new Color(0.7f, 0.9f, 1f), 4f);
        if (AlarmSystem.I != null && gm.extractionZone != null) AlarmSystem.I.PushTo(gm.extractionZone.transform.position);
    }
}
