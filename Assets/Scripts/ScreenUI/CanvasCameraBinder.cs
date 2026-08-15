using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Aggressive self-healing for a Canvas's <c>worldCamera</c> reference and
/// (optionally) for the Canvas being silently disabled.
/// </summary>
[RequireComponent(typeof(Canvas))]
[DefaultExecutionOrder(-50)]   // run before normal UI scripts
public class CanvasCameraBinder : MonoBehaviour
{
    [Tooltip("Tag of the camera to bind to. Defaults to 'MainCamera' (i.e. Camera.main).")]
    public string targetCameraTag = "MainCamera";

    [Tooltip("Negative = don't override. If positive, sets Canvas.planeDistance " +
             "each rebind so a wrong scene value can't push the UI behind the camera.")]
    public float planeDistanceOverride = -1f;

    [Tooltip("If the Canvas component gets disabled by another script, " +
             "re-enable it every frame so the UI never disappears.")]
    public bool keepCanvasEnabled = true;

    [Tooltip("If a CanvasGroup is on THIS GameObject and its alpha drops, " +
             "force it back to 1 every frame. Useful if a fader script " +
             "stops mid-fade and leaves the UI invisible.")]
    public bool forceCanvasGroupAlpha = false;

    [Tooltip("Log every time the binder heals a missing or broken reference. " +
             "Turn off in shipping builds — turn ON while you're chasing this bug.")]
    public bool verbose = false;

    private Canvas      _canvas;
    private CanvasGroup _localGroup;

    void Awake()
    {
        _canvas     = GetComponent<Canvas>();
        _localGroup = GetComponent<CanvasGroup>();
        TryBind("Awake");
    }

    void OnEnable()
    {
        TryBind("OnEnable");
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void LateUpdate()
    {
        // Self-heal every frame. Cheap — just a couple of null checks unless we
        // actually need to rebind.
        bool needsRebind =
            _canvas != null
            && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            && (
                _canvas.worldCamera == null
                || !_canvas.worldCamera.isActiveAndEnabled
                || _canvas.worldCamera.gameObject.scene != SceneManager.GetActiveScene()
            );

        if (needsRebind) TryBind("LateUpdate-heal");

        // Force Canvas back on if something silently disabled it
        if (keepCanvasEnabled && _canvas != null && !_canvas.enabled)
        {
            _canvas.enabled = true;
            if (verbose) Debug.Log($"[CanvasCameraBinder] {gameObject.name} re-enabled Canvas.");
        }

        // Optional CanvasGroup alpha clamp
        if (forceCanvasGroupAlpha && _localGroup != null && _localGroup.alpha < 0.999f)
        {
            _localGroup.alpha = 1f;
            if (verbose) Debug.Log($"[CanvasCameraBinder] {gameObject.name} restored CanvasGroup alpha.");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryBind($"sceneLoaded[{scene.name}]");
    }

    private void TryBind(string source)
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas == null) return;

        // Overlay canvases don't use worldCamera — skip silently.
        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay) return;

        Camera cam = FindTargetCamera();
        if (cam == null)
        {
            if (verbose)
                Debug.LogWarning($"[CanvasCameraBinder] {gameObject.name} could not find " +
                                 $"a camera tagged '{targetCameraTag}' (source: {source}).");
            return;
        }

        if (_canvas.worldCamera != cam)
        {
            _canvas.worldCamera = cam;
            if (verbose)
                Debug.Log($"[CanvasCameraBinder] {gameObject.name} → bound to " +
                          $"camera '{cam.name}' in scene '{cam.gameObject.scene.name}' " +
                          $"(source: {source}).");
        }

        if (planeDistanceOverride > 0f)
            _canvas.planeDistance = planeDistanceOverride;
    }

    private Camera FindTargetCamera()
    {
        // Walk all enabled cameras and prefer one in the currently-active scene
        // (avoids picking a leftover DontDestroyOnLoad camera).
        Camera best      = null;
        Camera[] cams    = Camera.allCameras;
        Scene  active    = SceneManager.GetActiveScene();
        bool   useDefault = string.IsNullOrEmpty(targetCameraTag) || targetCameraTag == "MainCamera";

        foreach (var c in cams)
        {
            if (c == null || !c.isActiveAndEnabled) continue;

            // For the default "MainCamera" tag, use CompareTag.
            // For a custom tag, also CompareTag.
            if (!c.CompareTag(useDefault ? "MainCamera" : targetCameraTag)) continue;

            // Best match: in the active scene
            if (c.gameObject.scene == active) return c;

            // Fallback: any tagged camera, anywhere
            best = c;
        }

        return best ?? (useDefault ? Camera.main : null);
    }
}
