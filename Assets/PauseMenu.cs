using UnityEngine;
using UnityEngine.InputSystem;

public class PauseMenu : MonoBehaviour
{
    [Header("Pause")]
    public GameObject pauseMenuUI; // drag your pause menu panel here (can be null for now)

    private bool _isPaused = false;

    void Start()
    {
        LockCursor();
    }

    // Hook this up to your Escape / Pause action in your Input Action asset
    public void OnPause(InputValue value)
    {
        if (value.isPressed)
            TogglePause();
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;

        if (_isPaused)
            Pause();
        else
            Resume();
    }

    private void Pause()
    {
        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(true);

        Time.timeScale = 0f;   // Freezes the game
        UnlockCursor();
    }

    private void Resume()
    {
        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(false);

        Time.timeScale = 1f;   // Unfreeze
        LockCursor();
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}