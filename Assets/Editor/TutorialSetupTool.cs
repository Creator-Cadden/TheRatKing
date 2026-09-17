#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-click setup for the tutorial scene. Menu: Tools ▸ Rat King ▸ Setup Tutorial Scene.
///
/// It builds every object the tutorial needs, creates the objective-card prefab
/// if it's missing, and drags every reference into place. It is SAFE TO RE-RUN:
/// it looks things up by name first and only creates what isn't already there, so
/// running it over a hierarchy you built by hand just fills in the blanks and
/// wires it. Nothing is deleted, and your own edits to sizes, colours and
/// positions survive a re-run.
///
/// What it does NOT do: place spawn points sensibly (they land on the manager's
/// position — drag them where you want), fill the icon library (you need sprites
/// for that), or fill the waves (it can't guess your enemy prefabs).
/// </summary>
public static class TutorialSetupTool
{
    private const string CardPrefabPath = "Assets/Prefabs/UI/ObjectiveCard.prefab";

    private static readonly string[] SpawnPointNames =
    {
        "Spawn_Dummy", "Spawn_Grunt", "Spawn_Balloon", "Spawn_Low", "Spawn_High"
    };

    private const string IconFolder = "Assets/Art/UI/KeyIcons";

    /// <summary>
    /// Imports every PNG in the key-icon folder as a Sprite and adds one Icon
    /// Library row per file, named after the file ("SPACE.png" → id "SPACE").
    /// Existing rows are left alone, so your own art and overrides survive.
    /// </summary>
    [MenuItem("Tools/Rat King/Fill Objective Icon Library", false, 2)]
    public static void FillIconLibrary()
    {
        var list = Object.FindFirstObjectByType<ObjectiveListUI>(FindObjectsInactive.Include);
        if (list == null)
        {
            Debug.LogWarning("[TutorialSetup] No ObjectiveListUI in the open scene. " +
                             "Run Setup Tutorial Scene first.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[TutorialSetup] No textures in {IconFolder}.");
            return;
        }

        // PASS 1 — fix the import settings.
        // This is a 3D project, so PNGs import as plain textures. Two settings
        // matter, not one: textureType alone leaves spriteImportMode at None, no
        // Sprite sub-asset is generated, and LoadAssetAtPath<Sprite> quietly
        // returns null for every file. That's what an empty icon library looks
        // like from the outside.
        int converted = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool needsFix = importer.textureType      != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single;
            if (!needsFix) continue;

            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.filterMode          = FilterMode.Point;   // matches the pixel look
            importer.SaveAndReimport();
            converted++;
        }
        if (converted > 0) AssetDatabase.Refresh();

        // PASS 2 — read the sprites back, now that they exist.
        Undo.RecordObject(list, "Fill icon library");
        int added = 0, missed = 0;

