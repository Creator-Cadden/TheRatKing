using UnityEngine;

/// <summary>
/// Global hit-stop: briefly freezes time on impact to give hits weight. Call
/// <see cref="Freeze"/> from anywhere (it auto-creates its runner). Non-stacking —
/// overlapping requests keep the LONGEST freeze rather than adding up, so a swing
/// that hits five enemies in one frame produces one short freeze, not five.
/// Uses unscaled time, and restores whatever timeScale was active before (so it
/// plays nice with a pause menu).
/// </summary>
public class HitStop : MonoBehaviour
{
    private static HitStop _inst;

    private float _freezeUntil = -1f;   // unscaled time the current freeze ends
    private float _restoreScale = 1f;
    private bool  _frozen;

    /// <summary>
    /// Freeze time for <paramref name="seconds"/> (real time). <paramref name="frozenScale"/>
    /// 0 = hard freeze (punchiest); a small value like 0.05 leaves a hint of motion.
    /// </summary>
    public static void Freeze(float seconds, float frozenScale = 0f)
    {
        if (seconds <= 0f) return;
        EnsureInstance();
        _inst.DoFreeze(seconds, frozenScale);
    }

    private static void EnsureInstance()
    {
        if (_inst != null) return;
        var go = new GameObject("[HitStop]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        _inst = go.AddComponent<HitStop>();
    }

    private void DoFreeze(float seconds, float frozenScale)
    {
        // Don't hijack a genuine pause (someone else already froze the game).
        if (!_frozen && Time.timeScale == 0f) return;

        float end = Time.unscaledTime + seconds;
        if (end <= _freezeUntil) return;   // non-stacking: keep the longer freeze
        _freezeUntil = end;

        if (!_frozen)
        {
            _restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            _frozen = true;
        }
        Time.timeScale = frozenScale;
    }

    void Update()
    {
        if (_frozen && Time.unscaledTime >= _freezeUntil)
        {
            Time.timeScale = _restoreScale;
            _frozen = false;
            _freezeUntil = -1f;
        }
    }
}
