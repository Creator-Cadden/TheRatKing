using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a weapon-specific cutscene when the boss arena scene loads, then
/// triggers BossHealthBarUI.PlayIntro() so the bar grows in as gameplay starts.
///
/// Three video slots — one per weapon (Blade / Hammer / Bow). The script
/// reads the player's equipped weapon at Start and plays the matching clip.
///
/// Setup (full details in BossCutscene_Setup.md):
///   1. Import your three .mp4 files into Assets/ — Unity creates VideoClip
///      assets automatically.
///   2. On a GameObject in the boss scene (e.g. an empty named "CutsceneRig"):
///        • Add Video Player component
///        • Add this script
///        • Wire the three clips into the Inspector slots
///        • Set Render Mode = Render Texture on the VideoPlayer
///        • Assign a RenderTexture asset to the VideoPlayer's Target Texture
///   3. On the MainUI canvas (or a dedicated cutscene canvas):
///        • Create a fullscreen RawImage child named "CutsceneScreen"
///        • Set its Texture to the same RenderTexture
///        • Drag CutsceneScreen into the "Video Canvas Root" Inspector field
///   4. Drag your BossHealthBarUI into the matching Inspector field.
///   5. Make sure BossHealthBarUI.showMode = Manual (so it waits for our cue).
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class BossCutsceneController : MonoBehaviour
{
    [Header("Cutscenes per Equipped Weapon")]
    [Tooltip("Cutscene that plays when entering the boss arena with the BLADE equipped.")]
    public VideoClip bladeCutscene;

    [Tooltip("Cutscene that plays when entering the boss arena with the HAMMER equipped.")]
    public VideoClip hammerCutscene;

    [Tooltip("Cutscene that plays when entering the boss arena with the BOW equipped.")]
    public VideoClip bowCutscene;

    [Tooltip("Fallback clip if the equipped-weapon slot above is empty " +
             "(e.g. you forgot to wire one). Leave null to skip straight to " +
             "the boss bar intro when no clip is found.")]
    public VideoClip fallbackCutscene;

    // ─────────────────────────────────────────
    [Header("Video Display")]
    [Tooltip("The GameObject holding the fullscreen RawImage that displays " +
             "the video (typically a child of the MainUI canvas). Activated " +
             "while the cutscene plays, deactivated when it ends.")]
    public GameObject videoCanvasRoot;

    // ─────────────────────────────────────────
    [Header("Boss Bar")]
    [Tooltip("BossHealthBarUI in the scene. PlayIntro() is called when the " +
             "cutscene finishes (or is skipped) so the bar grows in.")]
    public BossHealthBarUI bossHealthBar;

    [Tooltip("Optional small delay (seconds) between the cutscene ending and " +
             "the boss bar intro firing. 0.3 = small breath. 0 = instant.")]
    public float bossBarIntroDelay = 0.3f;

    // ─────────────────────────────────────────
    [Header("Player Lock")]
    [Tooltip("If on, disables PlayerMovement and PlayerCombat while the cutscene " +
             "plays so the player can't move or attack mid-scene.")]
    public bool freezePlayerDuringCutscene = true;

    // ─────────────────────────────────────────
    [Header("Skip")]
    [Tooltip("If on, the player can press the configured key to skip the cutscene.")]
    public bool allowSkip = true;

    [Tooltip("Key that skips the cutscene (Input System keyboard binding).")]
    public Key skipKey = Key.Escape;

    [Tooltip("Show a small 'Press [key] to skip' label by enabling this canvas " +
             "object during the cutscene. Optional.")]
    public GameObject skipPromptRoot;

    // ─────────────────────────────────────────
    [Header("Debug")]
    [Tooltip("If on, logs which weapon was detected + which clip is playing.")]
    public bool verbose = true;

    [Tooltip("If on, plays the cutscene as soon as the scene starts. Turn off " +
             "if you want to trigger it manually via PlayCutscene() from another script.")]
    public bool playOnStart = true;

    // ─────────────────────────────────────────
    // Runtime
    // ─────────────────────────────────────────
    private VideoPlayer _videoPlayer;
    private bool        _isPlaying;
    private PlayerMovement _playerMovement;
    private PlayerCombat   _playerCombat;

    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();
    }

    void Start()
    {
        // Hide the video canvas by default — only shown while cutscene plays.
        if (videoCanvasRoot  != null) videoCanvasRoot.SetActive(false);
        if (skipPromptRoot   != null) skipPromptRoot.SetActive(false);

        if (playOnStart) PlayCutscene();
    }

    void Update()
    {
        if (!_isPlaying || !allowSkip) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current[skipKey].wasPressedThisFrame)
        {
            if (verbose) Debug.Log("[BossCutsceneController] Skipped via input.");
            EndCutscene();
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>Manually trigger the weapon-appropriate cutscene.</summary>
    public void PlayCutscene()
    {
        if (_isPlaying) return;

        VideoClip clip = SelectClipForEquippedWeapon();
        if (clip == null)
        {
            if (verbose) Debug.Log("[BossCutsceneController] No clip for current weapon — skipping straight to boss bar intro.");
            TriggerBossBarIntro();
            return;
        }

        _isPlaying = true;
        if (verbose) Debug.Log($"[BossCutsceneController] Playing '{clip.name}'.");

        if (videoCanvasRoot != null) videoCanvasRoot.SetActive(true);
        if (skipPromptRoot  != null) skipPromptRoot.SetActive(allowSkip);

        if (freezePlayerDuringCutscene) LockPlayer(true);

        _videoPlayer.clip = clip;
        _videoPlayer.loopPointReached += OnClipFinished;
        _videoPlayer.Play();
    }

    /// <summary>End the cutscene early (also called automatically when the clip ends).</summary>
    public void EndCutscene()
    {
        if (!_isPlaying) return;
        _isPlaying = false;

        _videoPlayer.loopPointReached -= OnClipFinished;
        _videoPlayer.Stop();

        if (videoCanvasRoot != null) videoCanvasRoot.SetActive(false);
        if (skipPromptRoot  != null) skipPromptRoot.SetActive(false);

        if (freezePlayerDuringCutscene) LockPlayer(false);

        TriggerBossBarIntro();
    }

    // ═════════════════════════════════════════════════════════════
    // Internals
    // ═════════════════════════════════════════════════════════════

    private VideoClip SelectClipForEquippedWeapon()
    {
        EntityStats.WeaponType weapon = GetEquippedWeapon();

        VideoClip pick = weapon switch
        {
            EntityStats.WeaponType.Blade  => bladeCutscene,
            EntityStats.WeaponType.Hammer => hammerCutscene,
            EntityStats.WeaponType.Bow    => bowCutscene,
            _                             => null,
        };

        if (verbose)
            Debug.Log($"[BossCutsceneController] Equipped weapon = {weapon}, " +
                      $"clip = {(pick != null ? pick.name : "null")}");

        return pick != null ? pick : fallbackCutscene;
    }

    private EntityStats.WeaponType GetEquippedWeapon()
    {
        // Source of truth #1 — the save data (set during scene transitions).
        if (GameManager.Instance != null && GameManager.Instance.ActiveSave != null
            && GameManager.Instance.ActiveSave.hasData)
        {
            return (EntityStats.WeaponType)GameManager.Instance.ActiveSave.equippedWeapon;
        }

        // Source of truth #2 — read directly from the player in the scene.
        // (Test mode flow, no save active.)
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            EntityStats stats = player.GetComponent<EntityStats>();
            if (stats != null) return stats.EquippedWeapon;
        }

        return EntityStats.WeaponType.Blade;
    }

    private void OnClipFinished(VideoPlayer vp)
    {
        if (verbose) Debug.Log("[BossCutsceneController] Cutscene finished naturally.");
        EndCutscene();
    }

    private void LockPlayer(bool locked)
    {
        if (_playerMovement == null || _playerCombat == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) return;
            _playerMovement = player.GetComponent<PlayerMovement>();
            _playerCombat   = player.GetComponent<PlayerCombat>();
        }

        if (_playerMovement != null) _playerMovement.enabled = !locked;
        if (_playerCombat   != null) _playerCombat.enabled   = !locked;
    }

    private void TriggerBossBarIntro()
    {
        if (bossHealthBar == null) return;

        if (bossBarIntroDelay > 0f)
            Invoke(nameof(FireBossIntro), bossBarIntroDelay);
        else
            FireBossIntro();
    }

    private void FireBossIntro()
    {
        if (bossHealthBar != null) bossHealthBar.PlayIntro();
    }
}
