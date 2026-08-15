using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// DEV TOOL — disables the pixelation pipeline (camera renders straight to
/// screen instead of through the low-res RenderTexture + RawImage canvas).
/// Drop into any scene: with Start Disabled ON the scene runs clean from the
/// first frame. P toggles it live so you can compare looks.
/// Works generically: finds cameras with a target texture and the RawImage(s)
/// displaying that texture — no hard references to the Pixelizer prefab.
/// </summary>
public class PixelizerToggle : MonoBehaviour
{
    [Tooltip("Disable pixelation immediately on scene start.")]
    public bool startDisabled = true;

    [Tooltip("Key that toggles pixelation on/off at runtime.")]
    public Key toggleKey = Key.P;

    private bool _pixelated = true;
    private Camera        _cam;
    private RenderTexture _rt;
    private readonly System.Collections.Generic.List<GameObject> _pixelCanvases
        = new System.Collections.Generic.List<GameObject>();

    void Start()
    {
        // Find the camera that renders into a texture (the pixel pipeline).
        foreach (Camera c in Camera.allCameras)
        {
            if (c.targetTexture != null)
            {
                _cam = c;
                _rt  = c.targetTexture;
                break;
            }
        }

        // Find every RawImage showing that texture (the pixel display canvas).
        if (_rt != null)
        {
            foreach (RawImage img in FindObjectsByType<RawImage>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (img.texture == _rt)
                    _pixelCanvases.Add(img.gameObject);
            }
        }

        if (_cam == null)
        {
            Debug.LogWarning("[PixelizerToggle] No camera with a target texture found — nothing to toggle.");
            enabled = false;
            return;
        }

        if (startDisabled) SetPixelated(false);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetPixelated(!_pixelated);
    }

    private void SetPixelated(bool on)
    {
        _pixelated = on;
        if (_cam != null) _cam.targetTexture = on ? _rt : null;
        foreach (var go in _pixelCanvases)
            if (go != null) go.SetActive(on);

        Debug.Log($"[PixelizerToggle] Pixelation {(on ? "ON" : "OFF")}.");
    }
}
