using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the weapon-aware tutorial as ONE continuous flow. It contains up to two
/// parts — MOVEMENT (basics) then COMBAT (chosen weapon) — but which parts run is
/// decided by TutorialSettings (settings-menu toggles), so it can start at combat
/// or be off entirely. Controls are KEY-based (cursor stays locked): tap a key to
/// continue a read prompt, and HOLD one skip key any time to skip the current part
/// (basics → combat → game). A fill bar builds while held so nothing skips by
/// accident. Movement drills auto-advance off PlayerMovement events; combat/info
/// steps advance with the continue key or an external objective (TutorialTrigger /
/// dummy calling NotifyObjective). When the flow ends (or is fully skipped) it
/// calls GameManager.FinishTutorial() to load the first real level.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    public enum Advance { Continue, Move, Jump, Roll, Objective }

    [System.Serializable]
    public class Step
    {
        [TextArea(2, 4)] public string prompt;
        public Advance advance = Advance.Continue;
        [Tooltip("Objective mode only: id a TutorialTrigger / dummy reports via NotifyObjective().")]
        public string objectiveId = "";
    }

    [Header("Prompt UI")]
    public CanvasGroup promptPanel;
    public TMP_Text    promptText;
    public TMP_Text    sectionLabel;      // "Movement" / "Combat"
    [Tooltip("Shown only on read steps, e.g. 'Press Enter to continue'.")]
    public TMP_Text    continueHint;

    [Header("Keys")]
    public Key continueKey = Key.Enter;   // NOT Space — Space is jump
    public Key skipKey     = Key.Tab;

    [Header("Hold-to-skip")]
    public float skipHold = 0.9f;
    [Tooltip("Filled Image (Image Type = Filled) that builds while holding the skip key.")]
    public Image    skipFill;
    public TMP_Text skipHint;

    [Header("Steps — leave empty to use built-in defaults")]
    public List<Step> movementSteps = new List<Step>();
    public List<Step> bladeSteps    = new List<Step>();
    public List<Step> hammerSteps   = new List<Step>();
    public List<Step> bowSteps      = new List<Step>();

    [Header("Movement drill tuning")]
    public float moveSpeedThreshold = 1.5f;
    public float moveHoldTime = 0.6f;

    // ── Runtime ──
    private struct Section { public string name; public List<Step> steps; }

    private PlayerMovement _move;
    private List<Section>  _sections = new List<Section>();
    private int   _section, _index;
    private Step  _current;
    private float _moveTimer, _skipTimer;
    private bool  _jumped, _rolled, _finished;

    void Awake()
    {
        Instance = this;
        EnsureDefaults();
    }

    void Start()
    {
        _move = FindFirstObjectByType<PlayerMovement>();
        if (_move != null)
        {
            _move.OnJumped      += () => _jumped = true;
            _move.OnRollStarted += () => _rolled = true;
        }

        if (skipHint != null) skipHint.text = $"Hold [{skipKey}] to skip";
        if (skipFill != null) skipFill.fillAmount = 0f;

        // Build the flow from the player's settings. The movement part needs a
        // player to complete its drills — if the scene has none yet, skip it so a
        // half-built scene can't soft-lock (the skip key still works regardless).
        _sections.Clear();
        if (TutorialSettings.ShowBasics && _move != null)
            _sections.Add(new Section { name = "Movement", steps = movementSteps });
        else if (TutorialSettings.ShowBasics)
            Debug.LogWarning("[TutorialManager] Basics on but no PlayerMovement in scene — skipping movement part.");
        if (TutorialSettings.ShowCombat)
            _sections.Add(new Section { name = "Combat", steps = CombatStepsForWeapon() });

        if (_sections.Count == 0) { Finish(); return; }   // nothing to show → straight to game
        BeginSection(0);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    private List<Step> CombatStepsForWeapon()
    {
        var w = GameManager.Instance != null
            ? GameManager.Instance.ChosenWeapon
            : EntityStats.WeaponType.Blade;
        switch (w)
        {
            case EntityStats.WeaponType.Hammer: return hammerSteps;
            case EntityStats.WeaponType.Bow:    return bowSteps;
            default:                            return bladeSteps;
        }
    }

    // ── Flow ──

    private void BeginSection(int section)
    {
        _section = section;
        _index   = 0;
        if (sectionLabel != null) sectionLabel.text = _sections[section].name;
        ShowStep();
    }

    private void ShowStep()
    {
        var list = _sections[_section].steps;
        if (list == null || _index >= list.Count) { NextSection(); return; }

        _current   = list[_index];
        _moveTimer = 0f;
        _jumped = _rolled = false;

        if (promptPanel != null) promptPanel.alpha = 1f;
        if (promptText  != null) promptText.text   = _current.prompt;

        if (continueHint != null)
        {
            bool read = _current.advance == Advance.Continue;
            continueHint.gameObject.SetActive(read);
            if (read) continueHint.text = $"Press [{continueKey}] to continue";
        }
    }

    void Update()
    {
        if (_finished) return;
        var kb = Keyboard.current;

        // Advance the current step.
        if (_current != null)
        {
            switch (_current.advance)
            {
                case Advance.Move:
                    if (_move != null && _move.HorizontalSpeed > moveSpeedThreshold)
                    {
                        _moveTimer += Time.deltaTime;
                        if (_moveTimer >= moveHoldTime) Next();
                    }
                    else _moveTimer = 0f;
                    break;

                case Advance.Jump:     if (_jumped) Next(); break;
                case Advance.Roll:     if (_rolled) Next(); break;
                case Advance.Continue:
                    if (kb != null && kb[continueKey].wasPressedThisFrame) Next();
                    break;
                // Objective advances via NotifyObjective().
            }
        }

        // Single hold-to-skip — skips the CURRENT part any time.
        bool held = kb != null && kb[skipKey].isPressed;
        if (held)
        {
            _skipTimer += Time.deltaTime;
            if (skipFill != null) skipFill.fillAmount = Mathf.Clamp01(_skipTimer / Mathf.Max(0.01f, skipHold));
            if (_skipTimer >= skipHold) { _skipTimer = 0f; if (skipFill != null) skipFill.fillAmount = 0f; SkipSection(); }
        }
        else
        {
            _skipTimer = 0f;
            if (skipFill != null) skipFill.fillAmount = 0f;
        }
    }

    /// <summary>Called by a TutorialTrigger zone or a training dummy on death.</summary>
    public void NotifyObjective(string id)
    {
        if (_current != null && _current.advance == Advance.Objective &&
            _current.objectiveId == id)
            Next();
    }

    private void Next() { _index++; ShowStep(); }

    private void NextSection()
    {
        if (_section + 1 < _sections.Count) BeginSection(_section + 1);
        else Finish();
    }

    /// <summary>Skip the current part → next part, or finish if it's the last.</summary>
    public void SkipSection()
    {
        if (_section + 1 < _sections.Count) BeginSection(_section + 1);
        else Finish();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _current  = null;
        if (promptPanel != null) promptPanel.alpha = 0f;

        if (GameManager.Instance != null) GameManager.Instance.FinishTutorial();
        else Debug.LogWarning("[TutorialManager] No GameManager — cannot leave tutorial.");
    }

    // ── Built-in default steps (used when a list is left empty) ──

    private void EnsureDefaults()
    {
        if (movementSteps.Count == 0)
        {
            movementSteps.Add(new Step { prompt = "Use W A S D to move around.", advance = Advance.Move });
            movementSteps.Add(new Step { prompt = "Press Space to jump.",          advance = Advance.Jump });
            movementSteps.Add(new Step { prompt = "Dodge-roll to avoid danger.",   advance = Advance.Roll });
            movementSteps.Add(new Step { prompt = "Nice. Hold Shift to sprint when you need speed.\nReady for combat?", advance = Advance.Continue });
        }
        if (bladeSteps.Count == 0)
        {
            bladeSteps.Add(new Step { prompt = "BLADE — click Left Mouse to slash. It's fast and free, so chain your hits." });
            bladeSteps.Add(new Step { prompt = "Jump, then Left Mouse for a spinning air attack that hits all around you." });
        }
        if (hammerSteps.Count == 0)
        {
            hammerSteps.Add(new Step { prompt = "HAMMER — Left Mouse for a heavy swing. Slow, but three times a blade's power." });
            hammerSteps.Add(new Step { prompt = "Jump, then Left Mouse to slam the ground in a 360° smash." });
        }
        if (bowSteps.Count == 0)
        {
            bowSteps.Add(new Step { prompt = "BOW — hold Right Mouse to aim, hold Left Mouse to charge, release to fire.\nA full charge triples your damage." });
            bowSteps.Add(new Step { prompt = "Jump, then Left Mouse to loose a 3-arrow burst at the ground below." });
        }
    }
}
