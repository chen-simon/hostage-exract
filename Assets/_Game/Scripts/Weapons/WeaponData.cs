using UnityEngine;

[CreateAssetMenu(menuName = "Hostage/Weapon Data", fileName = "WeaponData")]
public class WeaponData : ScriptableObject
{
    public string weaponName = "Weapon";
    public bool automatic;
    public float damage = 30f;
    [Tooltip("Rounds per minute")] public float fireRate = 400f;
    public int magSize = 12;
    public int reserveAmmo = 48;
    public float reloadTime = 1.8f;
    [Tooltip("Hip spread cone in degrees")] public float spread = 0.8f;
    public float range = 200f;
    [Header("Recoil")]
    public float recoilPitch = 1.2f;
    public float recoilYaw = 0.4f;
    public float impulseForce = 0.4f;
    [Header("Noise")]
    public float noiseDb = 60f;
    public float reloadNoiseDb = 30f;
}
