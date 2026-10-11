using UnityEngine;

// Short-lived bullet tracer line + impact markers.
public class Tracer : MonoBehaviour
{
    LineRenderer line;
    float life, maxLife;
    Color color;

    public static void Spawn(Vector3 from, Vector3 to, Color color, float life = 0.06f)
    {
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = 0.025f;
        lr.endWidth = 0.012f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (GameManager.I != null && GameManager.I.tracerMaterial != null) lr.sharedMaterial = GameManager.I.tracerMaterial;
        lr.startColor = lr.endColor = color;
        var t = go.AddComponent<Tracer>();
        t.line = lr;
        t.life = t.maxLife = life;
        t.color = color;
    }

    public static void Impact(RaycastHit hit)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Impact";
        go.transform.position = hit.point + hit.normal * 0.01f;
        go.transform.localScale = Vector3.one * 0.07f;
        go.transform.SetParent(hit.collider.transform, true);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (GameManager.I != null && GameManager.I.impactMaterial != null) r.sharedMaterial = GameManager.I.impactMaterial;
        Destroy(go, 20f);
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        var c = color; c.a *= life / maxLife;
        line.startColor = line.endColor = c;
    }
}
