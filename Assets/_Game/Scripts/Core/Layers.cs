using UnityEngine;

public static class Layers
{
    public static int Player => LayerMask.NameToLayer("Player");
    public static int Enemy => LayerMask.NameToLayer("Enemy");
    public static int Hostage => LayerMask.NameToLayer("Hostage");
    public static int Environment => LayerMask.NameToLayer("Environment");
    public static int Interactable => LayerMask.NameToLayer("Interactable");
    public static int Viewmodel => LayerMask.NameToLayer("Viewmodel");

    // What the player's bullets can hit.
    public static int PlayerShotMask => LayerMask.GetMask("Default", "Environment", "Enemy", "Hostage", "Interactable");
    // What guard bullets can hit (not other guards).
    public static int GuardShotMask => LayerMask.GetMask("Default", "Environment", "Player", "Hostage", "Interactable");
    // What blocks sight and sound.
    public static int OccluderMask => LayerMask.GetMask("Default", "Environment");
    public static int InteractMask => LayerMask.GetMask("Default", "Environment", "Hostage", "Interactable");

    public static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }
}
