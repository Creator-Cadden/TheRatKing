using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// The floating "E — Buy from the Ratmonger" prompt. One of these lives in the
/// HUD; it finds the closest <see cref="Interactable"/> in range of the player,
/// pins itself over that object in screen space, and runs the Interact input.
///
/// Put it on a RectTransform inside your SCREEN-SPACE HUD canvas (not a
/// world-space canvas) — screen-space keeps the text crisp and always readable
/// regardless of distance, which matters at Rat King's small character scale.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [Header("References (auto-built if empty)")]
    public RectTransform panel;
    public CanvasGroup   canvasGroup;
    public TMP_Text      keyLabel;
    public TMP_Text      promptLabel;
    public TMP_Text      subLabel;

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Input")]
    [Tooltip("Drag the Player/Interact action here. Falls back to the E key if empty.")]
    public InputActionReference interactAction;

    [Tooltip("Text shown in the key badge when no binding can be read.")]
    public string fallbackKeyText = "E";

    [Header("Targeting")]
    [Tooltip("Prefer whatever the player is FACING when two interactables are equally " +
             "close. Stops the prompt flip-flopping between two adjacent statues.")]
    public bool preferFacing = true;

    [Tooltip("How strongly facing is weighted against distance. Higher = facing matters more.")]
    [Range(0f, 1f)] public float facingWeight = 0.4f;

    [Header("Look")]
    public float fadeSpeed = 12f;
    [Tooltip("Screen-space offset from the target's projected position, in pixels.")]
    public Vector2 screenOffset = new Vector2(0f, 0f);
    [Tooltip("Gentle idle bob, in pixels.")]
    public float bobAmount = 3f;
    public float bobSpeed  = 2.5f;

    [Header("Colours")]
    public Color availableColor   = new Color(0.94f, 0.89f, 0.79f, 1f);
    public Color unavailableColor = new Color(0.61f, 0.55f, 0.47f, 1f);

    // ── Private ───────────────────────────────────────────────────────────────

    private Transform    _player;
    private Camera       _cam;
    private Interactable _target;
    private float        _alpha;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (panel == null) panel = GetComponent<RectTransform>();
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (promptLabel == null) BuildRuntimePrompt();

        canvasGroup.alpha          = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable   = false;
    }

    void OnEnable()
    {
        if (interactAction != null && interactAction.action != null)
        {
            interactAction.action.performed += OnInteractPerformed;
            interactAction.action.Enable();
        }
        RefreshKeyLabel();
    }

    void OnDisable()
    {
        if (interactAction != null && interactAction.action != null)
            interactAction.action.performed -= OnInteractPerformed;

        if (_target != null) { _target.SetFocused(false); _target = null; }
    }

    void Update()
    {
        EnsureRefs();

        Interactable best = FindBestTarget();

        if (best != _target)
        {
            _target?.SetFocused(false);
            _target = best;
            _target?.SetFocused(true);
            if (_target != null) RefreshContent();
        }

        // Fade toward the right visibility.
        float want = _target != null ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, want, Time.deltaTime * fadeSpeed);
        canvasGroup.alpha = _alpha;

        if (_target == null || _alpha <= 0.001f) return;

        RefreshContent();
        PositionOverTarget();

        // Fallback input path when no action is wired.
        if (interactAction == null || interactAction.action == null)
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                DoInteract();
        }
    }

    // ── Targeting ─────────────────────────────────────────────────────────────

    private Interactable FindBestTarget()
    {
        if (_player == null) return null;

        Interactable best      = null;
        float        bestScore = float.MaxValue;

        for (int i = 0; i < Interactable.All.Count; i++)
        {
            Interactable it = Interactable.All[i];
            if (it == null) continue;

            Vector3 toIt = it.transform.position - _player.position;
            float   dist = toIt.magnitude;
            if (dist > it.interactRange) continue;

            // Lower is better. Distance normalised to the object's own range so a
            // long-range shop doesn't always beat a short-range chest.
            float score = dist / Mathf.Max(0.01f, it.interactRange);

            if (preferFacing)
            {
                Vector3 flat = new Vector3(toIt.x, 0f, toIt.z);
                if (flat.sqrMagnitude > 0.001f)
                {
                    // dot: 1 = dead ahead, -1 = behind. Convert to a 0..1 penalty.
                    float dot     = Vector3.Dot(_player.forward, flat.normalized);
                    float penalty = (1f - dot) * 0.5f;
                    score += penalty * facingWeight;
                }
            }

            if (score < bestScore)
            {
                bestScore = score;
                best      = it;
            }
        }
        return best;
    }

    private void DoInteract()
    {
        if (_target == null) return;

        if (!_target.TryInteract())
        {
            // Refused — a tiny shake reads better than nothing happening.
            if (panel != null) StartCoroutine(RefuseShake());
        }
    }

    private System.Collections.IEnumerator RefuseShake()
    {
        Vector2 basePos = panel.anchoredPosition;
        const float duration = 0.22f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float decay = 1f - t / duration;
            panel.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 70f) * 6f * decay, 0f);
            yield return null;
        }
        panel.anchoredPosition = basePos;
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx) => DoInteract();

    // ── Presentation ──────────────────────────────────────────────────────────

    private void RefreshContent()
    {
        if (_target == null) return;

        Color c = _target.available ? availableColor : unavailableColor;

        if (promptLabel != null)
        {
            promptLabel.text  = _target.PromptLine;
            promptLabel.color = c;
        }
        if (keyLabel != null) keyLabel.color = c;

        if (subLabel != null)
        {
            bool hasSub = !string.IsNullOrWhiteSpace(_target.subtext);
            subLabel.gameObject.SetActive(hasSub);
            if (hasSub)
            {
                subLabel.text  = _target.subtext;
                subLabel.color = c;
            }
        }
    }

    private void PositionOverTarget()
    {
        if (_cam == null || panel == null) return;

        Vector3 screen = _cam.WorldToScreenPoint(_target.PromptPosition);
        if (screen.z < 0f)
        {
            canvasGroup.alpha = 0f;   // behind the camera
            return;
        }

        RectTransform parent = panel.parent as RectTransform;
        if (parent == null) return;

        Canvas canvas = panel.GetComponentInParent<Canvas>();
        Camera uiCam  = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screen, uiCam, out Vector2 local))
        {
            float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            panel.anchoredPosition = local + screenOffset + new Vector2(0f, bob);
        }
    }

    private void RefreshKeyLabel()
    {
        if (keyLabel == null) return;

        string text = fallbackKeyText;
        if (interactAction != null && interactAction.action != null)
        {
            string display = interactAction.action.GetBindingDisplayString(
                InputBinding.DisplayStringOptions.DontUseShortDisplayNames);
            if (!string.IsNullOrWhiteSpace(display))
            {
                // "E | Gamepad West" → take the first binding only.
                int pipe = display.IndexOf('|');
                text = (pipe > 0 ? display.Substring(0, pipe) : display).Trim();
            }
        }
        keyLabel.text = text;
    }

    private void EnsureRefs()
    {
        if (_player == null)
        {
            GameObject go = GameObject.FindWithTag(playerTag);
            if (go != null) _player = go.transform;
        }
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
    }

    // ── Runtime construction ──────────────────────────────────────────────────

    private void BuildRuntimePrompt()
    {
        UITheme theme = UITheme.Active;

        panel.sizeDelta = new Vector2(340f, 64f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot     = new Vector2(0.5f, 0.5f);

        // Key badge
        var badgeGo = new GameObject("KeyBadge", typeof(RectTransform), typeof(Image));
        var badgeRt = badgeGo.GetComponent<RectTransform>();
        badgeRt.SetParent(panel, false);
        badgeRt.sizeDelta = new Vector2(34f, 34f);
        badgeRt.anchoredPosition = new Vector2(-120f, 8f);
        var badge = badgeGo.GetComponent<Image>();
        badge.sprite = UITheme.SolidSprite;
        badge.raycastTarget = false;
        badge.color = theme != null
            ? UITheme.WithAlpha(theme.surfaceDeep, 0.92f)
            : new Color(0.12f, 0.10f, 0.06f, 0.92f);

        keyLabel = MakeLabel(badgeRt, "KeyLabel", 20f, FontStyles.Bold,
                             theme != null ? theme.goldLight : Color.white,
                             TextAlignmentOptions.Center);
        StretchTo(keyLabel.rectTransform, badgeRt);

        promptLabel = MakeLabel(panel, "Prompt", 22f, FontStyles.Bold,
                                availableColor, TextAlignmentOptions.Left);
        promptLabel.rectTransform.sizeDelta        = new Vector2(260f, 30f);
        promptLabel.rectTransform.anchoredPosition = new Vector2(35f, 8f);

        subLabel = MakeLabel(panel, "Sub", 15f, FontStyles.Normal,
                             unavailableColor, TextAlignmentOptions.Left);
        subLabel.rectTransform.sizeDelta        = new Vector2(260f, 22f);
        subLabel.rectTransform.anchoredPosition = new Vector2(35f, -16f);
        subLabel.gameObject.SetActive(false);
    }

    private TMP_Text MakeLabel(RectTransform parent, string name, float size,
                               FontStyles style, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize      = size;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.alignment     = align;
        tmp.raycastTarget = false;

        if (UITheme.Active != null && UITheme.Active.bodyFont != null)
            tmp.font = UITheme.Active.bodyFont;

        return tmp;
    }

    private static void StretchTo(RectTransform rt, RectTransform parent)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
