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

        [System.NonSerialized] public readonly List<EntityStats> live = new List<EntityStats>();
        [System.NonSerialized] public bool spawned;
        [System.NonSerialized] public int  initialCount;   // how many actually spawned
        /// <summary>Total max HP the wave had on arrival. Held as the denominator
        /// for the health bar, because the dead leave 'live' and the survivors'
        /// sum alone would make the bar jump BACKWARDS on every kill.</summary>
        [System.NonSerialized] public int  openingMaxHealth;
    }

    /// <summary>What a step does to the screen while its text is up.</summary>
    public enum FocusMode
    {
        /// <summary>Nothing — the game carries on underneath the words.</summary>
        None,
        /// <summary>Freeze, darken, and leave a lit window on the focus wave.</summary>
        Spotlight,
        /// <summary>Freeze and darken the lot — a beat with nothing to point at.</summary>
        FullScreen
    }

    /// <summary>What the player may still do while a step's text is up.</summary>
    public enum StepInputLock
    {
        /// <summary>Whatever the phase says.</summary>
        Inherit,
        /// <summary>Full control.</summary>
        None,
        /// <summary>Walk and look, but no swinging at the lesson.</summary>
        NoAttacking,
        /// <summary>Hands off.</summary>
        Everything
    }

    [System.Serializable]
    public class Objective
    {
        [Tooltip("The line on the card, e.g. \"Press the keys to move\".")]
        public string text = "";
        public Gate gate = Gate.Jump;

        [Tooltip("Icon library ids, left to right as the player reads them. For the " +
                 "move drill use \"W\",\"A\",\"S\",\"D\" — the manager maps each slot " +
                 "to the right direction itself.")]
        public List<string> iconIds = new List<string>();

        [Header("Gate tuning")]
        [Tooltip("Counting gates: Jump, Dash, BasicAttack, JumpAttack.")]
        public int requiredCount = 1;
        [Tooltip("Timed gates: MoveHold, Sprint, Aim.")]
        public float holdTime = 0.6f;

        [Tooltip("Show the bar across the bottom of the card. On a timed gate it " +
                 "fills while you hold, so you can see how much longer to keep " +
                 "holding instead of guessing. Counting and wave gates can use it " +
                 "too; read steps never do.")]
        public bool showProgressBar = true;
        [Tooltip("Wave Cleared only: fill the bar from the enemies' REMAINING " +
                 "HEALTH instead of the body count. On a single-enemy fight a kill " +
                 "counter only ever reads 0 or 1, so the bar would sit empty for " +
                 "the whole fight and then vanish. Health turns it into a damage " +
                 "meter, which is what you want while teaching combat.")]
        public bool progressFromHealth = true;
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

        [Header("Freeze and show")]
        [Tooltip("Spotlight stops time, darkens the screen and leaves a lit window " +
                 "around the focus wave's first living enemy — so the word STAGGER " +
                 "and the label floating over the rat that just took the hit are on " +
                 "screen together, with nothing else moving. Full Screen freezes " +
                 "and darkens everything. Either one always waits for the key.")]
        public FocusMode focus = FocusMode.None;
        [Tooltip("Which wave to light up. -1 = the most recently spawned one.")]
        public int   focusWaveIndex = -1;
        [Tooltip("Realtime beat before the freeze bites, so the reaction label has " +
                 "finished popping in rather than being frozen mid-pop.")]
        public float focusDelay = 0.35f;

        [Header("Flow")]
        [Tooltip("Hold the LAST page until the continue key. Automatic on a " +
                 "freeze-and-show. Leave it off everywhere else: the panel pages " +
                 "and closes itself, so the key only ever means 'hurry up' and the " +
                 "player is never made to press it.")]
        public bool holdForKey = false;
        [Tooltip("What the player can do while this step is up. Inherit reads the " +
                 "phase, and treats any step that is pure text as no-attacking.")]
        public StepInputLock inputLock = StepInputLock.Inherit;

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
        [Tooltip("Attacking hasn't been taught yet in this phase — hold the attack " +
                 "and aim buttons for the whole phase, so a player mashing buttons " +
                 "during the movement drills can't start a fight the tutorial " +
                 "hasn't introduced yet.")]
        public bool lockAttacks = false;
        public List<Step> steps = new List<Step>();
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Prompt panel (centre — story beats only)")]
    public CanvasGroup promptPanel;
    public TMP_Text    promptText;
    public TMP_Text    sectionLabel;
    public TMP_Text    continueHint;

    [Tooltip("The paged prompt panel. Assign it and it takes the prompt over " +
             "completely — plate, paging, read timing and the continue hint. The " +
             "four fields above are only a fallback for when it's empty.")]
    public TutorialPromptUI promptUI;

    [Tooltip("Freeze-and-show overlay. Found in the scene if empty, and built " +
             "from nothing if there isn't one.")]
    public TutorialFocus focusOverlay;

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

    [Tooltip("Master switch for the green bar on objective cards. Off hides it " +
             "everywhere regardless of each objective's own Show Progress Bar.")]
    public bool showProgressBars = true;

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
    private Wave _lastSpawnedWave;

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

        if (promptUI == null) promptUI = FindFirstObjectByType<TutorialPromptUI>();
        if (promptUI == null && promptPanel != null)
        {
            // Upgrade the old one-page panel where it stands: same GameObject,
            // same text objects, now with a plate behind it, paging and read
            // timing. An existing scene needs no rewiring at all.
            promptUI              = promptPanel.gameObject.AddComponent<TutorialPromptUI>();
            promptUI.group        = promptPanel;
            promptUI.panel        = promptPanel.GetComponent<RectTransform>();
            promptUI.body         = promptText;
            promptUI.continueHint = continueHint;
        }
        if (promptUI != null) promptUI.continueKey = continueKey;

        if (focusOverlay == null) focusOverlay = FindFirstObjectByType<TutorialFocus>();
        if (focusOverlay == null)
        {
            // TutorialFocus builds its own canvas and quads, so a scene saved
            // before the freeze-and-show beats existed still gets them without a
            // trip through the setup tool.
            var focusGO = new GameObject("TutorialFocus");
            focusGO.transform.SetParent(transform, false);
            focusOverlay = focusGO.AddComponent<TutorialFocus>();
        }

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
        PlayerInputLock.ClearAll(); // nor a lock, or the next scene has dead controls
        CloseFocus();
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

        // A freeze-and-show always waits for the key: the whole point is that the
        // player looks at the thing before the game starts moving again.
        bool wantsFocus = step.focus != FocusMode.None && readStep;
        bool holdForKey = step.holdForKey || wantsFocus;

        ApplyInputLock(step, readStep);

        if (!string.IsNullOrWhiteSpace(body)) ShowPrompt(body, holdForKey);
        else HidePrompt();

        _awaitingRead = readStep;

        // A read step is a beat where the game politely stops hitting you.
        SetDamageFrozen(readStep && freezeDamageDuringPrompts);

        // Cards last so they slide in after the prompt has settled.
        foreach (Objective o in _liveObjectives) SpawnCard(o);

        step.onBegin?.Invoke();
        _busy = false;

        if (step.focus != FocusMode.None && !readStep)
            Debug.LogWarning($"[TutorialManager] Step \"{Trim(step.prompt)}\" asks for " +
                             "a freeze-and-show but also has objective cards. Freezing " +
                             "would stop the player completing them, so the focus is " +
                             "being skipped — split it into a text step and a task step.");

        if (wantsFocus) StartCoroutine(OpenFocusAfterDelay(step));
    }

    private static string Trim(string s)
        => string.IsNullOrEmpty(s) ? "(no prompt)"
         : (s.Length <= 40 ? s : s.Substring(0, 40) + "…");

    /// <summary>
    /// Decides what the player may do during a step. The phase sets the floor —
    /// during the movement drills, attacking isn't a thing yet — and a step can
    /// tighten it further. This is what stops someone mashing the attack button
    /// through an explanation and killing the dummy the next three steps are about.
    /// </summary>
    private void ApplyInputLock(Step step, bool readStep)
    {
        StepInputLock want = step != null ? step.inputLock : StepInputLock.Inherit;

        if (want == StepInputLock.Inherit)
        {
            bool phaseLocks = _currentPhase != null && _currentPhase.lockAttacks;
            bool reading    = readStep && step != null
                           && !string.IsNullOrWhiteSpace(step.PromptFor(ChosenWeapon()));

            // Reading is a hands-off moment too, just a gentler one: you can still
            // walk about while the words are up, you just can't swing.
            want = (phaseLocks || reading) ? StepInputLock.NoAttacking
                                           : StepInputLock.None;
        }

        switch (want)
        {
            case StepInputLock.Everything:
                PlayerInputLock.SetAll(true);
                break;
            case StepInputLock.NoAttacking:
                PlayerInputLock.ClearAll();
                PlayerInputLock.LockCombat(true);
                break;
            default:
                RestorePhaseLock();
                break;
        }
    }

    /// <summary>
    /// Back to whatever the current phase allows. Used between steps, so the
    /// 0.35s gap doesn't hand back a button the phase is meant to be holding.
    /// </summary>
    private void RestorePhaseLock()
    {
        PlayerInputLock.ClearAll();
        if (_currentPhase != null && _currentPhase.lockAttacks)
            PlayerInputLock.LockCombat(true);
    }

    private IEnumerator OpenFocusAfterDelay(Step step)
    {
        // Realtime: the reaction label's pop runs on scaled time and we want it
        // finished, not frozen halfway through.
        if (step.focusDelay > 0f) yield return new WaitForSecondsRealtime(step.focusDelay);

        if (_currentStep != step || focusOverlay == null) yield break;   // moved on

        // Hands completely off while time is stopped. Input still fires at
        // timeScale 0, so without this the player can stand there swinging at a
        // frozen rat while the explanation is on screen.
        PlayerInputLock.SetAll(true);

        if (step.focus == FocusMode.FullScreen) { focusOverlay.OpenFullScreen(); yield break; }

        Transform target = FocusTarget(step);
        if (target != null) focusOverlay.Open(target);
        else                focusOverlay.OpenFullScreen();
    }

    /// <summary>The first living enemy of the wave a step points at.</summary>
    private Transform FocusTarget(Step step)
    {
        Wave w = step.focusWaveIndex >= 0 ? WaveAt(step.focusWaveIndex) : _lastSpawnedWave;
        if (w == null) return null;
        PruneWave(w);
        foreach (EntityStats st in w.live)
            if (st != null && !st.IsDead) return st.transform;
        return null;
    }

    private void CloseFocus()
    {
        if (focusOverlay != null && focusOverlay.IsOpen) focusOverlay.Close();
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
        CloseFocus();
        RestorePhaseLock();
        HidePrompt();
        done?.onComplete?.Invoke();
        StartCoroutine(GapThenAdvance());
    }

    private IEnumerator GapThenAdvance()
    {
        _busy = true;
        // Realtime: a step can end while something still holds a freeze (the
        // stat-menu lesson ends the moment the menu opens, and the menu freezes
        // the game), and a scaled wait there would sit at zero indefinitely.
        if (stepGapSeconds > 0f) yield return new WaitForSecondsRealtime(stepGapSeconds);
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
        CloseFocus();
        PlayerInputLock.ClearAll();
        promptUI?.FinishNow();
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
            _skipTimer += Time.unscaledDeltaTime;   // works while frozen too
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

        // Unscaled: a freeze-and-show sets Time.timeScale to 0, and a stalled
        // clock here would leave the step waiting forever.
        _stepClock += Time.unscaledDeltaTime;

        // Read step — prompt panel only.
        if (_awaitingRead)
        {
            // A step with no prompt and no cards is pure scene action — don't
            // sit there waiting for a key on a blank screen.
            if (string.IsNullOrWhiteSpace(_currentStep.PromptFor(ChosenWeapon())))
            { CompleteStep(); return; }

            // The panel owns the pacing: it pages itself, auto-advances on a read
            // time taken from each page's word count, and only waits for the key
            // on a beat that asked to be acknowledged. Auto Advance After still
            // applies on top, as a hard ceiling on the whole step.
            if (promptUI != null)
            {
                bool capped = _currentStep.autoAdvanceAfter > 0f
                           && _stepClock >= _currentStep.autoAdvanceAfter;
                if (promptUI.IsFinished || capped) CompleteStep();
                return;
            }

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

    /// <summary>
    /// 0–1 completion of an objective, for the card's bottom bar. Timed gates are
    /// the point of this — a sprint or aim hold otherwise gives the player no idea
    /// how much longer to keep the key down.
    /// </summary>
    private float GateProgress01(Objective o)
    {
        switch (o.gate)
        {
            case Gate.MoveHold:
            case Gate.Sprint:
            case Gate.Aim:
                return Mathf.Clamp01(o.holdTimer / Mathf.Max(0.0001f, o.holdTime));

            case Gate.MoveDirections:
            {
                int n = 0;
                if (o.dirHit != null)
                    for (int i = 0; i < o.dirHit.Length; i++) if (o.dirHit[i]) n++;
                return n / 4f;
            }

            case Gate.WaveCleared:
            {
                // Health, not the body count: "kill the rat" is a gate with
                // exactly two states, so a kill counter leaves the bar empty for
                // the entire fight and then throws it away. Draining it as the
                // enemy loses HP turns the card into a damage meter, which is
                // the actual feedback while combat is being taught.
                if (o.progressFromHealth)
                {
                    float standing = WaveHealth01(o.waveIndex);
                    if (standing >= 0f) return 1f - standing;
                }

                int total = RequiredTotal(o);
                return total <= 0 ? 0f : Mathf.Clamp01(o.count / (float)total);
            }

            default:
            {
                int total = RequiredTotal(o);
                if (total <= 0) return 0f;
                return Mathf.Clamp01(o.count / (float)total);
            }
        }
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

    /// <summary>Icon slot (W,A,S,D) → dirHit index (fwd,back,left,right).</summary>
    private static readonly int[] WasdIconToDir = { 0, 2, 1, 3 };

    /// <summary>
    /// Fraction of a wave's health still standing, or -1 when there's nothing
    /// sensible to measure (wave missing, not spawned yet, no health at all).
    /// </summary>
    private float WaveHealth01(int index)
    {
        Wave w = index >= 0 ? WaveAt(index) : _lastSpawnedWave;
        if (w == null || !w.spawned) return -1f;

        int current = 0, max = 0;
        foreach (EntityStats st in w.live)
        {
            if (st == null) continue;
            max     += Mathf.Max(1, st.MaxHealth);
            current += st.IsDead ? 0 : Mathf.Max(0, st.CurrentHealth);
        }

        // The dead are pruned out of 'live', so summing the survivors would
        // shrink the denominator on every kill and walk the bar BACKWARDS. The
        // wave's opening total is the only stable thing to divide by.
        if (w.openingMaxHealth > 0) max = w.openingMaxHealth;
        if (max <= 0) return -1f;

        return Mathf.Clamp01(current / (float)max);
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

        objectiveList.SetProgress(o.cardId,
            (showProgressBars && o.showProgressBar) ? GateProgress01(o) : -1f);

        if (o.gate == Gate.MoveDirections)
        {
            // Icons read W A S D left-to-right, but dirHit is stored in movement
            // order (forward, back, left, right). Map between them rather than
            // ordering the icons to match the array, which is what made the card
            // read "W S A D".
            for (int i = 0; i < 4; i++)
                objectiveList.MarkIcon(o.cardId, i, o.dirHit[WasdIconToDir[i]]);
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
        w.spawned          = false;   // wave gates must not fire mid-arrival
        w.initialCount     = 0;
        w.openingMaxHealth = 0;
        _lastSpawnedWave   = w;
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

        // Banked before anything can die, so the health bar has a fixed
        // denominator for the rest of the fight.
        w.openingMaxHealth = 0;
        foreach (EntityStats st in w.live)
            if (st != null) w.openingMaxHealth += Mathf.Max(1, st.MaxHealth);

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

    /// <summary>
    /// Every wave back to "never spawned", and everything it dropped in
    /// destroyed. The old restart cleared the live list WITHOUT destroying what
    /// was in it, which is how a second run began with the first run's rats
    /// still standing in the room.
    /// </summary>
    private void ResetAllWaves()
    {
        foreach (Wave w in waves)
        {
            if (w == null) continue;
            foreach (EntityStats st in w.live)
                if (st != null) Destroy(st.gameObject);
            w.live.Clear();
            w.spawned          = false;
            w.initialCount     = 0;
            w.openingMaxHealth = 0;
        }

        _phaseWaves.Clear();
        _lastSpawnedWave = null;
        _hitBasic.Clear();
        _hitJump.Clear();
    }

    /// <summary>
    /// Wipes the per-objective counters. Objectives live on the Phase assets, so
    /// their runtime fields survive a restart — without this, a gate the player
    /// finished on the first run can read as already done the instant its card
    /// appears on the second.
    /// </summary>
    private void ResetAllObjectiveState()
    {
        _jumpCount = _dashCount = _basicCount = _airCount = 0;
        _objectiveSeen = "";
        _stepClock     = 0f;

        foreach (Phase ph in phases)
        {
            if (ph == null || ph.steps == null) continue;
            foreach (Step st in ph.steps)
            {
                if (st == null || st.objectives == null) continue;
                foreach (Objective o in st.objectives)
                {
                    if (o == null) continue;
                    o.done              = false;
                    o.count             = 0;
                    o.holdTimer         = 0f;
                    o.lastShownProgress = -1;
                }
            }
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

        CloseFocus();
        PlayerInputLock.ClearAll();
        promptUI?.FinishNow();
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

        // A restart has to undo everything the run switched on, not just move
        // the player back. Anything left set here carries straight into the
        // second run and looks like a brand new bug.
        CloseFocus();
        PlayerInputLock.ClearAll();
        SetDamageFrozen(false);
        promptUI?.FinishNow();
        HidePrompt();
        objectiveList?.Clear();
        _spawnRoutine = null;   // StopAllCoroutines already killed it

        System.Action reset = () =>
        {
            EndPhaseCleanup();
            ResetAllWaves();
            ResetAllObjectiveState();
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

        // Everything the tutorial switched on is a static or a global, so any
        // one of them left set would follow the player into the first real
        // level. An unkillable rat king is a far worse bug than a rough tutorial,
        // so the room is emptied and every global is put back by hand here.
        CloseFocus();
        promptUI?.FinishNow();
        HidePrompt();
        objectiveList?.Clear();
        EndPhaseCleanup();
        ResetAllWaves();
        PlayerInputLock.ClearAll();
        GameFreeze.ReleaseAll();

        ApplyPlayerSafety(false);   // the real game can kill you
        SetDamageFrozen(false);

        if (GameManager.Instance != null) GameManager.Instance.FinishTutorial();
        else Debug.LogWarning("[TutorialManager] No GameManager — cannot leave the tutorial.");
    }

    // ── Prompt panel ──────────────────────────────────────────────────────────

    /// <summary>
    /// Put an explanation up. With a TutorialPromptUI assigned this hands the
    /// text over whole and the panel does the paging and the read timing;
    /// otherwise it falls back to the old one-page-forever behaviour.
    /// </summary>
    /// <param name="holdForKey">
    /// True makes the LAST page wait for the continue key. Everything else pages
    /// itself, so pressing the key is an optional "hurry up" rather than the way
    /// the tutorial moves.
    /// </param>
    private void ShowPrompt(string text, bool holdForKey)
    {
        if (promptUI != null)
        {
            promptUI.continueKey = continueKey;
            promptUI.Show(text, holdForKey);
            return;
        }

        if (promptPanel != null) promptPanel.alpha = 1f;
        if (promptText  != null) promptText.text   = text;
        if (continueHint != null)
        {
            continueHint.gameObject.SetActive(holdForKey);
            if (holdForKey) continueHint.text = $"Press [{continueKey}] to continue";
        }
    }

    private void HidePrompt()
    {
        if (promptUI != null) promptUI.Hide();
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
        var movement = new Phase { name = "Movement", skippable = true,
                                   settingsGate = SettingsGate.ShowBasics,
                                   // Nothing to swing at yet, and a player
                                   // mashing buttons through the movement drills
                                   // shouldn't be starting fights.
                                   lockAttacks = true };
        movement.steps.Add(new Step { prompt = "Welcome to the sewers. Let's start with your feet." });
        movement.steps.Add(new Step
        {
            prompt = "",
            objectives =
            {
                new Objective { text = "Press the keys to move", gate = Gate.MoveDirections,
                                iconIds = { "W", "A", "S", "D" } },
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

        // Freeze on the hit the player just landed, with the light on the dummy
        // and the reaction label still floating above it. The words IMPACT and
        // STAGGER land while the thing they describe is on screen, which is the
        // one moment they mean anything.
        combat.steps.Add(new Step
        {
            prompt =
                "Look at the word above its head.\n\n" +
                "Every weapon carries an IMPACT value, and every enemy has TOUGHNESS. " +
                "Impact under their toughness and they SHRUG it off. Match it and they " +
                "FLINCH. Beat it and they STAGGER — wide open, and yours.\n\n" +
                "Heavier weapons and chain finishers carry more impact than a quick jab.",
            focus          = FocusMode.Spotlight,
            focusWaveIndex = 0,
            holdForKey     = true
        });

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
            "Two more coming, and these won't fight back.\n\n" +
            "Armour doesn't show on a rat — you read it off how it TAKES a hit. " +
            "A rat that staggers had nothing to spare. One that only flinches is " +
            "holding armour. One that shrugs a hit off entirely outranks your " +
            "weapon, and you'll need a heavier one, a finisher, or a jump attack." });
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
        enemies.steps.Add(new Step
        {
            prompt =
                "The unarmoured one buckled. The armoured one barely noticed.\n\n" +
                "Its toughness outranks your impact, so the hit landed and nothing " +
                "else did. Bring a heavier weapon, finish a combo, or come down on " +
                "it from the air.",
            focus          = FocusMode.Spotlight,
            focusWaveIndex = 3,
            holdForKey     = true
        });
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
