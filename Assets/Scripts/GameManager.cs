using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton GameManager — persists across scenes.
/// Handles: player death, death overlay, retry/reset, save/load, scene loading.
///
/// Setup:
///   1. Place on an empty GameObject in your FIRST scene (MainMenu or Floor1).
///   2. Assign deathScreen in the Inspector if this is a game scene.
///   3. Make sure your Player is tagged "Player" and spawn point tagged "SpawnPoint".
///   4. Set mainMenuScene and firstGameScene to match your actual scene names.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Scene names ───────────────────────────────────────────────
    [Header("Scene Names")]
    public string mainMenuScene  = "MainMenu";
    public string firstGameScene = "Floor1";

    // ── Death screen ──────────────────────────────────────────────
    [Header("Death Screen")]
    [Tooltip("Assign the DeathScreen component. Can be left null in the MainMenu scene.")]
    public DeathScreen deathScreen;

    [Header("Timing")]
    [Tooltip("Seconds after death before the overlay appears.")]
    public float deathScreenDelay = 1.2f;

    // ── Active save slot ──────────────────────────────────────────
    public int      ActiveSlot  { get; private set; } = -1;
    public SaveData ActiveSave  { get; private set; }
    public bool     HasActiveGame => ActiveSlot >= 0 && ActiveSave != null && ActiveSave.hasData;

    // ── Runtime player references (re-cached on each scene load) ──
    private Transform           _playerTransform;
    private CharacterController _playerController;
    private EntityStats         _playerStats;
    private XPSystem            _xpSystem;
    private Animator            _playerAnimator;

    private Vector3    _spawnPosition;
    private Quaternion _spawnRotation;

    private bool _isDead;

    // ═════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ═════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Update()
    {
        // Accumulate play time while a game is active
        if (HasActiveGame && ActiveSave != null)
            ActiveSave.totalPlayTime += Time.unscaledDeltaTime;
    }

    // ═════════════════════════════════════════════════════════════
    // Scene loaded — re-cache everything
    // ═════════════════════════════════════════════════════════════

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isDead = false;

        // Nothing to do in the main menu
        if (scene.name == mainMenuScene) return;

        // Re-find player references in the new scene
        CachePlayerReferences();
        CacheSpawnPoint();

        if (_playerStats != null)
            _playerStats.onDeath.AddListener(OnPlayerDeath);

        // Re-find death screen in the new scene if not already assigned
        if (deathScreen == null)
            deathScreen = FindFirstObjectByType<DeathScreen>();

        if (deathScreen != null)
            deathScreen.Hide(instant: true);

        // Apply save data if continuing
        if (HasActiveGame && _playerStats != null && _xpSystem != null)
            SaveSystem.ApplyToStats(ActiveSave, _playerStats, _xpSystem);

        // Checkpoint — record this scene as the current respawn point
        SaveCheckpoint(scene.name);
    }

    // ═════════════════════════════════════════════════════════════
    // Start new / continue
    // ═════════════════════════════════════════════════════════════

    public void StartNewGame(int slot, string sceneName = "")
    {
        ActiveSlot = slot;
        ActiveSave = new SaveData
        {
            hasData          = true,
            currentSceneName = string.IsNullOrEmpty(sceneName) ? firstGameScene : sceneName,
            currentFloor     = 1
        };

        SaveSystem.Delete(slot);
        LoadScene(ActiveSave.currentSceneName);
    }

    public void ContinueGame(int slot)
    {
        ActiveSlot = slot;
        ActiveSave = SaveSystem.Load(slot);
        LoadScene(ActiveSave.currentSceneName);
    }

    // ═════════════════════════════════════════════════════════════
    // Saving
    // ═════════════════════════════════════════════════════════════

    public void SaveCheckpoint(string sceneName)
    {
        if (ActiveSlot < 0 || _playerStats == null || _xpSystem == null) return;

        ActiveSave = SaveSystem.CaptureCurrentState(
            _playerStats, _xpSystem, sceneName,
            ActiveSave?.totalPlayTime ?? 0f);

        SaveSystem.Save(ActiveSlot, ActiveSave);
        Debug.Log($"[GameManager] Checkpoint saved — slot {ActiveSlot}, scene '{sceneName}'");
    }

    // ═════════════════════════════════════════════════════════════
    // Death flow  (same as your original, extended for saves)
    // ═════════════════════════════════════════════════════════════

    private void OnPlayerDeath()
    {
        if (_isDead) return;
        _isDead = true;
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        yield return new WaitForSecondsRealtime(deathScreenDelay);

        Time.timeScale = 0f;

        if (deathScreen != null)
            deathScreen.Show();
    }

    // ═════════════════════════════════════════════════════════════
    // Retry — called by DeathScreen Retry button
    // Reloads the checkpoint scene (start of current level)
    // ═════════════════════════════════════════════════════════════

    public void Retry()
    {
        Time.timeScale = 1f;
        StartCoroutine(RetrySequence());
    }

    private IEnumerator RetrySequence()
    {
        if (deathScreen != null)
            deathScreen.Hide(instant: false);

        yield return null;

        // If we have a save, reload the checkpoint scene with stats restored.
        // Otherwise fall back to in-scene respawn (original behaviour).
        if (HasActiveGame)
        {
            LoadScene(ActiveSave.currentSceneName);
        }
        else
        {
            ResetPlayerInPlace();
            _isDead = false;
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Game flow — called by PauseMenu and DeathScreen buttons
    // ═════════════════════════════════════════════════════════════

    public void ResetToCheckpoint()
    {
        Time.timeScale = 1f;

        if (HasActiveGame)
            LoadScene(ActiveSave.currentSceneName);
        else
            Retry();
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        CursorManager.ForceReset();
        LoadScene(mainMenuScene);
    }

    public void DeleteSaveAndQuit()
    {
        if (ActiveSlot >= 0) SaveSystem.Delete(ActiveSlot);
        ActiveSlot = -1;
        ActiveSave = null;
        ReturnToMainMenu();
    }

    // ═════════════════════════════════════════════════════════════
    // In-place reset (fallback when no save system active)
    // Preserves your original respawn-at-spawnpoint behaviour
    // ═════════════════════════════════════════════════════════════

    private void ResetPlayerInPlace()
    {
        if (_playerTransform == null) return;

        if (_playerController != null) _playerController.enabled = false;
        _playerTransform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        if (_playerController != null) _playerController.enabled = true;

        _playerStats?.ResetToFull();

        if (_playerAnimator != null)
        {
            _playerAnimator.Rebind();
            _playerAnimator.Update(0f);
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════

    private void LoadScene(string sceneName)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

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
        _xpSystem         = player.GetComponent<XPSystem>();
        _playerAnimator   = player.GetComponentInChildren<Animator>();
    }

    private void CacheSpawnPoint()
    {
        GameObject spawnObj = GameObject.FindWithTag("SpawnPoint");

        if (spawnObj == null)
        {
            Debug.LogWarning("[GameManager] No 'SpawnPoint' tagged object — using player position.");
            if (_playerTransform != null)
            {
                _spawnPosition = _playerTransform.position;
                _spawnRotation = _playerTransform.rotation;
            }
            return;
        }

        _spawnPosition = spawnObj.transform.position;
        _spawnRotation = spawnObj.transform.rotation;
        Debug.Log($"[GameManager] Spawn point → {_spawnPosition}");
    }
}