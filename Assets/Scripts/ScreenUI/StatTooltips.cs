using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Hover explanations for the stat menu. Drop this on the same object as
/// <see cref="StatMenuUI"/> and it wires a <see cref="TooltipTrigger"/> onto each
/// stat row at runtime, so hovering Strength or Speed says what the point
/// actually buys.
///
/// It needs no references: each row is found from the StatMenuUI's value label,
/// walking up to the label's parent so the whole row is hoverable rather than
/// just the number. Assign the Row fields below only if your hierarchy puts the
/// name and value in separate branches.
///
/// UI raycasting needs a Graphic to hit, so a row with no Image gets a fully
/// transparent one added — invisible, but hoverable.
/// </summary>
[RequireComponent(typeof(StatMenuUI))]
public class StatTooltips : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public string title;
        [TextArea(2, 5)] public string body;
        [Tooltip("Leave empty to use the matching value label's parent.")]
        public GameObject row;
    }

    [Header("Copy")]
    public Entry health = new Entry
    {
        title = "Health",
        body  = "<b>+10 max HP</b> per point.\nThe simplest point in the game: more " +
                "room to make a mistake."
    };

    public Entry strength = new Entry
    {
        title = "Strength",
        body  = "<b>+1 Strength</b> per point.\nYour weapon multiplies it — the blade " +
                "doubles it, the hammer quadruples it, the bow takes it straight. So " +
                "the same point is worth far more on a heavy weapon."
    };

    public Entry stamina = new Entry
    {
        title = "Stamina",
        body  = "<b>+10 max stamina</b> per point.\nSprinting drains it, and dodge " +
                "rolls and air attacks each cost a chunk. Run dry and you lose the " +
                "dodge — which is your only real defence."
    };

    public Entry speed = new Entry
    {
        title = "Speed",
        body  = "<b>+0.5 m/s</b> move speed and <b>−4% on every cooldown</b> per point.\n" +
                "It quickens attacks, rolls and the bow draw, so it compounds with " +
                "everything else you've built."
    };

    public Entry toughness = new Entry
    {
        title = "Toughness",
        body  = "Armour — how hard something is to stagger.\nEnemies have it; you don't. " +
                "Your weapon's IMPACT is measured against theirs: under it they shrug, " +
                "level with it they flinch, over it they stagger."
    };

    [Header("Behaviour")]
    [Tooltip("Seconds of hover before the bubble appears.")]
    public float delay = 0.35f;

    [Tooltip("Hover the whole row rather than just the number. Off = the value " +
             "label itself is the only hover target.")]
    public bool useParentRow = true;

    private readonly List<TooltipTrigger> _made = new List<TooltipTrigger>();

    void Start()
    {
        var menu = GetComponent<StatMenuUI>();
        if (menu == null) return;

        Wire(health,    menu.healthValueLabel);
        Wire(strength,  menu.strengthValueLabel);
        Wire(stamina,   menu.staminaValueLabel);
        Wire(speed,     menu.speedValueLabel);
        Wire(toughness, menu.toughnessValueLabel);

        if (_made.Count == 0)
            Debug.LogWarning("[StatTooltips] Wired nothing — StatMenuUI's value " +
                             "labels look unassigned.");
    }

    private void Wire(Entry e, TMP_Text valueLabel)
    {
        GameObject target = e.row;

        if (target == null && valueLabel != null)
            target = (useParentRow && valueLabel.transform.parent != null)
                ? valueLabel.transform.parent.gameObject
                : valueLabel.gameObject;

        if (target == null) return;
        if (string.IsNullOrWhiteSpace(e.body)) return;

        // Something has to catch the raycast. An invisible Image does it without
        // changing how the row looks.
        if (target.GetComponent<Graphic>() == null)
        {
            var img = target.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
        }
        foreach (Graphic g in target.GetComponents<Graphic>()) g.raycastTarget = true;

        var trigger = target.GetComponent<TooltipTrigger>();
        if (trigger == null) trigger = target.AddComponent<TooltipTrigger>();

        trigger.delay = delay;
        trigger.SetText(e.title, e.body);
        _made.Add(trigger);
    }
}
