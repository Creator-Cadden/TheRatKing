using UnityEngine;

/// <summary>
/// Debug helper — logs on Start to confirm a scene's scripts are executing.
/// Currently placed in lvl5. Harmless; remove the component when no longer needed.
/// </summary>
public class SceneTest : MonoBehaviour
{
    void Start()
    {
        Debug.Log("SCENE IS RUNNING");
    }
}