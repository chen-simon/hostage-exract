using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Builds the whole HUD in code. Spaghetti by design: one place for all on-screen text.
public class MissionUI : MonoBehaviour
{
    public static MissionUI I { get; private set; }

    Font font;
    RectTransform root;
    Text objective, counter, ammo, weaponName, prompt, flash, alarmBanner, crosshair, hitMarker;
    Image promptBarBg, promptBarFill;
    GameObject endPanel;
    Text endTitle, endStats;
    float flashUntil, hitMarkerUntil;

    void Awake()
    {
        I = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = canvasGo.GetComponent<RectTransform>();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        objective = MakeText("Objective", new Vector2(0, 1), new Vector2(40, -36), new Vector2(900, 40), 30, TextAnchor.UpperLeft);
        counter = MakeText("Counter", new Vector2(0, 1), new Vector2(40, -80), new Vector2(900, 34), 24, TextAnchor.UpperLeft);
        counter.color = new Color(0.85f, 0.85f, 0.8f);

        alarmBanner = MakeText("Alarm", new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(1000, 50), 38, TextAnchor.UpperCenter);
        alarmBanner.text = "! FACILITY ALARM — GUARDS ALERTED !";
        alarmBanner.color = new Color(1f, 0.2f, 0.15f);
        alarmBanner.gameObject.SetActive(false);

        flash = MakeText("Flash", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1200, 40), 28, TextAnchor.UpperCenter);

        crosshair = MakeText("Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60), 32, TextAnchor.MiddleCenter);
        crosshair.text = "+";
        crosshair.color = new Color(1, 1, 1, 0.8f);
        hitMarker = MakeText("HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 80), 44, TextAnchor.MiddleCenter);
        hitMarker.text = "×";
        hitMarker.gameObject.SetActive(false);

        prompt = MakeText("Prompt", new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(900, 40), 28, TextAnchor.MiddleCenter);
        promptBarBg = MakeImage("PromptBar", root, new Vector2(0.5f, 0.5f), new Vector2(0, -145), new Vector2(240, 10), new Color(0, 0, 0, 0.6f));
        promptBarFill = MakeImage("Fill", promptBarBg.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 10), new Color(0.9f, 0.85f, 0.4f));
        promptBarFill.rectTransform.pivot = new Vector2(0, 0.5f);

        ammo = MakeText("Ammo", new Vector2(1, 0), new Vector2(-40, 40), new Vector2(400, 60), 48, TextAnchor.LowerRight);
        weaponName = MakeText("Weapon", new Vector2(1, 0), new Vector2(-40, 100), new Vector2(400, 34), 24, TextAnchor.LowerRight);
        weaponName.color = new Color(0.85f, 0.85f, 0.8f);

        var meterGo = new GameObject("DecibelMeter", typeof(RectTransform));
        meterGo.transform.SetParent(root, false);
        var meterRt = meterGo.GetComponent<RectTransform>();
        meterRt.anchorMin = meterRt.anchorMax = new Vector2(0, 0.5f);
        meterRt.pivot = new Vector2(0, 0.5f);
        meterRt.anchoredPosition = new Vector2(40, -40);
        meterRt.sizeDelta = new Vector2(120, 420);
        meterGo.AddComponent<DecibelMeter>().Build(this, meterRt);

        BuildEndPanel();
    }

    void BuildEndPanel()
    {
        var bg = MakeImage("EndPanel", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000), new Color(0, 0, 0, 0.75f));
        endPanel = bg.gameObject;
        endTitle = MakeText("Title", new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1200, 90), 72, TextAnchor.MiddleCenter, bg.rectTransform);
        endStats = MakeText("Stats", new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1000, 240), 30, TextAnchor.MiddleCenter, bg.rectTransform);
        var btnImg = MakeImage("Restart", bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(320, 70), new Color(0.75f, 0.65f, 0.4f));
        var btn = btnImg.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => GameManager.I.Restart());
        var label = MakeText("Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 70), 32, TextAnchor.MiddleCenter, btnImg.rectTransform);
        label.text = "RESTART MISSION";
        label.color = Color.black;
        endPanel.SetActive(false);
    }

    public Text MakeText(string name, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, RectTransform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        go.transform.SetParent(parent != null ? parent : root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        go.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
        return t;
    }

    public Image MakeImage(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    public void Flash(string msg, Color color, float duration = 3f)
    {
        flash.text = msg;
        flash.color = color;
        flashUntil = Time.time + duration;
    }

    public void ShowHitMarker(bool kill)
    {
        hitMarker.color = kill ? new Color(1f, 0.25f, 0.2f) : Color.white;
        hitMarkerUntil = Time.time + 0.15f;
    }

    public void SetPrompt(string text, float progress, bool usable)
    {
        prompt.text = text ?? "";
        prompt.color = usable ? Color.white : new Color(1f, 0.7f, 0.5f);
        promptBarBg.gameObject.SetActive(progress > 0f);
        promptBarFill.rectTransform.sizeDelta = new Vector2(240 * Mathf.Clamp01(progress), 10);
    }

    public void ShowEndScreen(bool success)
    {
        var gm = GameManager.I;
        endPanel.SetActive(true);
        endTitle.text = success ? "MISSION COMPLETE" : "MISSION FAILED";
        endTitle.color = success ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.35f, 0.3f);
        float t = gm.EndTime - gm.StartTime;
        int saved = success ? gm.HostagesAlive : 0;
        endStats.text =
            $"{gm.EndReason}\n\n" +
            $"Time  {(int)(t / 60)}:{(int)(t % 60):00}\n" +
            $"Hostiles neutralized  {gm.Kills}\n" +
            $"Approach  {(gm.AlarmRaised ? "LOUD" : "STEALTH")}\n" +
            $"Hostages saved  {saved}/{gm.HostageTotal}   (lost {gm.HostagesDead})";
    }

    void Update()
    {
        var gm = GameManager.I;
        if (gm == null) return;

        objective.text = gm.IsPlaying ? gm.ObjectiveText() : "";
        counter.text = $"Freed {gm.HostagesFreed}/{gm.HostageTotal}  ·  Lost {gm.HostagesDead}/{gm.maxHostageDeaths}";

        alarmBanner.gameObject.SetActive(gm.AlarmRaised && gm.IsPlaying && Mathf.Repeat(Time.time, 1f) < 0.7f);
        flash.gameObject.SetActive(Time.time < flashUntil);
        hitMarker.gameObject.SetActive(Time.time < hitMarkerUntil);

        var sw = gm.player != null ? gm.player.GetComponent<WeaponSwitcher>() : null;
        var w = sw != null ? sw.Current : null;
        if (w != null && w.data != null)
        {
            weaponName.text = w.data.weaponName;
            ammo.text = w.IsReloading ? "RELOADING" : $"{w.AmmoInMag} / {w.Reserve}";
            ammo.color = w.AmmoInMag == 0 ? new Color(1f, 0.4f, 0.3f) : Color.white;
        }
        crosshair.gameObject.SetActive(gm.IsPlaying);
        if (!gm.IsPlaying) SetPrompt("", 0, true);
    }
}
