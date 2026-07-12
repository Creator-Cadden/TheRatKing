using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles the Credits scene's back button. Sits on any GameObject in the
/// Credits scene (e.g. the canvas root) and routes a button click back to
/// the MainMenu scene.
/// </summary>
public class CreditsScreen : MonoBehaviour
{
    [Header("Button")]
    [Tooltip("Back button in the Credits scene. Clicking it returns to MainMenu.")]
    public Button backButton;

    [Header("Scenes")]
    [Tooltip("Scene to load when the back button is clicked. Must match the " +
             "main menu scene's filename in Build Settings.")]
    public string mainMenuScene = "MainMenu";

    [Header("Options")]
    [Tooltip("If on, pressing Escape also returns to the main menu.")]
    public bool allowEscapeKey = true;

    void Start()
    {
        backButton?.onClick.AddListener(GoBack);

        // Use the same cursor "owner" pattern as the rest of your menus so
        // the cursor stays visible while in the Credits scene.
        CursorManager.Request("credits");
        Time.timeScale = 1f;
    }

    void OnDestroy()
    {
        CursorManager.Release("credits");
    }

    void Update()
    {
        // Quick escape-key support without needing the new Input System action.
        if (allowEscapeKey && Input.GetKeyDown(KeyCode.Escape))
            GoBack();
    }

    public void GoBack()
    {
        if (string.IsNullOrEmpty(mainMenuScene))
        {
            Debug.LogWarning("[CreditsScreen] Main Menu Scene field is empty.");
            return;
        }
        SceneManager.LoadScene(mainMenuScene);
    }
}
