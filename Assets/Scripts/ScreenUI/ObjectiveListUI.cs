using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The task list down the right-hand side of the screen. Cards slide in, sit in
/// a stack, and slide back out when their task is done — the cards below move up
/// to take the vacated slot.
///
/// Nothing in here knows about the tutorial. It's an objective list: hand it an
/// id, a line of text and a few key-icon names and it draws a row. The tutorial
/// is the first caller; quest objectives, room goals and "press F to interact"
/// hints can all reuse it later.
///
/// SETUP:
///   Canvas
///     └ ObjectiveList        RectTransform, anchor + pivot TOP-RIGHT,
///                            nudged in from the edge (e.g. anchoredPos -24, -120)
///        + ObjectiveListUI   container = itself, cardPrefab = the card prefab
///
/// Fill Icon Library once with every key sprite you have — "W", "A", "S", "D",
/// "SPACE", "SHIFT", "CTRL", "TAB", "LMB", "RMB", "E". Ids are case-insensitive.
/// An id with no sprite in the library is skipped rather than drawing a blank.
/// </summary>
public class ObjectiveListUI : MonoBehaviour
{
    [System.Serializable]
    public class KeyIcon
    {
        [Tooltip("What steps refer to this by, e.g. \"W\" or \"SPACE\".")]
        public string id = "";
        [Tooltip("The white / not-yet-done version.")]
        public Sprite sprite;
        [Tooltip("Optional separate 'done' art. Leave empty to just tint the " +
                 "sprite above with Complete Color.")]
        public Sprite completedSprite;
    }

    [Header("Refs")]
    [Tooltip("Cards are parented here. Anchor + pivot should be top-right.")]
    public RectTransform container;
    public ObjectiveCardUI cardPrefab;

    [Header("Icon library")]
    public List<KeyIcon> iconLibrary = new List<KeyIcon>();

    [Header("Layout")]
    public float rowHeight = 64f;
    public float spacing   = 8f;

    [Header("Motion")]
    [Tooltip("How far right of its slot a card starts before sliding in.")]
    public float enterOffsetX = 420f;
    [Tooltip("How far right a completed card slides before it's destroyed.")]
    public float exitOffsetX  = 420f;
    public float lerpSpeed    = 12f;

    [Header("Colours")]
    public Color incompleteColor = Color.white;
    public Color completeColor   = new Color(0.30f, 0.85f, 0.35f, 1f);

    private readonly List<ObjectiveCardUI> _cards = new List<ObjectiveCardUI>();

    void Awake()
    {
        if (container == null) container = GetComponent<RectTransform>();
    }

    void Update()
    {
        // Reap finished cards. Done here rather than in the card so the list is
        // the only thing that ever changes the collection.
        bool removed = false;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            ObjectiveCardUI c = _cards[i];
            if (c == null) { _cards.RemoveAt(i); removed = true; continue; }
            if (c.ReadyToDestroy) { _cards.RemoveAt(i); Destroy(c.gameObject); removed = true; }
        }
        if (removed) Relayout();
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Add a row. Returns the card, or null if the prefab is missing.</summary>
    public ObjectiveCardUI Add(string id, string text, IEnumerable<string> iconIds)
    {
        if (cardPrefab == null || container == null)
        {
            Debug.LogWarning("[ObjectiveListUI] No card prefab or container assigned.");
            return null;
        }

        var idle = new List<Sprite>();
        var done = new List<Sprite>();
        if (iconIds != null)
        {
            foreach (string key in iconIds)
            {
                KeyIcon k = Lookup(key);
                if (k == null || k.sprite == null)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        Debug.LogWarning($"[ObjectiveListUI] No icon in the library for \"{key}\".");
                    continue;
                }
                idle.Add(k.sprite);
                done.Add(k.completedSprite);
            }
        }

        ObjectiveCardUI card = Instantiate(cardPrefab, container);
        card.gameObject.SetActive(true);
        _cards.Add(card);

        card.Init(id, text, idle, done, incompleteColor, completeColor,
                  SlotFor(_cards.Count - 1), enterOffsetX, lerpSpeed);
        Relayout();
        return card;
    }

    /// <summary>White → green on one icon of one card.</summary>
    public void MarkIcon(string id, int index, bool done = true)
        => Find(id)?.MarkIcon(index, done);

    /// <summary>Set (or clear, with need &lt;= 1) the "2 / 3" counter on a card.</summary>
    public void SetCounter(string id, int have, int need)
    {
        ObjectiveCardUI c = Find(id);
        if (c == null) return;
        c.SetCounter(need > 1 ? $"{Mathf.Min(have, need)} / {need}" : "");
    }

    /// <summary>Light every icon, then slide the card away. Others move up.</summary>
    public void Complete(string id)
    {
        ObjectiveCardUI c = Find(id);
        if (c == null) return;
        c.MarkAllIcons();
        c.SetCounter("");
        c.Exit(exitOffsetX);
        Relayout();
    }

    /// <summary>Send every card away (a phase was skipped).</summary>
    public void CompleteAll()
    {
        foreach (ObjectiveCardUI c in _cards)
            if (c != null && !c.IsExiting) c.Exit(exitOffsetX);
        Relayout();
    }

    /// <summary>Destroy every card immediately, no animation.</summary>
    public void Clear()
    {
        foreach (ObjectiveCardUI c in _cards)
            if (c != null) Destroy(c.gameObject);
        _cards.Clear();
    }

    public bool Has(string id) => Find(id) != null;

    // ── Internals ─────────────────────────────────────────────────────────────

    private ObjectiveCardUI Find(string id)
    {
        foreach (ObjectiveCardUI c in _cards)
            if (c != null && !c.IsExiting && c.Id == id) return c;
        return null;
    }

    private KeyIcon Lookup(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (KeyIcon k in iconLibrary)
            if (k != null && string.Equals(k.id, key, System.StringComparison.OrdinalIgnoreCase))
                return k;
        return null;
    }

    private Vector2 SlotFor(int row) => new Vector2(0f, -row * (rowHeight + spacing));

    /// <summary>
    /// Re-number the rows. Cards on their way out are skipped, so the moment one
    /// leaves, everything under it is handed the slot above and glides up.
    /// </summary>
    private void Relayout()
    {
        int row = 0;
        foreach (ObjectiveCardUI c in _cards)
        {
            if (c == null || c.IsExiting) continue;
            c.SetSlot(SlotFor(row));
            row++;
        }
    }
}
