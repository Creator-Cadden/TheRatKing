using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton GameManager — persists across scenes.
/// Handles: player death, death overlay, retry/reset, save/load, scene loading.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Scene names ───────────────────────────────────────────────
    [Header("Scene Names")]
    public string mainMenuScene     = "MainMenu";
    public string weaponSelectScene = "PlayerCustom";
    public string firstGameScene    = "lvl1";
    [Tooltip("Scene loaded when the player enters the enemy test arena. " +
             "Goes through WeaponSelect first. Never writes to a save slot.")]
    public string testWorldScene    = "TestingArena";

    // ── Death screen ──────────────────────────────────────────────
    [Header("In-Game UI — auto-found on scene load, do not assign in Inspector")]
    [Tooltip("Found automatically in each game scene. Leave null.")]
    public DeathScreen deathScreen;
    [Tooltip("Found automatically in each game scene. Leave null.")]
    public PauseMenu   pauseMenu;

    [Header("Timing")]
    [Tooltip("Seconds after death before the overlay appears.")]
    public float deathScreenDelay = 1.2f;

    // ── Active save slot ──────────────────────────────────────────
    public int      ActiveSlot  { get; private set; } = -1;
    public SaveData ActiveSave  { get; private set; }
    public bool     HasActiveGame => ActiveSlot >= 0 && ActiveSave != null && ActiveSave.hasData;

    // ── Test mode ─────────────────────────────────────────────────
    // When true, player is in the TestingArena. No save file is written,
    // no play time is accumulated, ActiveSlot stays at -1.
    public bool IsTestMode        { get; private set; } = false;
    public bool IsPendingTestMode => _pendingTestMode;
    private bool _pendingTestMode = false;

    // ── Runtime player references (re-cached on each scene load) ──
    private Transform           _playerTransform;
    private CharacterController _playerController;
    private EntityStats         _playerStats;
    private XPSystem            _xpSystem;
    private Animator            _playerAnimator;

    private Vector3    _spawnPosition;
    private Quaternion _spawnRotation;

    private bool _isDead;

    // ── Unity lifecycle ──

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
        // Accumulate play time while a real game is active.
        // Test mode never touches a save slot, so don't bump play time.
        if (HasActiveGame && ActiveSave != null && !IsTestMode)
            ActiveSave.totalPlayTime += Time.unscaledDeltaTime;
    }

    // ── Scene loaded — re-cache everything ──

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isDead = false;

        // Nothing to do in menu or weapon select scenes — no player present
        if (scene.name == mainMenuScene) return;
        if (scene.name == weaponSelectScene) return;

        // Re-find player references in the new scene
        CachePlayerReferences();
        CacheSpawnPoint();

        if (_playerStats != null)
        {
            // Remove first to prevent double-subscription on scene reload
            _playerStats.onDeath.RemoveListener(OnPlayerDeath);
            _playerStats.onDeath.AddListener(OnPlayerDeath);
            Debug.Log("[GameManager] Subscribed to player onDeath.");
        }

        // Re-find scene UI — searches inactive objects too so hidden roots are found
        deathScreen = FindFirstObjectByType<DeathScreen>(FindObjectsInactive.Include);
        pauseMenu   = FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);

        if (deathScreen != null)
        {
            deathScreen.Hide(instant: true);
            Debug.Log("[GameManager] DeathScreen found: " + deathScreen.gameObject.name);
        }
        else
            Debug.LogWarning("[GameManager] No DeathScreen in scene '" + scene.name + "'.");

        if (pauseMenu != null)
            Debug.Log("[GameManager] PauseMenu found: " + pauseMenu.gameObject.name);
        else
            Debug.LogWarning("[GameManager] No PauseMenu in scene '" + scene.name + "'.");

        // Apply save data if continuing or starting new game
        if (HasActiveGame && _playerStats != null && _xpSystem != null)
        {
            SaveSystem.ApplyToStats(ActiveSave, _playerStats, _xpSystem);

            // Equip the weapon stored in the save (chosen in WeaponSelect
            // for new games, restored from file on continue)
            _playerStats.EquipWeapon(
                (EntityStats.WeaponType)ActiveSave.equippedWeapon);
        }
        else if (IsTestMode && _playerStats != null)
        {
            // Test mode — no save file. Reset to full and equip the
            // weapon the player picked on the WeaponSelect screen.
            _playerStats.ResetToFull();
            _playerStats.EquipWeapon(_pendingWeapon);
        }

        // Checkpoint — record this scene as the current respawn point.
        // SaveCheckpoint internally no-ops in test mode.
        SaveCheckpoint(scene.name);
    }

    // ── Start new / continue ──

    public void StartNewGame(int slot, string sceneName = "", string saveName = "",
        EntityStats.WeaponType startingWeapon = EntityStats.WeaponType.Blade)
    {
        ActiveSlot     = slot;
        _pendingWeapon = startingWeapon;

        ActiveSave = new SaveData
        {
            hasData          = true,
            currentSceneName = string.IsNullOrEmpty(sceneName) ? firstGameScene : sceneName,
            currentFloor     = 1,
            saveName         = saveName,
            equippedWeapon   = (int)startingWeapon
        };

        SaveSystem.Delete(slot);
        LoadScene(ActiveSave.currentSceneName);
    }

    // ── Pending new game state (set before WeaponSelect, consumed after) ──
    public int    PendingSlot    { get; private set; } = -1;
    public string PendingName    { get; private set; } = "";
    public string PendingFirstScene  { get; private set; } = "";
    public EntityStats.WeaponType PendingWeapon { get; private set; } = EntityStats.WeaponType.Blade;
    private EntityStats.WeaponType _pendingWeapon = EntityStats.WeaponType.Blade;

    /// <summary>
    /// Called by MainMenuUI after name is entered.
    /// Stores slot + name then loads the weapon select scene.
    /// WeaponSelect calls StartNewGame() once a weapon is picked.
    /// </summary>
    public void PrepareNewGame(int slot, string saveName, string weaponSelectScene)
    {
        PendingSlot       = slot;
        PendingName       = saveName;
        PendingFirstScene = firstGameScene;
        LoadScene(weaponSelectScene);
    }

    public void ContinueGame(int slot)
    {
        ActiveSlot = slot;
        ActiveSave = SaveSystem.Load(slot);
        IsTestMode = false;
        LoadScene(ActiveSave.currentSceneName);
    }

    // ── Level transition — used by LevelTransition triggers ──

    /// <summary>
    /// Called by a LevelTransition trigger when the player crosses into
    /// the next level. Captures the player's CURRENT in-game stats (HP,
    /// stamina, XP, weapon, etc.) into ActiveSave with the destination
    /// </summary>
    public void TransitionToLevel(string sceneName, int floorMode = -1)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[GameManager] TransitionToLevel called with empty sceneName.");
            return;
        }

        // Test mode — no save, just load. The new TestingArena reload will
        // reset the player to full / re-equip the weapon as usual.
        if (IsTestMode)
        {
            LoadScene(sceneName);
            return;
        }

        // Real game — capture the player's current state into ActiveSave
        // with the DESTINATION scene name as the new checkpoint.
        if (HasActiveGame && _playerStats != null && _xpSystem != null)
        {
            ActiveSave = SaveSystem.CaptureCurrentState(
                _playerStats, _xpSystem, sceneName,
                ActiveSave?.totalPlayTime ?? 0f,
                ActiveSave?.saveName ?? "");

            // Floor handling
            if (floorMode == -1)
                ActiveSave.currentFloor = Mathf.Min(ActiveSave.currentFloor + 1, 3);
            else if (floorMode >= 1 && floorMode <= 3)
                ActiveSave.currentFloor = floorMode;
            // floorMode == 0 → leave currentFloor as-captured

            Debug.Log($"[GameManager] Transitioning → '{sceneName}' " +
                      $"with stats HP:{ActiveSave.currentHealth}/{ActiveSave.maxHealth} " +
                      $"STR:{ActiveSave.strength} Floor:{ActiveSave.currentFloor}");
        }
        else
        {
            Debug.LogWarning("[GameManager] TransitionToLevel: no active save / player — " +
                             "loading scene without stat capture.");
        }

        LoadScene(sceneName);
    }

    // ── Test world flow — no save slot, weapon select then arena ──

    /// <summary>
    /// Called by MainMenuUI when the "Test Arena" button is clicked.
    /// Flags the next WeaponSelect confirm as a test run, then loads
    /// WeaponSelect so the player can pick a weapon.
    /// </summary>
    public void EnterTestWorld()
    {
        // Wipe any active save state so test mode doesn't leak.
        ActiveSlot        = -1;
        ActiveSave        = null;
        IsTestMode        = false;          // set true on StartTestWorld()
        _pendingTestMode  = true;
        PendingSlot       = -1;
        PendingName       = "";
        PendingFirstScene = testWorldScene;

        LoadScene(weaponSelectScene);
    }

    /// <summary>
    /// Called by WeaponSelectUI.OnConfirm() when IsPendingTestMode is true.
    /// Loads the test arena with the chosen weapon. No save file, no save slot.
    /// </summary>
    public void StartTestWorld(EntityStats.WeaponType chosenWeapon)
    {
        ActiveSlot       = -1;
        ActiveSave       = null;
        IsTestMode       = true;
        _pendingTestMode = false;
        _pendingWeapon   = chosenWeapon;

        string scene = string.IsNullOrEmpty(testWorldScene) ? "TestingArena" : testWorldScene;
        LoadScene(scene);
    }

    // ── Saving ──

    public void SaveCheckpoint(string sceneName)
    {
        // Test mode never writes to a save slot.
        if (IsTestMode) return;
        if (ActiveSlot < 0 || _playerStats == null || _xpSystem == null) return;

        ActiveSave = SaveSystem.CaptureCurrentState(
            _playerStats, _xpSystem, sceneName,
            ActiveSave?.totalPlayTime ?? 0f,
            ActiveSave?.saveName ?? "");

        SaveSystem.Save(ActiveSlot, ActiveSave);
        Debug.Log($"[GameManager] Checkpoint saved — slot {ActiveSlot}, scene '{sceneName}'");
    }

    // ── Death flow  (same as your original, extended for saves) ──

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

    // ── Retry — called by DeathScreen Retry button Reloads the checkpoint scene (start of current level) ──

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

        // In test mode, just reload the test scene with the same weapon.
        if (IsTestMode)
        {
            string scene = string.IsNullOrEmpty(testWorldScene) ? "TestingArena" : testWorldScene;
            LoadScene(scene);
            yield break;
        }

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

    // ── Game flow — called by PauseMenu and DeathScreen buttons ──

    public void ResetToCheckpoint()
    {
        Time.timeScale = 1f;

        if (IsTestMode)
        {
            string scene = string.IsNullOrEmpty(testWorldScene) ? "TestingArena" : testWorldScene;
            LoadScene(scene);
            return;
        }

        if (HasActiveGame)
            LoadScene(ActiveSave.currentSceneName);
        else
            Retry();
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        CursorManager.ForceReset();

        // Clear test-mode flags so the main menu starts clean.
        IsTestMode       = false;
        _pendingTestMode = false;

        LoadScene(mainMenuScene);
    }

    public void DeleteSaveAndQuit()
    {
        if (ActiveSlot >= 0) SaveSystem.Delete(ActiveSlot);
        ActiveSlot = -1;
        ActiveSave = null;
        ReturnToMainMenu();
    }

    // ── In-place reset (fallback when no save system active) Preserves your original respawn-at-spawnpoint behaviour ──

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

    // ── Helpers ──

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
