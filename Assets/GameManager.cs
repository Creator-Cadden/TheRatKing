using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton GameManager — handles player death, game-over overlay, and retry/reset.
///
/// ── Setup ──────────────────────────────────────────────────────────────────────
/// 1. Create an empty GameObject called "GameManager" in your scene.
/// 2. Attach this script.
/// 3. Assign the DeathScreenCanvas reference (see DeathScreen.cs / the Canvas prefab).
/// 4. Make sure your SpawnPoint GameObject is tagged "SpawnPoint".
/// 5. Make sure your Player GameObject is tagged "Player".
///
/// The manager subscribes to EntityStats.onDeath automatically at runtime —
/// no extra wiring needed in PlayerCombat or PlayerMovement.
/// ───────────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Singleton ──
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Assign the root Canvas GameObject that contains the death / game-over UI.")]
    public DeathScreen deathScreen;

    [Header("Timing")]
    [Tooltip("Seconds after the player dies before the overlay fades in. " +
             "Gives the death animation a moment to play.")]
    public float deathScreenDelay = 1.2f;

    // ── Runtime state ──
    private Transform _playerTransform;
    private CharacterController _playerController;
    private EntityStats _playerStats;
    private Animator _playerAnimator;

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    private bool _isDead;

    // ───────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Optional: DontDestroyOnLoad(gameObject); // only if using multiple scenes
    }

    void Start()
    {
        CachePlayerReferences();
        CacheSpawnPoint();

        if (_playerStats != null)
            _playerStats.onDeath.AddListener(OnPlayerDeath);

        if (deathScreen != null)
            deathScreen.Hide(instant: true);
    }

    // ───────────────────────────────────────────────
    // Player death
    // ───────────────────────────────────────────────

    private void OnPlayerDeath()
    {
        if (_isDead) return;
        _isDead = true;
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        // Wait for death animation to breathe
        yield return new WaitForSecondsRealtime(deathScreenDelay);

        // Freeze the game
        Time.timeScale = 0f;

        // Show overlay
        if (deathScreen != null)
            deathScreen.Show();
    }

    // ───────────────────────────────────────────────
    // Called by the Retry button on the DeathScreen
    // ───────────────────────────────────────────────

    public void Retry()
    {
        // Unfreeze first so coroutines and physics work again
        Time.timeScale = 1f;

        StartCoroutine(RetrySequence());
    }

    private IEnumerator RetrySequence()
    {
        // Hide overlay immediately
        if (deathScreen != null)
            deathScreen.Hide(instant: false);

        // Wait one frame so the overlay starts fading before we reset
        yield return null;

        ResetPlayer();
        _isDead = false;
    }

    // ───────────────────────────────────────────────
    // Reset helpers
    // ───────────────────────────────────────────────

    private void ResetPlayer()
    {
        if (_playerTransform == null)
        {
            Debug.LogWarning("[GameManager] Player reference lost — cannot reset.");
            return;
        }

        // ── 1. Teleport to spawn ──
        // CharacterController blocks Transform.position changes, so disable it briefly.
        if (_playerController != null) _playerController.enabled = false;

        _playerTransform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

        if (_playerController != null) _playerController.enabled = true;

        // ── 2. Restore full health / stamina ──
        if (_playerStats != null)
            _playerStats.ResetToFull();

        // ── 3. Clear animation state ──
        if (_playerAnimator != null)
        {
            _playerAnimator.Rebind();
            _playerAnimator.Update(0f);
        }
    }

    // ───────────────────────────────────────────────
    // Caching
    // ───────────────────────────────────────────────

    private void CachePlayerReferences()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[GameManager] No GameObject tagged 'Player' found!");
            return;
        }

        _playerTransform  = player.transform;
        _playerController = player.GetComponent<CharacterController>();
        _playerStats      = player.GetComponent<EntityStats>();
        _playerAnimator   = player.GetComponentInChildren<Animator>();
    }

    private void CacheSpawnPoint()
    {
        GameObject spawnObj = GameObject.FindWithTag("SpawnPoint");
        if (spawnObj == null)
        {
            Debug.LogWarning("[GameManager] No GameObject tagged 'SpawnPoint' found — using player's current position.");
            if (_playerTransform != null)
            {
                _spawnPosition = _playerTransform.position;
                _spawnRotation = _playerTransform.rotation;
            }
            return;
        }

        _spawnPosition = spawnObj.transform.position;
        _spawnRotation = spawnObj.transform.rotation;
        Debug.Log($"[GameManager] Spawn point set at {_spawnPosition}");
    }
}