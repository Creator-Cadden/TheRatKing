using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles pause/resume. All cursor state goes through CursorManager.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Pause UI")]
    public GameObject pauseMenuUI;

    public bool IsPaused { get; private set; } = false;

    // Cursor owner key — must be unique across all systems
    private const string CURSOR_OWNER = "pause";

    void Start()
    {
        // Game starts playing — no cursor request, CursorManager defaults to locked
        CursorManager.Release(CURSOR_OWNER);
    }

    // Bound to your Escape / Pause action in the Input Action Asset
    public void OnPause(InputValue value)
    {
        if (value.isPressed)
            TogglePause();
    }

    public void TogglePause()
    {
        if (IsPaused)
            Resume();
        else
            Pause();
    }

    public void Pause()
    {
        IsPaused = true;

        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(true);

        Time.timeScale = 0f;
        CursorManager.Request(CURSOR_OWNER);
    }

    public void Resume()
    {
        IsPaused = false;

        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(false);

        Time.timeScale = 1f;
        CursorManager.Release(CURSOR_OWNER);
    }

    void OnDestroy()
    {
        // Make sure we don't leave a dangling request if this object is destroyed
        CursorManager.Release(CURSOR_OWNER);
    }
}