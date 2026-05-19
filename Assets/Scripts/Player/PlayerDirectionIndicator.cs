using UnityEngine;

/// <summary>
/// Attach to an empty child GameObject on the Player.
/// Position that child object at whatever height you want the ring —
/// no code controls the position, the empty object IS the anchor.
///
/// Draws:
///   - A ring that fades out on the back half (opposite the facing direction)
///   - A small triangle pointer just outside the ring on the facing side
///
/// Setup:
///   1. Create empty child on Player, name it "DirectionRing"
///   2. Set its local Y to whatever puts it at ground level (e.g. -0.9)
///   3. Attach this script — MeshFilter + MeshRenderer added automatically
///   4. The ring and triangle rotate with the parent player Y automatically
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PlayerDirectionRing : MonoBehaviour
{
    [Header("Ring")]
    public float ringRadius    = 1.2f;
    public float ringThickness = 0.055f;
    public int   ringSegments  = 64;

    [Header("Triangle Pointer")]
    [Tooltip("How far beyond the ring edge the triangle tip sits.")]
    public float triangleOffset = 0.12f;
    [Tooltip("Half-width of the triangle base (sits on the ring edge).")]
    public float triangleWidth  = 0.12f;
    [Tooltip("How far the triangle tip extends past its base.")]
    public float triangleLength = 0.18f;

    [Header("Colors")]
    [Tooltip("Ring color on the front (facing) side.")]
    public Color ringFrontColor = new Color(1f,   1f,   1f,   0.55f);
    [Tooltip("Ring color on the back side — set alpha low to fade it out.")]
    public Color ringBackColor  = new Color(1f,   1f,   1f,   0.04f);
    [Tooltip("Triangle pointer color.")]
    public Color triangleColor  = new Color(1f,  0.65f, 0.1f, 0.90f);

    // ── Private ──
    private MeshFilter   _ringFilter;
    private MeshRenderer _ringRenderer;

    private GameObject   _triangleGO;
    private MeshFilter   _triFilter;
    private MeshRenderer _triRenderer;

    private Material _ringMat;
    private Material _triMat;

    // ─────────────────────────────────────────

    void Awake()
    {
        _ringFilter   = GetComponent<MeshFilter>();
        _ringRenderer = GetComponent<MeshRenderer>();

        // Triangle is a child so it inherits rotation automatically
        _triangleGO = new GameObject("DirectionTriangle");
        _triangleGO.transform.SetParent(transform, false);
        _triFilter   = _triangleGO.AddComponent<MeshFilter>();
        _triRenderer = _triangleGO.AddComponent<MeshRenderer>();

        _ringMat = CreateMat(ringFrontColor);
        _triMat  = CreateMat(triangleColor);

        _ringRenderer.material = _ringMat;
        _triRenderer.material  = _triMat;

        foreach (var r in new[] { _ringRenderer, _triRenderer })
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
        }
    }

    void Start()
    {
        BuildRing();
        BuildTriangle();
    }

    void LateUpdate()
    {
        // The empty GameObject position is controlled entirely by the Editor —
        // no position code here. Just keep it flat (no pitch/roll).
        // Only override the rotation axes we don't want:
        // preserve Y from the parent player, zero out X and Z tilt.
        float parentY = transform.parent != null
            ? transform.parent.eulerAngles.y
            : transform.eulerAngles.y;

        transform.rotation = Quaternion.Euler(0f, parentY, 0f);

        // Sync colors if tweaked in Inspector at runtime
        _ringMat.color = ringFrontColor;
        _triMat.color  = triangleColor;
    }

    // ─────────────────────────────────────────
    // Ring mesh — vertex colors fade front→back
    // ─────────────────────────────────────────

    private void BuildRing()
    {
        float innerR = ringRadius - ringThickness;
        float outerR = ringRadius;

        int vertCount = ringSegments * 2;
        var verts  = new Vector3[vertCount];
        var colors = new Color[vertCount];
        var tris   = new int[ringSegments * 6];

        for (int i = 0; i < ringSegments; i++)
        {
            // angle 0 = forward (+Z), PI = backward (-Z)
            float angle = (float)i / ringSegments * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);   // X
            float sin   = Mathf.Sin(angle);   // Z

            verts[i * 2]     = new Vector3(cos * innerR, 0f, sin * innerR);
            verts[i * 2 + 1] = new Vector3(cos * outerR, 0f, sin * outerR);

            // sin(angle): +1 at front (Z+), -1 at back (Z-)
            // Remap 0..1 then apply power curve for aggressive back fade.
            // Squaring makes the front stay bright longer and the back
            // drop to invisible much faster. The back quarter (sin < -0.5)
            // clamps to fully transparent.
            float frontness = (sin + 1f) * 0.5f;          // 0 = back, 1 = front
            frontness = Mathf.Pow(frontness, 3f);          // sharp falloff
            frontness = Mathf.Clamp01(frontness);
            Color c = Color.Lerp(ringBackColor, ringFrontColor, frontness);
            colors[i * 2]     = c;
            colors[i * 2 + 1] = c;
        }

        for (int i = 0; i < ringSegments; i++)
        {
            int next  = (i + 1) % ringSegments;
            int b     = i * 6;
            int iA = i * 2, iB = i * 2 + 1;
            int nA = next * 2, nB = next * 2 + 1;

            tris[b]     = iA; tris[b + 1] = nA; tris[b + 2] = iB;
            tris[b + 3] = iB; tris[b + 4] = nA; tris[b + 5] = nB;
        }

        var mesh = new Mesh
        {
            vertices  = verts,
            triangles = tris,
            colors    = colors
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _ringFilter.sharedMesh = mesh;

        // Sprites/Default reads vertex colors — enable it
        _ringMat.enableInstancing = false;
    }

    // ─────────────────────────────────────────
    // Triangle pointer — small, sits just past the ring on the forward side
    // ─────────────────────────────────────────

    private void BuildTriangle()
    {
        float baseZ = ringRadius + triangleOffset;
        float tipZ  = baseZ + triangleLength;

        // Triangle points forward (+Z), base straddles the ring edge
        var verts = new Vector3[]
        {
            new Vector3(-triangleWidth, 0f, baseZ),  // base left
            new Vector3( triangleWidth, 0f, baseZ),  // base right
            new Vector3(0f,             0f, tipZ ),  // tip
        };

        // Double-sided
        var tris = new int[] { 0, 2, 1,  0, 1, 2 };

        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _triFilter.sharedMesh = mesh;
    }

    // ─────────────────────────────────────────

    private static Material CreateMat(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Standard");
        var mat   = new Material(shader);
        mat.color = color;
        mat.renderQueue = 3000;
        return mat;
    }
}