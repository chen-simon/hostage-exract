using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the global post-process volume at runtime and drives the damage vignette.
public class PostFX : MonoBehaviour
{
    public Health playerHealth;
    Vignette vignette;
    ColorAdjustments color;
    float hitPulse;

    void Awake()
    {
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.5f);
        bloom.threshold.Override(1.1f);

        color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.2f);
        color.contrast.Override(8f);
        color.saturation.Override(-8f);
        color.colorFilter.Override(new Color(1f, 0.95f, 0.86f));

        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(18f);

        vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.5f);
        vignette.color.Override(Color.black);
    }

    void Start()
    {
        if (playerHealth == null && GameManager.I != null && GameManager.I.player != null)
            playerHealth = GameManager.I.player.GetComponent<Health>();
        if (playerHealth != null) playerHealth.Damaged += (_, __) => hitPulse = 1f;
    }

    void Update()
    {
        if (playerHealth == null) return;
        hitPulse = Mathf.MoveTowards(hitPulse, 0f, Time.deltaTime * 2.5f);
        float hurt = Mathf.Clamp01(1f - playerHealth.Fraction);
        float red = Mathf.Clamp01(hurt * 1.2f + hitPulse * 0.5f);
        vignette.color.value = Color.Lerp(Color.black, new Color(0.6f, 0f, 0f), red);
        vignette.intensity.value = Mathf.Lerp(0.22f, 0.55f, red);
        color.saturation.value = Mathf.Lerp(-8f, -60f, hurt);
    }
}
