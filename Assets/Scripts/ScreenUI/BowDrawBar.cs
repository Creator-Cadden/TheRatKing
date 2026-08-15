using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen draw bar for the bow: shows only while drawing, fills with the
/// charge fraction, and turns gold + pulses at max draw. Put this on a UI object
/// with a Filled Image and assign the fill. Auto-finds the player's BowController.
/// Hides via a CanvasGroup (NOT SetActive) so its Update keeps running.
/// </summary>
public class BowDrawBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave null to auto-find on the Player.")]
    public BowController bow;
    [Tooltip("Filled Image (Image Type = Filled, Radial 360).")]
    public Image fillImage;
    [Tooltip("Root shown/hidden with the draw. Defaults to this GameObject.")]
    public GameObject root;

    [Header("Colours")]
    public Color drawingColour = new Color(1f, 1f, 1f, 0.9f);
    public Color maxColour     = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Full-draw pulse")]
    public float pulseSpeed  = 9f;
    public float pulseAmount = 0.08f;

    private CanvasGroup   _cg;
    private RectTransform _rt;
    private Vector3       _baseScale = Vector3.one;

    void Start()
    {
        TryFindBow();

        if (root == null) root = gameObject;

        // Hide via CanvasGroup alpha so the GameObject stays ACTIVE and Update runs.
        _cg = root.GetComponent<CanvasGroup>();
        if (_cg == null) _cg = root.AddComponent<CanvasGroup>();

        _rt = root.GetComponent<RectTransform>();
        if (_rt != null) _baseScale = _rt.localScale;

        SetVisible(false);
    }

    void Update()
    {
        if (bow == null) TryFindBow();          // lazy — player may spawn after us
        if (bow == null) { SetVisible(false); return; }

        bool drawing = bow.IsCharging;
        SetVisible(drawing);
        if (!drawing) return;

        float f     = bow.CurrentChargeFraction;
        bool  atMax = f >= 0.999f;

        if (fillImage != null)
        {
            fillImage.fillAmount = f;
            fillImage.color      = atMax ? maxColour : drawingColour;
        }

        if (_rt != null)
        {
            float pulse = atMax ? 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount : 1f;
            _rt.localScale = _baseScale * pulse;
        }
    }

    private void TryFindBow()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) bow = player.GetComponent<BowController>();
    }

    private void SetVisible(bool v)
    {
        if (_cg == null) return;
        _cg.alpha          = v ? 1f : 0f;
        _cg.blocksRaycasts = v;
    }
}
