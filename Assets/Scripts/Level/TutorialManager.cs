using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Drives the "1_T Tutorial" scene: PHASES → STEPS → OBJECTIVES.
///
/// A step is one beat. It either shows a line on the centre prompt panel and
/// waits for a key (story beats), or it puts one or more OBJECTIVE CARDS on the
/// right of the screen and waits for them all to be done. Cards complete in any
/// order, slide away as they're finished, and the ones below move up — so the
/// movement lesson can offer "move / jump / sprint / dash" all at once and let
/// the player poke at them in whatever order they like.
///
/// Every gate is an explicit signal out of the real movement/combat code — no
/// "they've probably got it by now" timers — so the tutorial can't teach
/// something the game stopped doing.
///
/// Default flow:
///   1. MOVEMENT (skippable) — one step, four cards: WASD · jump · sprint · dash
///   2. COMBAT   (skippable) — drop a passive dummy, then basic attacks → jump
///      attack → aim (prompts swap per weapon), kill it, XP + gold, stat menu
///   3. ENEMIES             — a grunt (basic attacker), a balloon rat (decals,
///      explained as a family: circles, cones, rectangles, drops), then a
///      light-armour + heavy-armour pair for the armour-class lesson: hit each
///      once, read the difference, jump-attack each, finish them
///   4. End prompt: restart the tutorial, or continue to the first level.
///
/// Needs in the scene: ObjectiveListUI, ScreenFader, the prompt panel widgets,
/// and one empty Transform per spawn.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // ── Types ─────────────────────────────────────────────────────────────────

    public enum Gate
    {
        MoveDirections,    // push forward / back / left / right (the WASD drill)
        MoveHold,          // keep moving for holdTime
        Jump,              // jump requiredCount times
        Sprint,            // sprint for holdTime
        Dash,              // dodge-roll requiredCount times
        BasicAttack,       // requiredCount grounded attacks
        JumpAttack,        // requiredCount air attacks
        Aim,               // hold aim for holdTime
        StatMenu,          // open the stat menu
        WaveCleared,       // every enemy in waveIndex is dead
        HitEachTarget,     // land one ordinary hit on every enemy in waveIndex
        JumpHitEachTarget, // land one air-attack hit on every enemy in waveIndex
        Objective          // something calls NotifyObjective(id)
    }

    public enum SettingsGate { Always, ShowBasics, ShowCombat }

    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Tooltip("Empty Transform on the floor. The enemy falls to exactly here.")]
        public Transform point;
        [Tooltip("Optional label for your own sanity, e.g. \"low toughness\".")]
        public string note = "";
    }

    [System.Serializable]
    public class Wave
    {
        public string id = "wave";
        [Tooltip("One entry per enemy — mix prefabs freely within a wave.")]
        public List<SpawnEntry> spawns = new List<SpawnEntry>();

        [Header("Drop-in")]
        public float dropHeight = 8f;
        public float dropSpeed  = 16f;
        public GameObject landFx;
        [Tooltip("Beat before the first one drops, so a wave doesn't appear the " +
                 "instant the step begins.")]
        public float initialDelay = 1.2f;
        [Tooltip("Beat between each enemy in this wave — they arrive one at a time " +
                 "instead of raining down together.")]
        public float betweenDelay = 0.9f;

        [Header("Behaviour")]
        [Tooltip("Never attacks — a harmless training dummy.")]
        public bool passive = false;
        [Tooltip("Never moves once it lands. It still turns to face you and still " +
                 "flinches and staggers — it just won't walk. Use it for the dummy " +
                 "and the toughness pair.")]
        public bool stationary = false;
        [Tooltip("Bolted to the floor. Knockback and launch move it zero distance " +
                 "and it's pinned to its spawn point, so no amount of punching can " +
                 "walk a training dummy out of the room. Stationary alone doesn't " +
                 "cover this — knockback bypasses the AI entirely.")]
        public bool immovable = false;
        [Tooltip("Pins its health floor at 1 so a drill can't end early. Released " +
                 "by a step's Release Wave Index, or by Kill Wave Index.")]
        public bool invulnerableWhileTeaching = false;

        [Header("Rewards override")]
        [Tooltip("XP this wave's kills pay out, beating the stat block. 0 = normal. " +
                 "Set to one level's worth (10) on the dummy so the level-up and " +
                 "stat-spend lesson actually has a point to spend.")]
        public int xpOverride = 0;
        [Tooltip("Rat Coin this wave's kills pay out. 0 = normal.")]
        public int coinOverride = 0;

        [Tooltip("Add the over-head armour chevrons to this wave's enemies if their " +
                 "prefab doesn't already have them.")]
        public bool showToughnessChevrons = true;

        [System.NonSerialized] public readonly List<EntityStats> live = new List<EntityStats>();
        [System.NonSerialized] public bool spawned;
        [System.NonSerialized] public int  initialCount;   // how many actually spawned
    }

    [System.Serializable]
    public class Objective
    {
        [Tooltip("The line on the card, e.g. \"Press the keys to move\".")]
        public string text = "";
        public Gate gate = Gate.Jump;

        [Tooltip("Icon library ids, left to right — \"W\",\"S\",\"A\",\"D\" for the " +
                 "move drill (that order: forward, back, left, right).")]
        public List<string> iconIds = new List<string>();

        [Header("Gate tuning")]
        [Tooltip("Counting gates: Jump, Dash, BasicAttack, JumpAttack.")]
        public int requiredCount = 1;
        [Tooltip("Timed gates: MoveHold, Sprint, Aim.")]
        public float holdTime = 0.6f;
        [Tooltip("Wave gates: WaveCleared, HitEachTarget, JumpHitEachTarget.")]
        public int waveIndex = -1;
        [Tooltip("Objective gate only.")]
        public string objectiveId = "";

        [Tooltip("None = every weapon. Otherwise the card is skipped unless the " +
                 "player picked this weapon in PlayerCustom.")]
        public EntityStats.WeaponType weaponOnly = EntityStats.WeaponType.None;

        // runtime
        [System.NonSerialized] public bool   done;
        [System.NonSerialized] public string cardId;
        [System.NonSerialized] public float  holdTimer;
        [System.NonSerialized] public int    count;
        [System.NonSerialized] public bool[] dirHit;
        [System.NonSerialized] public int    lastShownProgress;
    }

    [System.Serializable]
    public class Step
    {
        [TextArea(2, 4)]
        [Tooltip("Centre panel line. With no objectives this IS the step and it " +
                 "waits for the continue key (or Auto Advance After).")]
        public string prompt = "";
        [Tooltip("Read steps only: > 0 advances on its own after this long.")]
        public float autoAdvanceAfter = 0f;

        [Header("Per-weapon prompt (optional)")]
        [Tooltip("Appended to Prompt when the player is holding this weapon. Leave " +
                 "empty for none. This is how one step says something specific " +
                 "about your weapon without needing three copies of the step.")]
        [TextArea(2, 4)] public string promptBlade  = "";
        [TextArea(2, 4)] public string promptHammer = "";
        [TextArea(2, 4)] public string promptBow    = "";

        /// <summary>Prompt plus whichever weapon tail applies.</summary>
        public string PromptFor(EntityStats.WeaponType weapon)
        {
            string tail = weapon switch
            {
                EntityStats.WeaponType.Blade  => promptBlade,
                EntityStats.WeaponType.Hammer => promptHammer,
                EntityStats.WeaponType.Bow    => promptBow,
                _                             => ""
            };

            if (string.IsNullOrWhiteSpace(tail))   return prompt;
            if (string.IsNullOrWhiteSpace(prompt)) return tail;
            return prompt + "\n\n" + tail;
        }

        [Tooltip("Cards shown on the right. All must finish for the step to end.")]
        public List<Objective> objectives = new List<Objective>();

        [Tooltip("None = every weapon. Otherwise the WHOLE step is skipped unless " +
                 "the player picked this weapon — the way to give a read prompt " +
                 "per weapon, since a prompt has no objective to filter.")]
        public EntityStats.WeaponType weaponOnly = EntityStats.WeaponType.None;

        [Header("Scene actions (fire when the step begins)")]
        public bool fadeResetBefore = false;
        [Tooltip("Index into Waves. -1 = none.")]
        public int spawnWaveIndex = -1;
        [Tooltip("Stop protecting an invulnerable wave so it can be killed. -1 = none.")]
        public int releaseWaveIndex = -1;
        [Tooltip("Kill everything in this wave so XP and gold drop. -1 = none.")]
        public int killWaveIndex = -1;

        [Header("Hooks")]
        public UnityEvent onBegin;
        public UnityEvent onComplete;
    }

    [System.Serializable]
    public class Phase
    {
        public string name = "Movement";
        [Tooltip("Can the player hold the skip key to jump past this phase?")]
        public bool skippable = true;
        public SettingsGate settingsGate = SettingsGate.ShowBasics;
        public List<Step> steps = new List<Step>();
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Prompt panel (centre — story beats only)")]
    public CanvasGroup promptPanel;
    public TMP_Text    promptText;
    public TMP_Text    sectionLabel;
    public TMP_Text    continueHint;

    [Header("Objective list (right side)")]
    public ObjectiveListUI objectiveList;

    [Header("End prompt")]
    [Tooltip("Buttons wire to RestartTutorial() and ContinueToGame().")]
    public GameObject endPromptPanel;

    [Header("Keys")]
    // Checked against InputSystem_Actions so nothing double-fires. Taken already:
    // Space = Jump, Enter = Attack (!), Tab = Stat Menu (!), Ctrl/C = Roll,
    // LShift = Sprint, F = Interact, LMB = Attack, RMB = Aim. E and Backspace are free.
    [Tooltip("Do NOT use Enter — it's a second Attack binding.")]
    public Key continueKey = Key.E;
    [Tooltip("Do NOT use Tab — it opens the stat menu.")]
    public Key skipKey = Key.Backspace;

    [Header("Hold-to-skip")]
    public float skipHold = 0.9f;
    public UnityEngine.UI.Image skipFill;
    public TMP_Text skipHint;

    [Header("Scene refs")]
    [Tooltip("Where fade-resets put the player. Empty = wherever they started.")]
    public Transform startPoint;
    public StatMenuUI statMenu;

    [Header("Waves")]
    public List<Wave> waves = new List<Wave>();

    [Header("Phases — leave empty to use the built-in default flow")]
    public List<Phase> phases = new List<Phase>();

    [Header("Tuning")]
    public float moveSpeedThreshold = 1.5f;
    [Tooltip("How far a key/stick has to push before a direction icon counts.")]
    public float directionDeadzone = 0.5f;
    [Tooltip("A hit landing this soon after an air attack counts as a jump hit.")]
    public float jumpHitWindow = 1.0f;
    [Tooltip("Pause after a step completes before the next one begins.")]
    public float stepGapSeconds = 0.35f;

    [Tooltip("Floor the player's health at 1 for the whole tutorial — they can be " +
             "hurt and learn to respect a hit, but a lesson never ends in a death " +
             "screen. Cleared the moment they leave for the first real level.")]
    public bool playerCannotDie = true;

    [Tooltip("Refill stamina at the start of every step. Jump attacks and dashes " +
             "cost stamina and fail SILENTLY at zero, which reads as 'the tutorial " +
             "is broken'. Leave this on.")]
    public bool refillStaminaOnStep = true;

    [Tooltip("Log every objective as it begins and completes. Turn on when a card " +
             "won't clear and you need to see whether the gate is firing.")]
    public bool verboseGates = false;

    [Tooltip("While an explanation is on screen waiting for the continue key, " +
             "nothing takes damage — not the player, not the enemies. The player " +
             "can read without being chewed on, and their idle clicking can't kill " +
             "the thing being explained.")]
    public bool freezeDamageDuringPrompts = true;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private PlayerMovement _move;
    private PlayerCombat   _combat;
    private EntityStats    _playerStats;

    private readonly List<Phase> _active = new List<Phase>();
    private int   _phaseIndex, _stepIndex;
    private Phase _currentPhase;
    private Step  _currentStep;
    private readonly List<Objective> _liveObjectives = new List<Objective>();

    private Vector3    _startPos;
    private Quaternion _startRot;

    private float _skipTimer, _stepClock, _lastJumpAttackTime = -999f;
    private int   _jumpCount, _dashCount, _basicCount, _airCount;
    private string _objectiveSeen = "";

    private readonly HashSet<EntityStats> _hitBasic = new HashSet<EntityStats>();
    private readonly HashSet<EntityStats> _hitJump  = new HashSet<EntityStats>();

    private bool _running, _finished, _busy, _awaitingRead;
    private readonly List<Wave> _phaseWaves = new List<Wave>();
    private int _cardSerial;
    private Coroutine _spawnRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        if (phases.Count == 0) BuildDefaultFlow();
    }

    void Start()
    {
        CachePlayer();

        _startPos = startPoint != null ? startPoint.position
                  : (_move != null ? _move.transform.position : transform.position);
        _startRot = startPoint != null ? startPoint.rotation
                  : (_move != null ? _move.transform.rotation : transform.rotation);

        if (skipHint != null) skipHint.text = $"Hold [{skipKey}] to skip";
        if (skipFill != null) skipFill.fillAmount = 0f;
        if (endPromptPanel != null) endPromptPanel.SetActive(false);
        HidePrompt();

        if (objectiveList == null) objectiveList = FindFirstObjectByType<ObjectiveListUI>();
        if (objectiveList == null)
            Debug.LogWarning("[TutorialManager] No ObjectiveListUI in scene — objective cards won't show.");

        ApplyPlayerSafety(playerCannotDie);
        BeginRun();
    }

    void OnDestroy()
    {
        UnhookPlayer();
        ApplyPlayerSafety(false);   // never let the floor survive the scene
        SetDamageFrozen(false);     // nor a freeze — it's static
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Health floor on the player. 1 means hits still hurt, still knock you
    /// about, still flash the vignette — you just can't be killed by a lesson.
    /// </summary>
    private void ApplyPlayerSafety(bool on)
    {
        if (_playerStats == null) return;
        _playerStats.HealthFloor = on ? 1 : 0;
    }

    /// <summary>
    /// The general no-damage period. It's a static on EntityStats rather than a
    /// per-entity flag so one call covers everything in the scene, including
    /// enemies that spawn while it's raised.
    /// </summary>
    private void SetDamageFrozen(bool frozen)
    {
        EntityStats.SuppressAllDamage = frozen;
    }

    private void CachePlayer()
    {
        _move   = FindFirstObjectByType<PlayerMovement>();
        _combat = FindFirstObjectByType<PlayerCombat>();
        _playerStats = _move != null ? _move.GetComponent<EntityStats>() : null;

        if (_move != null)
        {
            _move.OnJumped      += OnPlayerJumped;
            _move.OnRollStarted += OnPlayerDashed;
        }
        else Debug.LogWarning("[TutorialManager] No PlayerMovement — movement gates can't complete.");

        if (_combat != null)
        {
            _combat.OnBasicAttack += OnPlayerBasicAttack;
            _combat.OnJumpAttack  += OnPlayerJumpAttack;
        }
        else Debug.LogWarning("[TutorialManager] No PlayerCombat — combat gates can't complete.");

        if (statMenu == null) statMenu = FindFirstObjectByType<StatMenuUI>(FindObjectsInactive.Include);
    }

    private void UnhookPlayer()
    {
        if (_move != null)
        {
            _move.OnJumped      -= OnPlayerJumped;
            _move.OnRollStarted -= OnPlayerDashed;
        }
        if (_combat != null)
        {
            _combat.OnBasicAttack -= OnPlayerBasicAttack;
            _combat.OnJumpAttack  -= OnPlayerJumpAttack;
        }
    }

    private void OnPlayerJumped()      => _jumpCount++;
    private void OnPlayerDashed()      => _dashCount++;
    private void OnPlayerBasicAttack() => _basicCount++;
    private void OnPlayerJumpAttack()
    {
        _airCount++;
        _lastJumpAttackTime = Time.time;
    }

    /// <summary>
    /// Every spawned enemy reports its damage here, and we decide whether it came
    /// from the air.
    ///
    /// Watching for the OnJumpAttack event is NOT enough: an air attack deals its
    /// damage inside the weapon call, before PlayerCombat gets to raise the
    /// event — so the enemy's damage arrives while the "last jump attack" stamp
    /// is still from the previous one. PlayerCombat therefore opens a window
    /// BEFORE it asks the weapon, and that's what we read. Being airborne counts
    /// too, as a belt-and-braces second signal.
    /// </summary>
    private void OnEnemyDamaged(EntityStats st)
    {
        if (st == null) return;

        bool fromAir =
            (_combat != null && _combat.InJumpAttackWindow) ||
            (_move   != null && !_move.IsGrounded) ||
            (Time.time - _lastJumpAttackTime <= jumpHitWindow);

        if (fromAir) _hitJump.Add(st);
        else         _hitBasic.Add(st);

        if (verboseGates)
            Debug.Log($"[Tutorial] hit '{st.name}' — {(fromAir ? "AIR" : "ground")}");
    }

    // ── Run control ───────────────────────────────────────────────────────────

    private void BeginRun()
    {
        _finished = false;
        _active.Clear();

        foreach (Phase p in phases)
        {
            if (p == null || p.steps == null || p.steps.Count == 0) continue;
            if (p.settingsGate == SettingsGate.ShowBasics && !TutorialSettings.ShowBasics) continue;
            if (p.settingsGate == SettingsGate.ShowCombat && !TutorialSettings.ShowCombat) continue;
            _active.Add(p);
        }

        if (_active.Count == 0) { Finish(); return; }

        _running = true;
        BeginPhase(0);
    }

    private void BeginPhase(int index)
    {
        EndPhaseCleanup();

        _phaseIndex   = index;
        _currentPhase = _active[index];
        _stepIndex    = -1;
        if (sectionLabel != null) sectionLabel.text = _currentPhase.name;
        AdvanceStep();
    }

    private void AdvanceStep()
    {
        List<Step> steps = _currentPhase.steps;
        EntityStats.WeaponType weapon = ChosenWeapon();

        // Skip nulls and steps meant for a weapon the player didn't pick.
        do { _stepIndex++; }
        while (_stepIndex < steps.Count &&
               (steps[_stepIndex] == null ||
                (steps[_stepIndex].weaponOnly != EntityStats.WeaponType.None &&
                 steps[_stepIndex].weaponOnly != weapon)));

        if (_stepIndex >= steps.Count) { NextPhase(); return; }

        StartCoroutine(BeginStep(steps[_stepIndex]));
    }

    private IEnumerator BeginStep(Step step)
    {
        _busy         = true;
        _currentStep  = null;
        _awaitingRead = false;
        _liveObjectives.Clear();

        // Thaw FIRST. Kill Wave Index goes through TakeDamage, so a freeze left
        // over from the previous explanation would silently swallow the kill.
        SetDamageFrozen(false);

        if (step.fadeResetBefore) yield return ResetPlayerBehindFade();

        // Scene actions first, so the player reads the prompt with the enemy
        // already on its way down.
        if (step.spawnWaveIndex   >= 0) SpawnWave(step.spawnWaveIndex);
        if (step.releaseWaveIndex >= 0) ReleaseWave(step.releaseWaveIndex);
        if (step.killWaveIndex    >= 0) KillWave(step.killWaveIndex);

        // Build this step's cards, dropping ones meant for another weapon.
        EntityStats.WeaponType weapon = ChosenWeapon();
        foreach (Objective o in step.objectives)
        {
            if (o == null) continue;
            if (o.weaponOnly != EntityStats.WeaponType.None && o.weaponOnly != weapon) continue;
            PrepareObjective(o);
            _liveObjectives.Add(o);
        }

        if (refillStaminaOnStep && _playerStats != null) _playerStats.RestoreStamina();

        _currentStep = step;
        _stepClock   = 0f;

        bool   readStep = _liveObjectives.Count == 0;
        string body     = step.PromptFor(weapon);
        if (!string.IsNullOrWhiteSpace(body))
        {
            ShowPrompt(body, showContinueHint: readStep && step.autoAdvanceAfter <= 0f);
        }
        else HidePrompt();

        _awaitingRead = readStep;

        // A read step is a beat where the game politely stops hitting you.
        SetDamageFrozen(readStep && freezeDamageDuringPrompts);

        // Cards last so they slide in after the prompt has settled.
        foreach (Objective o in _liveObjectives) SpawnCard(o);

        step.onBegin?.Invoke();
        _busy = false;
    }

    private void PrepareObjective(Objective o)
    {
        o.done      = false;
        o.holdTimer = 0f;
        o.count     = 0;
        o.lastShownProgress = -1;
        o.cardId    = "obj_" + (_cardSerial++);
        o.dirHit    = new bool[4];

        // Counting gates start from now, not from whatever the player did earlier.
        switch (o.gate)
        {
            case Gate.Jump:        _jumpCount  = 0; break;
            case Gate.Dash:        _dashCount  = 0; break;
            case Gate.BasicAttack: _basicCount = 0; break;
            case Gate.JumpAttack:  _airCount   = 0; break;
            case Gate.HitEachTarget:     _hitBasic.Clear(); break;
            case Gate.JumpHitEachTarget: _hitJump.Clear();  break;
            case Gate.Objective:   _objectiveSeen = ""; break;
        }
    }

    private void SpawnCard(Objective o)
    {
        if (objectiveList == null) return;
        objectiveList.Add(o.cardId, o.text, o.iconIds);
        objectiveList.SetCounter(o.cardId, 0, RequiredTotal(o));
        if (verboseGates) Debug.Log($"[Tutorial] … {o.gate} — \"{o.text}\"");
    }

    private void CompleteStep()
    {
        Step done = _currentStep;
        _currentStep  = null;
        _awaitingRead = false;
        HidePrompt();
        done?.onComplete?.Invoke();
        StartCoroutine(GapThenAdvance());
    }

    private IEnumerator GapThenAdvance()
    {
        _busy = true;
        if (stepGapSeconds > 0f) yield return new WaitForSeconds(stepGapSeconds);
        _busy = false;
        AdvanceStep();
    }

    private void NextPhase()
    {
        if (_phaseIndex + 1 < _active.Count) BeginPhase(_phaseIndex + 1);
        else Finish();
    }

    /// <summary>Skip the current phase. Ignored if the phase isn't skippable.</summary>
    public void SkipPhase()
    {
        if (!_running || _finished) return;
        if (_currentPhase != null && !_currentPhase.skippable) return;
        _currentStep = null;
        _awaitingRead = false;
        objectiveList?.CompleteAll();
        HidePrompt();
        NextPhase();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!_running || _finished) return;
        Keyboard kb = Keyboard.current;

        // Hold-to-skip, offered only while the phase allows it.
        bool canSkip = _currentPhase != null && _currentPhase.skippable;
        if (skipHint != null && skipHint.gameObject.activeSelf != canSkip)
            skipHint.gameObject.SetActive(canSkip);

        if (canSkip && kb != null && kb[skipKey].isPressed)
        {
            _skipTimer += Time.deltaTime;
            if (skipFill != null) skipFill.fillAmount = Mathf.Clamp01(_skipTimer / Mathf.Max(0.01f, skipHold));
            if (_skipTimer >= skipHold)
            {
                _skipTimer = 0f;
                if (skipFill != null) skipFill.fillAmount = 0f;
                SkipPhase();
                return;
            }
        }
        else
        {
            _skipTimer = 0f;
            if (skipFill != null) skipFill.fillAmount = 0f;
        }

        if (_busy || _currentStep == null) return;
        _stepClock += Time.deltaTime;

        // Read step — prompt panel only.
        if (_awaitingRead)
        {
            // A step with no prompt and no cards is pure scene action — don't
            // sit there waiting for a key on a blank screen.
            if (string.IsNullOrWhiteSpace(_currentStep.PromptFor(ChosenWeapon())))
            { CompleteStep(); return; }

            bool timedOut = _currentStep.autoAdvanceAfter > 0f
                         && _stepClock >= _currentStep.autoAdvanceAfter;
            bool pressed  = _currentStep.autoAdvanceAfter <= 0f
                         && kb != null && kb[continueKey].wasPressedThisFrame;
            if (timedOut || pressed) CompleteStep();
            return;
        }

        // Objective step — tick every unfinished card.
        bool all = true;
        foreach (Objective o in _liveObjectives)
        {
            if (o.done) continue;
            if (EvaluateGate(o))
            {
                o.done = true;
                objectiveList?.Complete(o.cardId);
                if (verboseGates) Debug.Log($"[Tutorial] ✔ {o.gate} — \"{o.text}\"");
            }
            else
            {
                PushProgressToCard(o);
                all = false;
            }
        }
        if (all) CompleteStep();
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    private bool EvaluateGate(Objective o)
    {
        switch (o.gate)
        {
            case Gate.MoveDirections:
            {
                if (_move == null) return false;
                Vector2 m = _move.MoveInput;
                if (m.y >  directionDeadzone) o.dirHit[0] = true;   // forward
                if (m.y < -directionDeadzone) o.dirHit[1] = true;   // back
                if (m.x < -directionDeadzone) o.dirHit[2] = true;   // left
                if (m.x >  directionDeadzone) o.dirHit[3] = true;   // right
                return o.dirHit[0] && o.dirHit[1] && o.dirHit[2] && o.dirHit[3];
            }

            case Gate.MoveHold:
                if (_move != null && _move.HorizontalSpeed > moveSpeedThreshold)
                     o.holdTimer += Time.deltaTime;
                else o.holdTimer  = 0f;
                return o.holdTimer >= o.holdTime;

            case Gate.Sprint:
                if (_move != null && _move.IsSprinting) o.holdTimer += Time.deltaTime;
                else o.holdTimer = 0f;
                return o.holdTimer >= o.holdTime;

            case Gate.Aim:
                if (_combat != null && _combat.IsAiming) o.holdTimer += Time.deltaTime;
                else o.holdTimer = 0f;
                return o.holdTimer >= o.holdTime;

            case Gate.Jump:        o.count = _jumpCount;  return o.count >= RequiredTotal(o);
            case Gate.Dash:        o.count = _dashCount;  return o.count >= RequiredTotal(o);
            case Gate.BasicAttack: o.count = _basicCount; return o.count >= RequiredTotal(o);
            case Gate.JumpAttack:  o.count = _airCount;   return o.count >= RequiredTotal(o);

            case Gate.StatMenu:
                return statMenu != null && statMenu.statMenuRoot != null
                    && statMenu.statMenuRoot.activeInHierarchy;

            case Gate.WaveCleared:
            {
                Wave w = WaveAt(o.waveIndex);
                if (w == null) return true;
                if (!w.spawned) return false;
                PruneWave(w);
                o.count = TargetCount(o) - w.live.Count;
                return w.live.Count == 0;
            }

            case Gate.HitEachTarget:
            case Gate.JumpHitEachTarget:
            {
                Wave w = WaveAt(o.waveIndex);
                if (w == null) return true;
                if (!w.spawned) return false;
                PruneWave(w);
                HashSet<EntityStats> set = o.gate == Gate.HitEachTarget ? _hitBasic : _hitJump;
                int hit = 0;
                foreach (EntityStats st in w.live) if (set.Contains(st)) hit++;
                o.count = hit;
                return w.live.Count > 0 && hit >= w.live.Count;
            }

            case Gate.Objective:
                return _objectiveSeen == o.objectiveId && !string.IsNullOrEmpty(o.objectiveId);
        }
        return false;
    }

    /// <summary>How many "units" this objective needs — used for the card counter.</summary>
    private int RequiredTotal(Objective o)
    {
        switch (o.gate)
        {
            case Gate.MoveDirections: return 4;
            case Gate.Jump:
            case Gate.Dash:
            case Gate.BasicAttack:
            case Gate.JumpAttack:     return Mathf.Max(1, o.requiredCount);
            case Gate.WaveCleared:
            case Gate.HitEachTarget:
            case Gate.JumpHitEachTarget: return TargetCount(o);
            default: return 1;
        }
    }

    private int TargetCount(Objective o)
    {
        Wave w = WaveAt(o.waveIndex);
        if (w == null) return 1;
        // initialCount, not live.Count — otherwise "2 of 2 killed" would read
        // "0 of 0" as the wave empties.
        if (w.spawned) return Mathf.Max(1, w.initialCount);
        return Mathf.Max(1, w.spawns != null ? w.spawns.Count : 1);
    }

    /// <summary>
    /// Feed the card its progress. Direction icons map one-to-one; otherwise, if
    /// the number of icons happens to match the number of things to do, they light
    /// up one per unit (three LMB icons for a three-hit combo). If they don't
    /// match, the icons just say "which key" and the counter carries the progress.
    /// </summary>
    private void PushProgressToCard(Objective o)
    {
        if (objectiveList == null) return;

        if (o.gate == Gate.MoveDirections)
        {
            for (int i = 0; i < 4; i++) objectiveList.MarkIcon(o.cardId, i, o.dirHit[i]);
            int n = 0;
            for (int i = 0; i < 4; i++) if (o.dirHit[i]) n++;
            if (n != o.lastShownProgress)
            {
                objectiveList.SetCounter(o.cardId, n, 4);
                o.lastShownProgress = n;
            }
            return;
        }

        int total = RequiredTotal(o);
        int have  = Mathf.Clamp(o.count, 0, total);
        if (have == o.lastShownProgress) return;
        o.lastShownProgress = have;

        if (o.iconIds != null && o.iconIds.Count == total && total > 1)
            for (int i = 0; i < total; i++) objectiveList.MarkIcon(o.cardId, i, i < have);

        objectiveList.SetCounter(o.cardId, have, total);
    }

    /// <summary>Called by a TutorialTrigger zone or any script with an objective id.</summary>
    public void NotifyObjective(string id) => _objectiveSeen = id;

    // ── Waves ─────────────────────────────────────────────────────────────────

    private Wave WaveAt(int index)
        => (index >= 0 && index < waves.Count) ? waves[index] : null;

    /// <summary>
    /// Kicks off a wave. The enemies arrive on a timer rather than all at once —
    /// a beat before the first, then one at a time — so a pair reads as two
    /// separate arrivals instead of a single thud.
    /// </summary>
    private void SpawnWave(int index)
    {
        Wave w = WaveAt(index);
        if (w == null) return;
        if (w.spawns == null || w.spawns.Count == 0)
        {
            Debug.LogWarning($"[TutorialManager] Wave '{w.id}' has no spawns — " +
                             "drop enemy prefabs into the Waves list on this component.");
            return;
        }

        w.live.Clear();
        w.spawned      = false;   // wave gates must not fire mid-arrival
        w.initialCount = 0;
        if (!_phaseWaves.Contains(w)) _phaseWaves.Add(w);

        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(SpawnWaveRoutine(w));
    }

    private IEnumerator SpawnWaveRoutine(Wave w)
    {
        if (w.initialDelay > 0f) yield return new WaitForSeconds(w.initialDelay);

        bool first = true;
        foreach (SpawnEntry e in w.spawns)
        {
            if (e == null || e.prefab == null) continue;

            if (!first && w.betweenDelay > 0f) yield return new WaitForSeconds(w.betweenDelay);
            first = false;

            Transform p = e.point != null ? e.point : transform;
            GameObject go = Instantiate(e.prefab, p.position, p.rotation);

            EntityStats st = go.GetComponent<EntityStats>();
            if (st != null)
            {
                w.live.Add(st);
                st.onDamageTaken.AddListener(_ => OnEnemyDamaged(st));
            }
            else Debug.LogWarning($"[TutorialManager] '{e.prefab.name}' has no EntityStats — " +
                                  "wave gates on it will misbehave.");

            if (w.invulnerableWhileTeaching) go.AddComponent<TutorialDummyGuard>();

            if (w.showToughnessChevrons && go.GetComponent<ToughnessChevrons>() == null)
                go.AddComponent<ToughnessChevrons>();

            if (w.xpOverride > 0 || w.coinOverride > 0)
            {
                var drop = go.GetComponent<EnemyXPDrop>();
                if (drop != null)
                {
                    if (w.xpOverride   > 0) drop.xpOverride       = w.xpOverride;
                    if (w.coinOverride > 0) drop.currencyOverride = w.coinOverride;
                }
            }

            go.AddComponent<TutorialEnemyDrop>()
              .Begin(p.position, w.dropHeight, w.dropSpeed,
                     w.passive, w.stationary, w.immovable, w.landFx);
        }

        // Only now may a WaveCleared / HitEachTarget gate start counting.
        w.initialCount = w.live.Count;
        w.spawned      = true;
        _spawnRoutine  = null;
    }

    /// <summary>Stop protecting an invulnerable wave — the next hit kills.</summary>
    private void ReleaseWave(int index)
    {
        Wave w = WaveAt(index);
        if (w == null) return;
        PruneWave(w);
        foreach (EntityStats st in w.live)
            if (st != null) st.GetComponent<TutorialDummyGuard>()?.Release();
    }

    private void KillWave(int index)
    {
        Wave w = WaveAt(index);
        if (w == null) return;
        PruneWave(w);
        // TakeDamage rather than Destroy: the normal death path is what pays out
        // XP and gold, which is the whole point of this beat.
        foreach (EntityStats st in new List<EntityStats>(w.live))
        {
            if (st == null || st.IsDead) continue;
            st.GetComponent<TutorialDummyGuard>()?.Release();
            st.TakeDamage(999999);
        }
        PruneWave(w);
    }

    private void PruneWave(Wave w)
    {
        for (int i = w.live.Count - 1; i >= 0; i--)
            if (w.live[i] == null || w.live[i].IsDead) w.live.RemoveAt(i);
    }

    /// <summary>Remove anything this phase dropped in, so a skip leaves a clean room.</summary>
    private void EndPhaseCleanup()
    {
        // Stop a wave that's still arriving, or its stragglers would land into a
        // phase that's already over.
        if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }

        foreach (Wave w in _phaseWaves)
        {
            foreach (EntityStats st in w.live)
                if (st != null) Destroy(st.gameObject);
            w.live.Clear();
            w.spawned = false;
        }
        _phaseWaves.Clear();
        _hitBasic.Clear();
        _hitJump.Clear();
    }

    // ── Reset / fade ──────────────────────────────────────────────────────────

    private IEnumerator ResetPlayerBehindFade()
    {
        HidePrompt();

        if (ScreenFader.Instance != null)
        {
            ScreenFader.Instance.FadeThrough(PlacePlayerAtStart);
            yield return null;
            while (ScreenFader.Instance != null && ScreenFader.Instance.IsFading) yield return null;
        }
        else
        {
            PlacePlayerAtStart();
            yield return null;
        }
    }

    private void PlacePlayerAtStart()
    {
        if (_move != null)
        {
            _move.TeleportTo(_startPos);
            _move.transform.rotation = _startRot;
        }
        if (_playerStats != null && !_playerStats.IsDead) _playerStats.Heal(999999);
    }

    // ── Finish / restart ──────────────────────────────────────────────────────

    private void Finish()
    {
        if (_finished) return;
        _finished     = true;
        _running      = false;
        _currentStep  = null;
        _awaitingRead = false;

        EndPhaseCleanup();
        objectiveList?.Clear();
        HidePrompt();
        SetDamageFrozen(false);

        if (endPromptPanel != null)
        {
            endPromptPanel.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        else ContinueToGame();
    }

    /// <summary>Wire to the "Restart tutorial" button.</summary>
    public void RestartTutorial()
    {
        if (endPromptPanel != null) endPromptPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        StopAllCoroutines();
        _busy = false;
        objectiveList?.Clear();

        System.Action reset = () =>
        {
            EndPhaseCleanup();
            foreach (Wave w in waves) { w.live.Clear(); w.spawned = false; }
            PlacePlayerAtStart();
        };

        if (ScreenFader.Instance != null) ScreenFader.Instance.FadeThrough(reset);
        else reset();

        BeginRun();
    }

    /// <summary>Wire to the "Continue" button.</summary>
    public void ContinueToGame()
    {
        if (endPromptPanel != null) endPromptPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        ApplyPlayerSafety(false);   // the real game can kill you
        SetDamageFrozen(false);

        if (GameManager.Instance != null) GameManager.Instance.FinishTutorial();
        else Debug.LogWarning("[TutorialManager] No GameManager — cannot leave the tutorial.");
    }

    // ── Prompt panel ──────────────────────────────────────────────────────────

    private void ShowPrompt(string text, bool showContinueHint)
    {
        if (promptPanel != null) promptPanel.alpha = 1f;
        if (promptText  != null) promptText.text   = text;
        if (continueHint != null)
        {
            continueHint.gameObject.SetActive(showContinueHint);
            if (showContinueHint) continueHint.text = $"Press [{continueKey}] to continue";
        }
    }

    private void HidePrompt()
    {
        if (promptPanel  != null) promptPanel.alpha = 0f;
        if (continueHint != null) continueHint.gameObject.SetActive(false);
    }

    private EntityStats.WeaponType ChosenWeapon()
    {
        if (_playerStats != null && _playerStats.EquippedWeapon != EntityStats.WeaponType.None)
            return _playerStats.EquippedWeapon;
        return GameManager.Instance != null ? GameManager.Instance.ChosenWeapon
                                            : EntityStats.WeaponType.Blade;
    }

    // ── Default flow ──────────────────────────────────────────────────────────
    // Runs only when Phases is empty, so a fresh scene works immediately. Touch
    // anything in the inspector and your list wins.
    //
    // Expected waves:
    //   0  dummy          1 × Grunt      passive ✅ stationary ✅ invulnerable ✅
    //   1  basic attacker 1 × Grunt
    //   2  decal          1 × AirGrunt (that prefab carries the Balloon Rat stats)
    //   3  armour classes Grunt (light) + ToughRat (heavy)
    //                                    passive ✅ stationary ✅ invulnerable ✅

    private void BuildDefaultFlow()
    {
        // 1 ── MOVEMENT: one step, four cards, any order.
        var movement = new Phase { name = "Movement", skippable = true, settingsGate = SettingsGate.ShowBasics };
        movement.steps.Add(new Step { prompt = "Welcome to the sewers. Let's start with your feet." });
        movement.steps.Add(new Step
        {
            prompt = "",
            objectives =
            {
                new Objective { text = "Press the keys to move", gate = Gate.MoveDirections,
                                iconIds = { "W", "S", "A", "D" } },
                new Objective { text = "Jump",  gate = Gate.Jump, requiredCount = 1,
                                iconIds = { "SPACE" } },
                new Objective { text = "Hold to sprint", gate = Gate.Sprint, holdTime = 1.2f,
                                iconIds = { "SHIFT" } },
                new Objective { text = "Dash — you're invincible mid-dash", gate = Gate.Dash,
                                requiredCount = 1, iconIds = { "CTRL" } },
            }
        });
        movement.steps.Add(new Step { prompt = "That's everything your legs can do. Weapons next." });
        phases.Add(movement);


        // 2 ── COMBAT: sequential, so the lesson order holds. Nearly every line
        // here is weapon-filtered — the three weapons play so differently that a
        // shared script would be vague about all of them.
        var combat = new Phase { name = "Combat", skippable = true, settingsGate = SettingsGate.ShowCombat };
        combat.steps.Add(new Step
        {
            prompt = "Something's coming down. It won't fight back — it's here to be hit.",
            autoAdvanceAfter = 2.2f,
            fadeResetBefore = true, spawnWaveIndex = 0
        });

        // ── How your weapon works, before you swing it. Three read steps, each
        // filtered to one weapon, so only the relevant one ever appears.
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Blade,
            prompt = "You picked the BLADE. Fast and free — no stamina per swing, so " +
                     "you can keep the pressure on. Its strength is the CHAIN: land " +
                     "hits back to back and the third comes out as a heavier finisher " +
                     "that carries enough impact to stagger things a single jab can't." });
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Hammer,
            prompt = "You picked the HAMMER. Half a blade's swing speed, roughly three " +
                     "times its power, and enough impact to stagger most things outright. " +
                     "One good hit beats three rushed ones." });
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Bow,
            prompt = "You picked the BOW. Hold to draw, release to fire — a full draw " +
                     "triples the damage.\nFrom the hip it looses roughly where your rat " +
                     "is facing, with a little help finding a target near that line. " +
                     "AIMED, you point it yourself: the shot goes where the camera looks " +
                     "and arcs on the way in." });

        // First hit alone, so the impact/stagger explanation lands on something
        // the player just felt rather than something they were told up front.
        combat.steps.Add(new Step { objectives = {
            new Objective { text = "Hit it once", gate = Gate.BasicAttack, requiredCount = 1,
                            iconIds = { "LMB" } },
        }});

        combat.steps.Add(new Step { prompt =
            "Every weapon carries an IMPACT value, and every enemy has TOUGHNESS. " +
            "Impact under their toughness and they shrug it off; match it and they " +
            "flinch; beat it and they stagger — wide open. Heavier weapons and " +
            "chain finishers carry more impact than a quick jab." });

        // ── The weapon's own drill ──
        combat.steps.Add(new Step { objectives = {
            new Objective { text = "Chain two more hits — the third is the finisher",
                            gate = Gate.BasicAttack, requiredCount = 2,
                            iconIds = { "LMB", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Blade },
            new Objective { text = "Land two more heavy swings",
                            gate = Gate.BasicAttack, requiredCount = 2,
                            iconIds = { "LMB", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Hammer },
            new Objective { text = "Draw and land two more shots",
                            gate = Gate.BasicAttack, requiredCount = 2,
                            iconIds = { "LMB", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Bow },
        }});

        // One more drill each, so the dummy is where the weapon is really learned.
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Blade,
            prompt = "Chains only hold if you keep swinging. Pause too long and the " +
                     "count resets to the first hit — so a blade wants rhythm, not " +
                     "single pokes.",
            objectives = {
                new Objective { text = "Land three without pausing — a full chain",
                                gate = Gate.BasicAttack, requiredCount = 3,
                                iconIds = { "LMB", "LMB", "LMB" } },
            }});
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Hammer,
            prompt = "A hammer swing doesn't get interrupted the way a blade does — " +
                     "get hit mid-swing and it still lands. That's the trade for how " +
                     "slow it is: commit and you get paid.",
            objectives = {
                new Objective { text = "Land two more heavy hits",
                                gate = Gate.BasicAttack, requiredCount = 2,
                                iconIds = { "LMB", "LMB" } },
            }});
        combat.steps.Add(new Step {
            weaponOnly = EntityStats.WeaponType.Bow,
            prompt = "Hold the draw all the way for a full charge — three times the " +
                     "damage of a snap shot. Release early and you get the snap shot.",
            objectives = {
                new Objective { text = "Land two fully drawn shots",
                                gate = Gate.BasicAttack, requiredCount = 2,
                                iconIds = { "LMB", "LMB" } },
            }});

        combat.steps.Add(new Step { objectives = {
            new Objective { text = "Hold to aim, then swing — your facing locks so hits " +
                                   "land where you're looking",
                            gate = Gate.Aim, holdTime = 1.5f, iconIds = { "RMB" },
                            weaponOnly = EntityStats.WeaponType.Blade },
            new Objective { text = "Hold to aim, then swing — your feet plant so the " +
                                   "swing lands where you point it",
                            gate = Gate.Aim, holdTime = 1.5f, iconIds = { "RMB" },
                            weaponOnly = EntityStats.WeaponType.Hammer },
            new Objective { text = "Hold to aim — now you're pointing the bow yourself",
                            gate = Gate.Aim, holdTime = 1.5f, iconIds = { "RMB" },
                            weaponOnly = EntityStats.WeaponType.Bow },
        }});

        combat.steps.Add(new Step { objectives = {
            new Objective { text = "Jump, then attack in the air — a spin that hits all " +
                                   "around you",
                            gate = Gate.JumpAttack, requiredCount = 1,
                            iconIds = { "SPACE", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Blade },
            new Objective { text = "Jump, then attack in the air — a ground slam in a " +
                                   "circle around the landing",
                            gate = Gate.JumpAttack, requiredCount = 1,
                            iconIds = { "SPACE", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Hammer },
            new Objective { text = "Jump, then attack in the air — a three-arrow burst " +
                                   "angled down",
                            gate = Gate.JumpAttack, requiredCount = 1,
                            iconIds = { "SPACE", "LMB" },
                            weaponOnly = EntityStats.WeaponType.Bow },
        }});
        combat.steps.Add(new Step { prompt =
            "Attacks from the air hit harder on the way down — more knockback than " +
            "the same weapon swung on the ground. Use one to shove something off you " +
            "when you're crowded." });

        combat.steps.Add(new Step { prompt = "Finish it.", autoAdvanceAfter = 0.8f, killWaveIndex = 0 });
        combat.steps.Add(new Step { prompt =
            "Every kill pays out XP and RAT COIN. XP levels you up; coin buys what " +
            "you find between rooms." });
        combat.steps.Add(new Step { objectives = {
            new Objective { text = "Open your stats and spend a point", gate = Gate.StatMenu,
                            iconIds = { "TAB" } },
        }});
        phases.Add(combat);

        // 3 ── ENEMIES: the intro roster, then the armour-class lesson.
        // Every explanation waits for the continue key rather than a timer — the
        // player sets the pace, and the next rat can't land on top of the reading.
        var enemies = new Phase { name = "Enemies", skippable = false, settingsGate = SettingsGate.ShowCombat };

        enemies.steps.Add(new Step
        {
            prompt = "Live one this time — it fights back. It swings at you: watch " +
                     "the wind-up and dash through it.",
            promptBlade  = "A grunt has no armour, so even a single blade hit staggers " +
                           "it. Keep the chain going and it never gets a swing off.",
            promptHammer = "A grunt has no armour and you have the heaviest weapon in " +
                           "the game. One hit ends its turn — you can afford to wait " +
                           "for the wind-up and punish it.",
            promptBow    = "You don't have to be near it. Back off, draw, and it dies " +
                           "walking toward you — but keep an eye on the gap, because " +
                           "a drawn bow slows you down.",
            fadeResetBefore = true, spawnWaveIndex = 1
        });
        enemies.steps.Add(new Step { objectives = {
            new Objective { text = "Defeat the grunt", gate = Gate.WaveCleared, waveIndex = 1 },
        }});

        enemies.steps.Add(new Step { prompt =
            "Some attacks paint a SHAPE on the floor before they land. That shape is " +
            "exactly where the hit goes — stand in it when it fills and it connects.\n" +
            "They come in every form: circles, cones, long rectangles a rat charges " +
            "down, and drops from above. Learn the shape, leave the shape." });

        enemies.steps.Add(new Step
        {
            prompt = "Here comes one now.",
            promptBlade  = "Balloons are soft — no armour and barely any health. " +
                           "Anything you swing pops one.",
            promptHammer = "Balloons are soft and light, and your hammer sends things " +
                           "flying. Expect this one to leave the ground.",
            promptBow    = "A balloon floats — an easy target at range, and you can " +
                           "pop it before it ever gets above you.",
            autoAdvanceAfter = 1.4f, spawnWaveIndex = 2
        });
        enemies.steps.Add(new Step { objectives = {
            new Objective { text = "Defeat the balloon rat — it drops on you from above, " +
                                   "marking a circle where it lands",
                            gate = Gate.WaveCleared, waveIndex = 2 },
        }});

        // ── Armour classes ──
        enemies.steps.Add(new Step { prompt =
            "Two more coming, and these won't fight back. Look above their heads: " +
            "each CHEVRON is a point of armour. No chevrons means no armour at all; " +
            "a stack of them means your hits are going to bounce." });
        enemies.steps.Add(new Step
        {
            prompt = "One lightly armoured, one heavily. Same weapon, same swing — " +
                     "and they will not react the same way.",
            promptBlade  = "A single blade hit won't move the armoured one. Your chain " +
                           "finisher will — that's what the third hit is for.",
            promptHammer = "Your hammer has enough impact to move both of them. That's " +
                           "the hammer's whole argument.",
            promptBow    = "An arrow carries the least impact of any weapon, so armour " +
                           "shrugs it off. A full draw or a hit from the air is your " +
                           "answer.",
            autoAdvanceAfter = 2f, spawnWaveIndex = 3
        });
        enemies.steps.Add(new Step { objectives = {
            new Objective { text = "Hit each one once and watch how differently they take it",
                            gate = Gate.HitEachTarget, waveIndex = 3 },
        }});
        enemies.steps.Add(new Step { prompt =
            "The unarmoured one buckled. The armoured one barely noticed — its " +
            "toughness outranks your impact, so the hit landed but nothing else did. " +
            "Bring a heavier weapon, finish a combo, or come down on it from the air." });
        enemies.steps.Add(new Step { objectives = {
            new Objective { text = "Now jump-attack each one — more impact, more knockback",
                            gate = Gate.JumpHitEachTarget, waveIndex = 3,
                            iconIds = { "SPACE", "LMB" } },
        }});
        enemies.steps.Add(new Step { releaseWaveIndex = 3, objectives = {
            new Objective { text = "Finish them", gate = Gate.WaveCleared, waveIndex = 3 },
        }});
        phases.Add(enemies);
    }
}
