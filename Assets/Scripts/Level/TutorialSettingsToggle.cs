using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds a UI Toggle to a TutorialSettings switch. Drop on a Toggle in your
/// settings panel and pick which part it controls: one for "Basic controls
/// tutorial", one for "Combat tutorial". Reads the saved value on enable and
/// writes changes back to PlayerPrefs immediately.
/// </summary>
[RequireComponent(typeof(Toggle))]
public class TutorialSettingsToggle : MonoBehaviour
{
    public enum Which { Basics, Combat }

    [Tooltip("Which tutorial part this toggle turns on/off.")]
    public Which setting = Which.Basics;

    private Toggle _toggle;

    void Awake() => _toggle = GetComponent<Toggle>();

    void OnEnable()
    {
        _toggle.SetIsOnWithoutNotify(
            setting == Which.Basics ? TutorialSettings.ShowBasics : TutorialSettings.ShowCombat);
        _toggle.onValueChanged.AddListener(Apply);
    }

    void OnDisable() => _toggle.onValueChanged.RemoveListener(Apply);

    private void Apply(bool on)
    {
        if (setting == Which.Basics) TutorialSettings.ShowBasics = on;
        else                         TutorialSettings.ShowCombat = on;
    }
}
