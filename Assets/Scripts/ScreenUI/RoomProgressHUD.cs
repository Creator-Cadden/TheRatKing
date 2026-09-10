using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// The "where am I in the tower" readout: FLOOR 1 · ROOM 3, plus a live enemy
/// counter during a fight and a ROOM CLEARED stinger when the gate opens.
///
/// This is the piece that makes a linear tower climb legible. Without it the
/// player can't tell a 5-room floor from a 9-room one, and clearing a room feels
/// like nothing happened.
///
/// Reads the floor from the active save and the room from the scene name
/// (your naming convention "1_3 Utility" = floor 1, room 3), and binds to the
/// scene's <see cref="EncounterController"/> for the enemy count.
/// </summary>
public class RoomProgressHUD : MonoBehaviour
{
    [Header("Labels")]
    [Tooltip("e.g. 'FLOOR 1'.")]
    public TMP_Text floorLabel;

    [Tooltip("e.g. 'ROOM 3 / 6'.")]
    public TMP_Text roomLabel;

    [Tooltip("e.g. 'RATS LEFT  3'. Hidden when no encounter is active.")]
    public TMP_Text enemyCountLabel;

    [Header("Room Cleared stinger")]
    [Tooltip("Optional. A CanvasGroup holding a 'ROOM CLEARED' banner. Faded in " +
             "for a moment when the last enemy dies.")]
    public CanvasGroup clearedBanner;
    public TMP_Text    clearedLabel;
    public string      clearedMessage = "ROOM CLEARED";
    public float       clearedHold    = 1.6f;
    public float       clearedFade    = 0.35f;

    [Header("Formats")]
    [Tooltip("{0} = floor number.")]
    public string floorFormat = "FLOOR {0}";
    [Tooltip("{0} = room number, {1} = rooms on this floor.")]
    public string roomFormat  = "ROOM {0} / {1}";
    [Tooltip("{0} = enemies remaining.")]
    public string enemyFormat = "RATS LEFT  {0}";

    [Header("Rooms per floor")]
    [Tooltip("Floor 1 has this many rooms including the boss. Each floor above adds " +
             "'Extra Rooms Per Floor' more. Matches the design doc: F1 = 5 + boss.")]
    public int roomsOnFloorOne = 6;
    public int extraRoomsPerFloor = 2;

    [Header("Toasts")]
    [Tooltip("Also fire a corner toast when a room is cleared.")]
    public bool toastOnClear = false;

    // ── Private ───────────────────────────────────────────────────────────────

    private EncounterController _encounter;
    private float _bannerTimer = -1f;
    private int   _lastAlive   = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        BindEncounter();
        RefreshLocation();

        if (clearedBanner != null)
        {
            clearedBanner.alpha = 0f;
            if (clearedLabel != null) clearedLabel.text = clearedMessage;
        }
        if (enemyCountLabel != null) enemyCountLabel.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (_encounter != null)
            _encounter.onAllDefeated.RemoveListener(OnRoomCleared);
    }

    void Update()
    {
        // The encounter may spawn a frame or two after us.
        if (_encounter == null) BindEncounter();

        UpdateEnemyCount();
        UpdateBanner();
    }

    // ── Location ──────────────────────────────────────────────────────────────

    /// <summary>Re-read floor/room. Call this after a level transition if you keep the HUD alive.</summary>
    public void RefreshLocation()
    {
        int floor = 1;
        if (GameManager.Instance != null && GameManager.Instance.ActiveSave != null)
            floor = Mathf.Max(1, GameManager.Instance.ActiveSave.currentFloor);

        string sceneName = SceneManager.GetActiveScene().name;
        if (TryParseSceneName(sceneName, out int parsedFloor, out int parsedRoom))
        {
            floor = parsedFloor;
            int total = RoomsOnFloor(floor);
            if (roomLabel != null)
                roomLabel.text = string.Format(roomFormat, parsedRoom, total);
        }
        else if (roomLabel != null)
        {
            // Not a numbered room (TestingArena, Tutorial) — hide rather than lie.
            roomLabel.text = "";
        }

        if (floorLabel != null) floorLabel.text = string.Format(floorFormat, floor);
    }

    private int RoomsOnFloor(int floor) =>
        roomsOnFloorOne + Mathf.Max(0, floor - 1) * extraRoomsPerFloor;

    /// <summary>Parses "1_3 Utility" / "1_6 Boss" into floor 1, room 3 / 6.</summary>
    private static bool TryParseSceneName(string sceneName, out int floor, out int room)
    {
        floor = room = 0;
        if (string.IsNullOrEmpty(sceneName)) return false;

        int underscore = sceneName.IndexOf('_');
        if (underscore <= 0) return false;

        string floorPart = sceneName.Substring(0, underscore);
        if (!int.TryParse(floorPart, out floor)) return false;

        // Everything after '_' up to the first space is the room number.
        string rest = sceneName.Substring(underscore + 1);
        int space   = rest.IndexOf(' ');
        string roomPart = space > 0 ? rest.Substring(0, space) : rest;

        return int.TryParse(roomPart, out room);
    }

    // ── Encounter binding ─────────────────────────────────────────────────────

    private void BindEncounter()
    {
        EncounterController found = FindFirstObjectByType<EncounterController>();
        if (found == null || found == _encounter) return;

        if (_encounter != null) _encounter.onAllDefeated.RemoveListener(OnRoomCleared);

        _encounter = found;
        _encounter.onAllDefeated.AddListener(OnRoomCleared);
    }

    private void UpdateEnemyCount()
    {
        if (enemyCountLabel == null) return;

        if (_encounter == null || _encounter.IsCleared || _encounter.TotalCount == 0)
        {
            if (enemyCountLabel.gameObject.activeSelf)
                enemyCountLabel.gameObject.SetActive(false);
            return;
        }

        int alive = _encounter.AliveCount;
        if (alive == _lastAlive) return;
        _lastAlive = alive;

        if (!enemyCountLabel.gameObject.activeSelf)
            enemyCountLabel.gameObject.SetActive(true);

        enemyCountLabel.text = string.Format(enemyFormat, alive);
    }

    // ── Cleared stinger ───────────────────────────────────────────────────────

    private void OnRoomCleared()
    {
        if (clearedBanner != null) _bannerTimer = 0f;

        if (toastOnClear)
            ToastNotifier.Show(clearedMessage, "", ToastNotifier.ToastKind.Good);
    }

    private void UpdateBanner()
    {
        if (clearedBanner == null || _bannerTimer < 0f) return;

        _bannerTimer += Time.deltaTime;

        float total = clearedFade + clearedHold + clearedFade;
        float a;
        if (_bannerTimer < clearedFade)
            a = clearedFade > 0f ? _bannerTimer / clearedFade : 1f;
        else if (_bannerTimer < clearedFade + clearedHold)
            a = 1f;
        else
            a = clearedFade > 0f ? 1f - (_bannerTimer - clearedFade - clearedHold) / clearedFade : 0f;

        clearedBanner.alpha = Mathf.Clamp01(a);

        if (_bannerTimer >= total)
        {
            clearedBanner.alpha = 0f;
            _bannerTimer = -1f;
        }
    }
}
