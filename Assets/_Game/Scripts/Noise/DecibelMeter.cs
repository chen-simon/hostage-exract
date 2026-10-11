using UnityEngine;
using UnityEngine.UI;

// Vertical HUD bar showing the player's current noise (peak-hold, decays ~40 dB/s).
public class DecibelMeter : MonoBehaviour
{
    public float minDb = 10f;
    public float maxDb = 90f;
    public float decayPerSecond = 40f;

    Image fill, bg;
    RectTransform barRt;
    Text valueText, label;
    float height;

    public void Build(MissionUI ui, RectTransform rt)
    {
        height = rt.sizeDelta.y - 60f;
        bg = ui.MakeImage("Bar", rt, new Vector2(0, 0), new Vector2(20, 30 + height / 2), new Vector2(40, height), new Color(0, 0, 0, 0.55f));
        barRt = bg.rectTransform;
        fill = ui.MakeImage("Fill", barRt, new Vector2(0.5f, 0), Vector2.zero, new Vector2(40, 0), Color.green);
        fill.rectTransform.pivot = new Vector2(0.5f, 0);

        float limitY = Mathf.InverseLerp(minDb, maxDb, NoiseSystem.LimitDb) * height;
        var line = ui.MakeImage("Limit", barRt, new Vector2(0.5f, 0), new Vector2(6, limitY), new Vector2(56, 4), new Color(1f, 0.15f, 0.1f));
        var limitLabel = ui.MakeText("LimitLabel", new Vector2(0, 0), new Vector2(48, limitY - 12), new Vector2(80, 24), 18, TextAnchor.MiddleLeft, barRt);
        limitLabel.text = $"{NoiseSystem.LimitDb:0}";
        limitLabel.color = new Color(1f, 0.4f, 0.3f);

        valueText = ui.MakeText("Value", new Vector2(0, 0), new Vector2(0, 0), new Vector2(120, 26), 22, TextAnchor.LowerLeft, rt);
        label = ui.MakeText("Label", new Vector2(0, 1), new Vector2(0, 0), new Vector2(120, 26), 20, TextAnchor.UpperLeft, rt);
        label.text = "NOISE dB";
    }

    void Update()
    {
        if (fill == null) return;
        NoiseSystem.PlayerPeakDb = Mathf.Max(0f, NoiseSystem.PlayerPeakDb - decayPerSecond * Time.deltaTime);
        float db = NoiseSystem.PlayerPeakDb;
        float t = Mathf.InverseLerp(minDb, maxDb, db);
        fill.rectTransform.sizeDelta = new Vector2(40, t * height);

        bool over = db >= NoiseSystem.LimitDb;
        Color c = db < 40f ? new Color(0.3f, 0.9f, 0.4f) : (db < NoiseSystem.LimitDb ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.2f, 0.15f));
        if (over && Mathf.Repeat(Time.time * 8f, 1f) < 0.5f) c = Color.white;
        fill.color = c;
        valueText.text = db < minDb ? "-- dB" : $"{db:0} dB";
        valueText.color = over ? new Color(1f, 0.3f, 0.2f) : Color.white;
    }
}
