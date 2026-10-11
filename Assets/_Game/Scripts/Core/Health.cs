using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("Seconds without damage before regen starts. 0 = no regen.")]
    public float regenDelay = 0f;
    public float regenRate = 20f;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Fraction => Current / maxHealth;
    public float LastDamageTime { get; private set; } = -100f;

    public event Action<float, Vector3> Damaged; // amount, direction the shot travelled
    public event Action Died;

    void Awake() => Current = maxHealth;

    void Update()
    {
        if (IsDead || regenDelay <= 0f || Current >= maxHealth) return;
        if (Time.time - LastDamageTime > regenDelay)
            Current = Mathf.Min(maxHealth, Current + regenRate * Time.deltaTime);
    }

    public void Damage(float amount, Vector3 direction)
    {
        if (IsDead || amount <= 0f) return;
        Current -= amount;
        LastDamageTime = Time.time;
        Damaged?.Invoke(amount, direction);
        if (Current <= 0f)
        {
            Current = 0f;
            IsDead = true;
            Died?.Invoke();
        }
    }
}
