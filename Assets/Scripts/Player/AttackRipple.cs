using UnityEngine;

/// <summary>
/// Lightweight visual impact ripples for player attacks. Three flavors:
/// </summary>
public class AttackRipple : MonoBehaviour
{
    private float    _lifetime;
    private float    _elapsed;
    private float    _expandAmount;       // how much to grow over lifetime, world units
    private float    _startRadius;
    private Color    _startColor;
    private Material _mat;

    // ── Public static API — spawn from combat scripts ──

    /// <summary>
    /// Spawns a full 360° ring at <paramref name="position"/> that expands
    /// outward by <paramref name="expandAmount"/> over <paramref name="lifetime"/>
    /// seconds, fading from full alpha to 0.
    /// </summary>
    public static void SpawnRing(Vector3 position, float radius, Color color,
                                 float lifetime = 0.35f, float expandAmount = 0.6f,
                                 float thickness = 0.08f, int segments = 48)
    {
        var go = new GameObject("AttackRipple_Ring");
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;

        var ripple = go.AddComponent<AttackRipple>();
        ripple.Setup(color, lifetime, expandAmount, radius);
        ripple.SetMesh(BuildRingMesh(radius, thickness, segments));
    }

    /// <summary>
    /// Spawns an arc of a ring centered on <paramref name="position"/>,
    /// pointed along <paramref name="forward"/>, covering <paramref name="angleDeg"/>
    /// degrees of sweep. Used for cone-shaped attacks.
    /// </summary>
    public static void SpawnArc(Vector3 position, Vector3 forward, float radius,
                                float angleDeg, Color color,
                                float lifetime = 0.3f, float expandAmount = 0.4f,
                                float thickness = 0.08f, int segments = 32)
    {
        var go = new GameObject("AttackRipple_Arc");
        go.transform.position = position;
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
        go.transform.rotation = Quaternion.LookRotation(flatForward.normalized);

        var ripple = go.AddComponent<AttackRipple>();
        ripple.Setup(color, lifetime, expandAmount, radius);
        ripple.SetMesh(BuildArcMesh(radius, angleDeg, thickness, segments));
    }

    /// <summary>
    /// Spawns a brief disc flash at <paramref name="position"/>, growing and
    /// fading. Used for arrow impacts.
    /// </summary>
    public static void SpawnFlash(Vector3 position, float radius, Color color,
                                  float lifetime = 0.15f, float expandAmount = 0.3f,
                                  int segments = 24)
    {
        var go = new GameObject("AttackFlash");
        go.transform.position = position;

        // Face the camera so the flash always reads clearly
        Camera cam = Camera.main;
        if (cam != null)
            go.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        var ripple = go.AddComponent<AttackRipple>();
        ripple.Setup(color, lifetime, expandAmount, radius);
        ripple.SetMesh(BuildDiscMesh(radius, segments));
    }

    // ── Setup + animation ──

    private void Setup(Color color, float lifetime, float expandAmount, float startRadius)
    {
        _lifetime     = Mathf.Max(0.001f, lifetime);
        _elapsed      = 0f;
        _expandAmount = expandAmount;
        _startRadius  = Mathf.Max(0.0001f, startRadius);
        _startColor   = color;

        var renderer  = gameObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows    = false;

        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Unlit/Color");

        _mat = new Material(shader) { color = color };
        _mat.renderQueue = 3000;
        renderer.material = _mat;
    }

    private void SetMesh(Mesh mesh)
    {
        var filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _lifetime);

        // Scale outward smoothly
        float scale = 1f + (_expandAmount / _startRadius) * Mathf.SmoothStep(0f, 1f, t);
        transform.localScale = new Vector3(scale, 1f, scale);

        // Fade alpha
        Color c = _startColor;
        c.a = _startColor.a * (1f - t);
        _mat.color = c;

        if (_elapsed >= _lifetime)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    // ── Mesh builders ──

    private static Mesh BuildRingMesh(float radius, float thickness, int segments)
    {
        float innerR = radius - thickness * 0.5f;
        float outerR = radius + thickness * 0.5f;

        var verts = new Vector3[segments * 2];
        var tris  = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);
            verts[i * 2]     = new Vector3(cos * innerR, 0f, sin * innerR);
            verts[i * 2 + 1] = new Vector3(cos * outerR, 0f, sin * outerR);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int b    = i * 6;
            int iA = i * 2, iB = i * 2 + 1;
            int nA = next * 2, nB = next * 2 + 1;

            // Two triangles per segment, double-sided so the ripple is visible
            // from above and below.
            tris[b]     = iA; tris[b + 1] = nA; tris[b + 2] = iB;
            tris[b + 3] = iB; tris[b + 4] = nA; tris[b + 5] = nB;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildArcMesh(float radius, float angleDeg, float thickness, int segments)
    {
        float halfAngle = angleDeg * 0.5f * Mathf.Deg2Rad;
        float innerR    = radius - thickness * 0.5f;
        float outerR    = radius + thickness * 0.5f;

        var verts = new Vector3[(segments + 1) * 2];
        var tris  = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t     = (float)i / segments;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);
            // Forward is +Z, so we sweep around the Y axis: x = sin, z = cos
            verts[i * 2]     = new Vector3(sin * innerR, 0f, cos * innerR);
            verts[i * 2 + 1] = new Vector3(sin * outerR, 0f, cos * outerR);
        }

        for (int i = 0; i < segments; i++)
        {
            int b = i * 6;
            int iA = i * 2, iB = i * 2 + 1;
            int nA = (i + 1) * 2, nB = (i + 1) * 2 + 1;

            tris[b]     = iA; tris[b + 1] = nA; tris[b + 2] = iB;
            tris[b + 3] = iB; tris[b + 4] = nA; tris[b + 5] = nB;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildDiscMesh(float radius, int segments)
    {
        var verts = new Vector3[segments + 1];
        var tris  = new int[segments * 3];
        verts[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments + 1;
            int b    = i * 3;
            tris[b]     = 0;
            tris[b + 1] = i + 1;
            tris[b + 2] = next;
        }

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
