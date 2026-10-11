using UnityEngine;
using UnityEngine.InputSystem;

// Thin wrapper over the project-wide InputSystem_Actions asset ("Player" map).
public static class GameInput
{
    static InputAction move, look, attack, interact, crouch, jump, sprint, reload, weapon1, weapon2, next, previous;
    static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => initialized = false;

    static void Init()
    {
        if (initialized) return;
        initialized = true;
        var asset = InputSystem.actions;
        if (asset == null) { Debug.LogError("No project-wide input actions assigned."); return; }
        asset.FindActionMap("Player", true).Enable();
        move = asset.FindAction("Player/Move");
        look = asset.FindAction("Player/Look");
        attack = asset.FindAction("Player/Attack");
        interact = asset.FindAction("Player/Interact");
        crouch = asset.FindAction("Player/Crouch");
        jump = asset.FindAction("Player/Jump");
        sprint = asset.FindAction("Player/Sprint");
        reload = asset.FindAction("Player/Reload");
        weapon1 = asset.FindAction("Player/Weapon1");
        weapon2 = asset.FindAction("Player/Weapon2");
        next = asset.FindAction("Player/Next");
        previous = asset.FindAction("Player/Previous");
    }

    public static Vector2 Move { get { Init(); return move != null ? move.ReadValue<Vector2>() : Vector2.zero; } }
    public static Vector2 Look { get { Init(); return look != null ? look.ReadValue<Vector2>() : Vector2.zero; } }
    public static bool FireHeld { get { Init(); return attack != null && attack.IsPressed(); } }
    public static bool FirePressed { get { Init(); return attack != null && attack.WasPressedThisFrame(); } }
    public static bool InteractHeld { get { Init(); return interact != null && interact.IsPressed(); } }
    public static bool CrouchPressed { get { Init(); return crouch != null && crouch.WasPressedThisFrame(); } }
    public static bool JumpPressed { get { Init(); return jump != null && jump.WasPressedThisFrame(); } }
    public static bool SprintHeld { get { Init(); return sprint != null && sprint.IsPressed(); } }
    public static bool ReloadPressed { get { Init(); return reload != null && reload.WasPressedThisFrame(); } }
    public static bool Weapon1Pressed { get { Init(); return weapon1 != null && weapon1.WasPressedThisFrame(); } }
    public static bool Weapon2Pressed { get { Init(); return weapon2 != null && weapon2.WasPressedThisFrame(); } }
    public static bool NextPressed { get { Init(); return next != null && next.WasPressedThisFrame(); } }
    public static bool PreviousPressed { get { Init(); return previous != null && previous.WasPressedThisFrame(); } }
    public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
    public static bool EscapePressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    public static bool ClickPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
}