        foreach (string guid in guids)
        {
            string path   = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            // Fallback: on some import modes the Sprite is a sub-asset rather
            // than the main one, so look through everything at that path.
            if (sprite == null)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite s) { sprite = s; break; }
            }

            if (sprite == null)
            {
                missed++;
                Debug.LogWarning($"[TutorialSetup] No sprite could be read from {path}.");
                continue;
            }

            string id = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            bool exists = false;
            foreach (var k in list.iconLibrary)
                if (k != null && string.Equals(k.id, id, System.StringComparison.OrdinalIgnoreCase))
                { exists = true; break; }
            if (exists) continue;

            list.iconLibrary.Add(new ObjectiveListUI.KeyIcon { id = id, sprite = sprite });
            added++;
        }

        EditorUtility.SetDirty(list);
        EditorSceneManager.MarkSceneDirty(list.gameObject.scene);
        Debug.Log($"[TutorialSetup] Icon library: {added} row(s) added, " +
                  $"{converted} texture(s) reimported as Sprite, {missed} unreadable. " +
                  $"Library now holds {list.iconLibrary.Count} row(s). Save the scene.");
    }

    /// <summary>
    /// Forces the centre prompt panel back to a layout that fits. Separate from
    /// Setup because Setup deliberately only positions things the first time it
    /// creates them — it won't stomp your tweaks, which also means it can't fix
    /// the original too-short panel where long text ran up into the section
    /// title. This one overwrites those numbers on purpose.
    /// </summary>
    [MenuItem("Tools/Rat King/Re-layout Prompt Panel", false, 3)]
    public static void RelayoutPromptPanel()
    {
        var m = Object.FindFirstObjectByType<TutorialManager>(FindObjectsInactive.Include);
        if (m == null || m.promptPanel == null)
        {
            Debug.LogWarning("[TutorialSetup] No TutorialManager / prompt panel in the open scene.");
            return;
        }

        var panel = m.promptPanel.GetComponent<RectTransform>();
        Undo.RecordObject(panel, "Re-layout prompt panel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta        = new Vector2(1040f, 300f);
        panel.anchoredPosition = new Vector2(0f, 70f);

        // Section title sits ABOVE the panel, not inside it — that's the actual
        // fix. Text that grows downward from the top can then never reach it.
        Place(m.sectionLabel, new Vector2(0.5f, 1f), new Vector2(0f, 42f),
              new Vector2(1000f, 40f), 28f, TextAlignmentOptions.Center);

        // Prompt text: pinned to the panel's top, tall enough for four lines,
        // and top-aligned so one-liners sit where long text starts.
        Place(m.promptText, new Vector2(0.5f, 1f), new Vector2(0f, -18f),
              new Vector2(1000f, 190f), 30f, TextAlignmentOptions.Top);

        Place(m.continueHint, new Vector2(0.5f, 0f), new Vector2(0f, 64f),
              new Vector2(1000f, 34f), 22f, TextAlignmentOptions.Center);
        Place(m.skipHint, new Vector2(0.5f, 0f), new Vector2(0f, 34f),
              new Vector2(1000f, 30f), 20f, TextAlignmentOptions.Center);

        if (m.skipFill != null)
        {
            var r = m.skipFill.rectTransform;
            Undo.RecordObject(r, "Re-layout prompt panel");
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta        = new Vector2(240f, 5f);
            r.anchoredPosition = new Vector2(0f, 16f);
        }

        int cards = RelayoutCardPrefab();

        // The card got taller, so the list has to step further per row or cards
        // overlap the one below.
        var list = Object.FindFirstObjectByType<ObjectiveListUI>(FindObjectsInactive.Include);
        if (list != null && list.cardPrefab != null)
        {
            var cr2 = list.cardPrefab.GetComponent<RectTransform>();
            if (cr2 != null && cr2.rect.height > 1f)
            {
                Undo.RecordObject(list, "Row height");
                list.autoRowHeight = true;
                list.rowHeight     = cr2.rect.height;
                list.spacing       = Mathf.Max(list.spacing, 10f);
                EditorUtility.SetDirty(list);
            }
        }

        EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
        Debug.Log("[TutorialSetup] Prompt panel re-laid out (section title now sits " +
                  "above the panel, prompt text grows downward from the top)" +
                  (cards > 0 ? " and the objective card widened." : "."));
    }

    /// <summary>
    /// Widens the existing card prefab and pushes the text and icon rows apart.
    /// Returns 1 if it changed something. Like the prompt panel, the creation
    /// path only sets these once, so an already-built card needs this.
    /// </summary>
    private static int RelayoutCardPrefab()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (asset == null) return 0;

        GameObject root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
        try
        {
            var card = root.GetComponent<ObjectiveCardUI>();
            var cr   = root.GetComponent<RectTransform>();
            if (cr != null) cr.sizeDelta = new Vector2(440f, 88f);

            if (card != null)
            {
                card.iconHeight   = 30f;
                card.maxIconWidth = 140f;

                // Text on the top row, icons on the bottom, with real air between
                // them — the old 64px card had them almost touching.
                if (card.label != null)
                {
                    RectTransform lr = card.label.rectTransform;
                    lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(1f, 1f);
                    lr.sizeDelta        = new Vector2(400f, 40f);
                    lr.anchoredPosition = new Vector2(-16f, -10f);
                    card.label.alignment = TextAlignmentOptions.TopRight;
                    card.label.fontSize  = 20f;
                }
                if (card.counter != null)
                {
                    RectTransform cnr = card.counter.rectTransform;
                    cnr.anchorMin = cnr.anchorMax = cnr.pivot = new Vector2(0f, 0f);
                    cnr.sizeDelta        = new Vector2(80f, 26f);
                    cnr.anchoredPosition = new Vector2(16f, 14f);
                }
            }

            // Cards built before the hold bar existed get one here, and cards
            // built with the single gold bar get upgraded to track + green fill.
            if (card != null)
            {
                Transform oldBar = root.transform.Find("ProgressBar");
                if (oldBar != null) Object.DestroyImmediate(oldBar.gameObject);

                Transform t = root.transform.Find("ProgressTrack");
                GameObject trackGO = t != null ? t.gameObject
                    : new GameObject("ProgressTrack", typeof(RectTransform));
                if (t == null) trackGO.transform.SetParent(root.transform, false);

                RectTransform trackR = trackGO.GetComponent<RectTransform>();
                trackR.anchorMin = new Vector2(0f, 0f);
                trackR.anchorMax = new Vector2(1f, 0f);
                trackR.pivot     = new Vector2(0.5f, 0f);
                trackR.offsetMin = new Vector2(10f, 6f);
                trackR.offsetMax = new Vector2(-10f, 11f);

                Image trackImg = trackGO.GetComponent<Image>();
                if (trackImg == null) trackImg = trackGO.AddComponent<Image>();
                trackImg.color         = new Color(1f, 1f, 1f, 0.10f);
                trackImg.raycastTarget = false;
                trackImg.type          = Image.Type.Simple;

                Transform f = trackGO.transform.Find("Fill");
                GameObject fillGO = f != null ? f.gameObject
                    : new GameObject("Fill", typeof(RectTransform));
                if (f == null) fillGO.transform.SetParent(trackGO.transform, false);

                RectTransform fillR = fillGO.GetComponent<RectTransform>();
                fillR.anchorMin = Vector2.zero;
                fillR.anchorMax = Vector2.one;
                fillR.offsetMin = Vector2.zero;
                fillR.offsetMax = Vector2.zero;

                Image fillImg = fillGO.GetComponent<Image>();
                if (fillImg == null) fillImg = fillGO.AddComponent<Image>();
                fillImg.color         = new Color(0.30f, 0.85f, 0.35f, 1f);
                fillImg.raycastTarget = false;
                fillImg.type          = Image.Type.Filled;
                fillImg.fillMethod    = Image.FillMethod.Horizontal;
                fillImg.fillOrigin    = (int)Image.OriginHorizontal.Left;
                fillImg.fillAmount    = 0f;

                card.progressTrack = trackImg;
                card.progressBar   = fillImg;
            }

            Transform icons = root.transform.Find("Icons");
            if (icons != null)
            {
                var ir = icons.GetComponent<RectTransform>();
                ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(1f, 0f);
                ir.sizeDelta        = new Vector2(400f, 34f);
                ir.anchoredPosition = new Vector2(-16f, 12f);

                var layout = icons.GetComponent<HorizontalLayoutGroup>();
                if (layout != null) layout.spacing = 8f;
            }

            PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return 1;
    }

    private static void Place(TMP_Text t, Vector2 anchor, Vector2 pos, Vector2 size,
                              float fontSize, TextAlignmentOptions align)
    {
        if (t == null) return;
        Undo.RecordObject(t, "Re-layout prompt panel");
        RectTransform r = t.rectTransform;
        Undo.RecordObject(r, "Re-layout prompt panel");
        r.anchorMin = r.anchorMax = r.pivot = anchor;
        r.sizeDelta        = size;
        r.anchoredPosition = pos;
        t.fontSize  = fontSize;
        t.alignment = align;
        EditorUtility.SetDirty(t);
    }

    /// <summary>Make one PNG import as a usable Sprite. Returns true if changed.</summary>
    private static bool EnsureSpriteImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;
        if (importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Single) return false;

        importer.textureType         = TextureImporterType.Sprite;
        importer.spriteImportMode    = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.filterMode          = FilterMode.Point;
        importer.SaveAndReimport();
        return true;
    }


    [MenuItem("Tools/Rat King/Setup Tutorial Scene", false, 1)]
    public static void Setup()
    {
        // ── Root ──────────────────────────────────────────────────────────────
        // Deliberately NOT "Tutorial" — that name is taken by the room model
        // (Tutorial.fbx). Parenting UI under an imported model instance would
        // make every child a prefab override and the scene refs wouldn't stick.
        GameObject root = GameObject.Find("TutorialSystem");
        if (root == null) root = GameObject.Find("TutorialHUD");
        if (root == null)
        {
            root = new GameObject("TutorialSystem");
            Undo.RegisterCreatedObjectUndo(root, "Create TutorialSystem root");
        }
        root.name = "TutorialSystem";

        // A prefab instance can't serialise references to scene Transforms, which
        // is exactly what the manager needs, so break the link.
        if (PrefabUtility.IsPartOfPrefabInstance(root))
        {
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            Debug.Log("[TutorialSetup] Unpacked the prefab instance on the root.");
        }

        // ── GameManager ───────────────────────────────────────────────────────
        // The tutorial needs one to leave the tutorial at all. It's normally
        // created in MainMenu and carried over by DontDestroyOnLoad, so pressing
        // Play directly on this scene leaves ContinueToGame with nothing to call.
        // GameManager already guards against duplicates (Awake destroys itself if
        // an Instance exists), so a copy sitting here is harmless when the player
        // arrives the normal way — it just self-destructs.
        if (Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include) == null)
        {
            var gmGO = new GameObject("GameManager");
            Undo.RegisterCreatedObjectUndo(gmGO, "Create GameManager");
            gmGO.AddComponent<GameManager>();
            Debug.Log("[TutorialSetup] Added a GameManager to this scene so the " +
                      "tutorial can be played and exited standalone. It destroys " +
                      "itself when you enter from MainMenu.");
        }

        // ── Manager, start point, spawn points ────────────────────────────────
        GameObject managerGO = Child(root, "TutorialManager");
        var manager = Ensure<TutorialManager>(managerGO);

        GameObject startPoint = Child(root, "StartPoint");
        GameObject spawnRoot  = Child(root, "SpawnPoints");
        var spawnPoints = new List<Transform>();
        foreach (string n in SpawnPointNames) spawnPoints.Add(Child(spawnRoot, n).transform);

        // ── Fade canvas ───────────────────────────────────────────────────────
        GameObject fadeCanvasGO = Child(root, "FadeCanvas");
        Canvas fadeCanvas = Ensure<Canvas>(fadeCanvasGO);
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 999;
        Ensure<CanvasScaler>(fadeCanvasGO);
        Ensure<GraphicRaycaster>(fadeCanvasGO);

        GameObject fadeQuad = UIChild(fadeCanvasGO, "FadeQuad");
        Stretch(fadeQuad);
        Image fadeImg = Ensure<Image>(fadeQuad);
        fadeImg.color = Color.black;
        fadeImg.raycastTarget = false;
        CanvasGroup fadeGroup = Ensure<CanvasGroup>(fadeQuad);
        fadeGroup.alpha = 0f;
        fadeGroup.interactable = false;
        fadeGroup.blocksRaycasts = false;
        var fader = Ensure<ScreenFader>(fadeQuad);
        fader.group = fadeGroup;

        // ── Tutorial canvas ───────────────────────────────────────────────────
        GameObject uiCanvasGO = Child(root, "TutorialCanvas");
        Canvas uiCanvas = Ensure<Canvas>(uiCanvasGO);
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiCanvas.sortingOrder = 100;
        var scaler = Ensure<CanvasScaler>(uiCanvasGO);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        Ensure<GraphicRaycaster>(uiCanvasGO);

        // Objective list — top right.
        GameObject listGO = UIChild(uiCanvasGO, "ObjectiveList");
        RectTransform listRect = listGO.GetComponent<RectTransform>();
        listRect.anchorMin = listRect.anchorMax = listRect.pivot = new Vector2(1f, 1f);
        listRect.sizeDelta = new Vector2(360f, 600f);
        listRect.anchoredPosition = new Vector2(-24f, -140f);
        var list = Ensure<ObjectiveListUI>(listGO);
        list.container = listRect;
        list.cardPrefab = LoadOrCreateCardPrefab();

        // Combo counter — sits above the prompt panel. Self-building: it makes its
        // own pips, so there's nothing to wire.
        GameObject comboGO = UIChild(uiCanvasGO, "ComboCounter");
        RectTransform comboRect = comboGO.GetComponent<RectTransform>();
        if (comboGO.GetComponent<ComboCounterUI>() == null)
        {
            comboRect.anchorMin = comboRect.anchorMax = comboRect.pivot = new Vector2(0.5f, 0f);
            comboRect.sizeDelta        = new Vector2(220f, 48f);
            comboRect.anchoredPosition = new Vector2(0f, 420f);
            Undo.AddComponent<ComboCounterUI>(comboGO);
        }

        // Prompt panel — bottom centre.
        GameObject promptGO = UIChild(uiCanvasGO, "PromptPanel");
        RectTransform promptRect = promptGO.GetComponent<RectTransform>();
        promptRect.anchorMin = promptRect.anchorMax = promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.sizeDelta = new Vector2(900f, 190f);
        promptRect.anchoredPosition = new Vector2(0f, 90f);
        CanvasGroup promptGroup = Ensure<CanvasGroup>(promptGO);
        promptGroup.alpha = 0f;
        promptGroup.interactable = false;
        promptGroup.blocksRaycasts = false;

        TMP_Text sectionLabel = Label(promptGO, "SectionLabel", "Movement", 28,
                                      new Vector2(0f, 74f),  new Vector2(880f, 36f));
        TMP_Text promptText   = Label(promptGO, "PromptText",   "", 34,
                                      new Vector2(0f, 14f),  new Vector2(880f, 90f));
        TMP_Text continueHint = Label(promptGO, "ContinueHint", "", 22,
                                      new Vector2(0f, -52f), new Vector2(880f, 30f));
        TMP_Text skipHint     = Label(promptGO, "SkipHint",     "", 20,
                                      new Vector2(0f, -78f), new Vector2(880f, 26f));
        sectionLabel.color = new Color(0.81f, 0.62f, 0.14f);
        continueHint.color = new Color(1f, 1f, 1f, 0.7f);
        skipHint.color     = new Color(1f, 1f, 1f, 0.45f);

        GameObject skipFillGO = UIChild(promptGO, "SkipFill");
        RectTransform skipRect = skipFillGO.GetComponent<RectTransform>();
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(0.5f, 0f);
        skipRect.sizeDelta = new Vector2(220f, 5f);
        skipRect.anchoredPosition = new Vector2(0f, -96f);
        Image skipFill = Ensure<Image>(skipFillGO);
        skipFill.type = Image.Type.Filled;
        skipFill.fillMethod = Image.FillMethod.Horizontal;
        skipFill.fillAmount = 0f;
        skipFill.color = new Color(0.81f, 0.62f, 0.14f);
        skipFill.raycastTarget = false;

        // End prompt — centre, starts off.
        GameObject endGO = UIChild(uiCanvasGO, "EndPromptPanel");
        RectTransform endRect = endGO.GetComponent<RectTransform>();
        endRect.anchorMin = endRect.anchorMax = endRect.pivot = new Vector2(0.5f, 0.5f);
        endRect.sizeDelta = new Vector2(620f, 260f);
        endRect.anchoredPosition = Vector2.zero;
        Image endBg = Ensure<Image>(endGO);
        endBg.color = new Color(0.05f, 0.05f, 0.06f, 0.92f);

        Label(endGO, "EndLabel", "Tutorial complete", 36,
              new Vector2(0f, 70f), new Vector2(560f, 50f));

        Button restart  = MakeButton(endGO, "Btn_Restart",  "Restart tutorial", new Vector2(-150f, -40f));
        Button continueB = MakeButton(endGO, "Btn_Continue", "Continue",         new Vector2( 150f, -40f));
        WireButton(restart,  manager, "RestartTutorial");
        WireButton(continueB, manager, "ContinueToGame");

        // Don't create one — this project is on the new Input System, so the
        // right module is InputSystemUIInputModule, and a second EventSystem is a
        // bug this project has already been bitten by. Just say something.
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
            Debug.LogWarning("[TutorialSetup] No EventSystem in the scene — the end-prompt " +
                             "buttons won't be clickable. Your UIs prefab normally brings one.");

        endGO.SetActive(false);

        // ── Wire the manager ──────────────────────────────────────────────────
        Undo.RecordObject(manager, "Wire TutorialManager");
        manager.promptPanel    = promptGroup;
        manager.promptText     = promptText;
        manager.sectionLabel   = sectionLabel;
        manager.continueHint   = continueHint;
        manager.objectiveList  = list;
        manager.endPromptPanel = endGO;
        manager.skipFill       = skipFill;
        manager.skipHint       = skipHint;
        manager.startPoint     = startPoint.transform;

        // Pre-fill the four waves the default flow expects, keeping whatever
        // prefabs you've already assigned.
        EnsureWaves(manager, spawnPoints);

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(list);
        EditorSceneManager.MarkSceneDirty(root.scene);

        Selection.activeGameObject = managerGO;
        Debug.Log("[TutorialSetup] Done. Remaining by hand: drop enemy prefabs into the " +
                  "five waves, move the six spawn points where you want them, and run " +
                  "Tools ▸ Rat King ▸ Fill Objective Icon Library.");
    }

    // ── Waves ─────────────────────────────────────────────────────────────────

    private static void EnsureWaves(TutorialManager m, List<Transform> points)
    {
        // 0 dummy · 1 grunt · 2 balloon · 3 light + heavy armour
        while (m.waves.Count < 4) m.waves.Add(new TutorialManager.Wave());

        SetWave(m.waves[0], "dummy",       true,  true,  true,  points, 0);
        SetWave(m.waves[1], "grunt",       false, false, false, points, 1);
        SetWave(m.waves[2], "balloon",     false, false, false, points, 2);
        SetWave(m.waves[3], "armour-pair", true,  true,  true,  points, 3, 4);

        // Bolted down: a teaching target that knockback can walk out of the room
        // stops being a teaching target.
        m.waves[0].immovable = true;
        m.waves[3].immovable = true;

        // One level's worth of XP off the first kill, so the level-up and
        // stat-spend lesson has an actual point to spend.
        m.waves[0].xpOverride   = 10;
        m.waves[0].coinOverride = 5;

        // Give the later arrivals room to breathe — landing on top of the
        // previous fight is what made it feel rushed.
        m.waves[1].initialDelay = 1.5f;
        m.waves[2].initialDelay = 3.0f;
        m.waves[3].initialDelay = 3.0f;
        m.waves[3].betweenDelay = 1.6f;
    }

    private static void SetWave(TutorialManager.Wave w, string id,
                                bool passive, bool stationary, bool invuln,
                                List<Transform> points, params int[] pointIndices)
    {
        if (string.IsNullOrEmpty(w.id) || w.id == "wave") w.id = id;
        w.passive    = passive;
        w.stationary = stationary;
        w.invulnerableWhileTeaching = invuln;

        while (w.spawns.Count < pointIndices.Length)
            w.spawns.Add(new TutorialManager.SpawnEntry());

        for (int i = 0; i < pointIndices.Length; i++)
        {
            int p = pointIndices[i];
            if (w.spawns[i].point == null && p < points.Count) w.spawns[i].point = points[p];
        }
    }

    // ── Card prefab ───────────────────────────────────────────────────────────

    private static ObjectiveCardUI LoadOrCreateCardPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (existing != null)
        {
            var found = existing.GetComponent<ObjectiveCardUI>();
            if (found != null) return found;
        }

        string dir = Path.GetDirectoryName(CardPrefabPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(dir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }

        // Build it in memory, save it, throw the temp copy away.
        var card = new GameObject("ObjectiveCard", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform cr = card.GetComponent<RectTransform>();
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
        cr.sizeDelta = new Vector2(440f, 88f);

        GameObject bg = UIChild(card, "Background");
        Stretch(bg);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.06f, 0.78f);
        bgImg.raycastTarget = false;

        TMP_Text label = Label(card, "Label", "Objective", 20,
                               new Vector2(-8f, 14f), new Vector2(400f, 34f));
        label.alignment = TextAlignmentOptions.MidlineRight;
        RectTransform lr = label.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(1f, 1f);
        lr.anchoredPosition = new Vector2(-16f, -10f);

        TMP_Text counter = Label(card, "Counter", "", 16, Vector2.zero, new Vector2(70f, 22f));
        counter.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform cnr = counter.rectTransform;
        cnr.anchorMin = cnr.anchorMax = cnr.pivot = new Vector2(0f, 0f);
        cnr.anchoredPosition = new Vector2(16f, 12f);
        counter.color = new Color(1f, 1f, 1f, 0.6f);

        GameObject icons = UIChild(card, "Icons");
        RectTransform ir = icons.GetComponent<RectTransform>();
        ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(1f, 0f);
        ir.sizeDelta = new Vector2(400f, 34f);
        ir.anchoredPosition = new Vector2(-16f, 12f);
        var layout = icons.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = 8f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        var slots = new Image[6];
        for (int i = 0; i < slots.Length; i++)
        {
            GameObject icon = UIChild(icons, "Icon_" + i);
            icon.GetComponent<RectTransform>().sizeDelta = new Vector2(30f, 30f);
            Image img = icon.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            slots[i] = img;
            icon.SetActive(false);
        }

        // Hold bar across the bottom edge: a faint track with a green fill inside
        // it, so an empty bar still reads as "nothing yet".
        GameObject trackGO = UIChild(card, "ProgressTrack");
        RectTransform trackR = trackGO.GetComponent<RectTransform>();
        trackR.anchorMin = new Vector2(0f, 0f);
        trackR.anchorMax = new Vector2(1f, 0f);
        trackR.pivot     = new Vector2(0.5f, 0f);
        trackR.offsetMin = new Vector2(10f, 6f);
        trackR.offsetMax = new Vector2(-10f, 11f);
        Image trackImg = trackGO.AddComponent<Image>();
        trackImg.color         = new Color(1f, 1f, 1f, 0.10f);
        trackImg.raycastTarget = false;

        GameObject fillGO = UIChild(trackGO, "Fill");
        Stretch(fillGO);
        Image fillImg = fillGO.AddComponent<Image>();
        fillImg.color         = new Color(0.30f, 0.85f, 0.35f, 1f);
        fillImg.raycastTarget = false;
        fillImg.type          = Image.Type.Filled;
        fillImg.fillMethod    = Image.FillMethod.Horizontal;
        fillImg.fillOrigin    = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount    = 0f;

        var comp = card.AddComponent<ObjectiveCardUI>();
        comp.progressTrack = trackImg;
        comp.progressBar   = fillImg;
        comp.rect      = cr;
        comp.group     = card.GetComponent<CanvasGroup>();
        comp.label     = label;
        comp.counter   = counter;
        comp.iconSlots = slots;

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(card, CardPrefabPath);
        Object.DestroyImmediate(card);
        AssetDatabase.SaveAssets();
        Debug.Log("[TutorialSetup] Created the card prefab at " + CardPrefabPath);
        return asset.GetComponent<ObjectiveCardUI>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameObject Child(GameObject parent, string name)
    {
        Transform t = parent.transform.Find(name);
        if (t != null) return t.gameObject;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject UIChild(GameObject parent, string name)
    {
        Transform t = parent.transform.Find(name);
        if (t != null)
        {
            if (t.GetComponent<RectTransform>() == null)
                Debug.LogWarning($"[TutorialSetup] '{name}' exists but isn't a UI object.");
            return t.gameObject;
        }

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    private static void Stretch(GameObject go)
    {
        RectTransform r = go.GetComponent<RectTransform>();
        if (r == null) return;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    private static TMP_Text Label(GameObject parent, string name, string text, float size,
                                  Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = UIChild(parent, name);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        bool fresh = tmp == null;
        if (fresh) tmp = Undo.AddComponent<TextMeshProUGUI>(go);

        RectTransform r = go.GetComponent<RectTransform>();
        if (fresh)
        {
            // Only lay it out the first time — don't stomp your own tweaks.
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = sizeDelta;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }
        return tmp;
    }

    private static Button MakeButton(GameObject parent, string name, string text, Vector2 pos)
    {
        GameObject go = UIChild(parent, name);
        RectTransform r = go.GetComponent<RectTransform>();
        Image img = go.GetComponent<Image>();
        bool fresh = img == null;
        if (fresh)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(250f, 64f);
            r.anchoredPosition = pos;
            img = Undo.AddComponent<Image>(go);
            img.color = new Color(0.16f, 0.16f, 0.18f, 1f);
        }

        Button b = go.GetComponent<Button>();
        if (b == null) b = Undo.AddComponent<Button>(go);
        b.targetGraphic = img;

        TMP_Text t = Label(go, "Text", text, 24, Vector2.zero, new Vector2(230f, 50f));
        Stretch(t.gameObject);
        return b;
    }

    /// <summary>
    /// Adds a persistent onClick listener — the kind that shows up in the
    /// inspector, not a runtime-only one that would vanish on scene reload.
    /// Skips if the same call is already wired, so re-running doesn't stack them.
    /// </summary>
    private static void WireButton(Button b, TutorialManager m, string method)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (b.onClick.GetPersistentTarget(i) == m && b.onClick.GetPersistentMethodName(i) == method)
                return;

        UnityEngine.Events.UnityAction call = method == "RestartTutorial"
            ? (UnityEngine.Events.UnityAction)m.RestartTutorial
            : m.ContinueToGame;

        UnityEventTools.AddPersistentListener(b.onClick, call);
        EditorUtility.SetDirty(b);
    }
}
#endif
