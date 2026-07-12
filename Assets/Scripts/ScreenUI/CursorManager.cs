using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton that owns ALL cursor lock/visibility state for the entire game.
/// </summary>
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    // Which systems are currently requesting the cursor be visible
    private readonly HashSet<string> _requests = new HashSet<string>();

    [Header("Debug — read only")]
    [SerializeField] private string _activeRequests = "none";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Game starts locked
        ApplyCursorState();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Register that 'owner' needs the cursor visible and unlocked.
    /// </summary>
    public static void Request(string owner)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[CursorManager] No CursorManager in scene!");
            return;
        }
        Instance._requests.Add(owner);
        Instance.ApplyCursorState();
    }

    /// <summary>
    /// Unregister 'owner' — cursor locks again if nobody else needs it.
    /// </summary>
    public static void Release(string owner)
    {
        if (Instance == null) return;
        Instance._requests.Remove(owner);
        Instance.ApplyCursorState();
    }

    /// <summary>
    /// Release all requests and force-lock. Call on scene load / game restart.
    /// </summary>
    public static void ForceReset()
    {
        if (Instance == null) return;
        Instance._requests.Clear();
        Instance.ApplyCursorState();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void ApplyCursorState()
    {
        bool needsCursor = _requests.Count > 0;

        Cursor.lockState = needsCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = needsCursor;

        _activeRequests = _requests.Count > 0
            ? string.Join(", ", _requests)
            : "none";

        Debug.Log($"[CursorManager] Locked={!needsCursor}  Requests=[{_activeRequests}]");
    }
}
