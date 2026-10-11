using UnityEngine;

// Look at something + hold E. Drives the HUD prompt.
public class PlayerInteract : MonoBehaviour
{
    public float range = 2.8f;

    PlayerController pc;
    IInteractable current;
    float progress;
    bool needsRelease;

    void Awake() => pc = GetComponent<PlayerController>();

    void Update()
    {
        var ui = MissionUI.I;
        if (!pc.Alive || GameManager.I == null || !GameManager.I.IsPlaying) { current = null; return; }

        IInteractable target = null;
        if (Physics.Raycast(pc.head.position, pc.head.forward, out var hit, range, Layers.InteractMask, QueryTriggerInteraction.Ignore))
            target = hit.collider.GetComponentInParent<IInteractable>();

        if (target != current) { current = target; progress = 0f; }
        if (!GameInput.InteractHeld) needsRelease = false;

        if (current == null)
        {
            ui?.SetPrompt("", 0f, true);
            return;
        }

        string text = current.GetPrompt(out bool usable);
        if (usable && GameInput.InteractHeld && !needsRelease)
        {
            progress += Time.deltaTime / Mathf.Max(0.01f, current.HoldTime);
            if (progress >= 1f)
            {
                current.Interact();
                progress = 0f;
                needsRelease = true;
            }
        }
        else progress = 0f;

        ui?.SetPrompt(usable ? $"[Hold E] {text}" : text, progress, usable);
    }
}
