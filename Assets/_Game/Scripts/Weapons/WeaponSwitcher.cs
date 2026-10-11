using UnityEngine;

public class WeaponSwitcher : MonoBehaviour
{
    public Weapon[] weapons;
    public int startIndex = 0;

    public Weapon Current => weapons != null && index >= 0 && index < weapons.Length ? weapons[index] : null;
    int index = -1;

    void Start() => Select(startIndex);

    void Update()
    {
        if (GameManager.I == null || !GameManager.I.IsPlaying) return;
        if (GameInput.Weapon1Pressed) Select(0);
        else if (GameInput.Weapon2Pressed) Select(1);
        else
        {
            float s = GameInput.Scroll;
            if (s > 0.1f) Select((index + 1) % weapons.Length);
            else if (s < -0.1f) Select((index - 1 + weapons.Length) % weapons.Length);
        }
    }

    public void Select(int i)
    {
        if (weapons == null || i < 0 || i >= weapons.Length || i == index) return;
        index = i;
        for (int k = 0; k < weapons.Length; k++) weapons[k].gameObject.SetActive(k == index);
    }
}
