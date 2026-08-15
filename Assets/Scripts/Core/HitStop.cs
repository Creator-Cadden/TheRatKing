using UnityEngine;

/// <summary>
/// Global hit-stop that reads as a smooth impact slow-mo instead of a jarring
/// freeze: time dips to a low (non-zero) scale on impact, holds briefly, then
/// EASES back up to normal speed. Non-stacking — overlapping hits keep the
/// longest hold. Restores whatever timeScale was active before (plays nice with
/// a pause menu). Auto-creates its runner.
/// </summary>
public class HitStop : MonoBehaviour
{
    private static HitStop _inst;

    private enum Phase { Idle, Held, Recovering }
    private Phase _phase = Phase.Idle;

    private float _restoreScale = 1f;
    private float _frozenScale  = 0.08f;
    private float _holdUntil;         // unscaled time the dip holds until
    private float _recoverDuration;
    private float _recoverStart;

    /// <summary>
    /// Impact slow-mo. <paramref name="seconds"/> = how long the dip HOLDS,
    /// <paramref name="frozenScale"/> = speed during the dip (0.08 ≈ strong slow-mo,
    /// NOT a dead stop — much less jarring than 0), <paramref name="recoverDuration"/>
    /// = how long it eases back up to normal afterward.
    /// </summary>
    public static void Freeze(float seconds, float frozenScale = 0.08f, float recoverDuration = 0.12f)
    {
        if (seconds <= 0f) return;
        Ensure();
        _inst.DoFreeze(seconds, frozenScale, recoverDuration);
    }

    private static void Ensure()
    {
        if (_inst != null) return;
        var go = new GameObject("[HitStop]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        _inst = go.AddComponent<HitStop>();
    }

    private void DoFreeze(float seconds, float frozenScale, float recoverDuration)
    {
        // Don't hijack a genuine pause (someone else already froze the game).
        if (_phase == Phase.Idle && Time.timeScale == 0f) return;

        // Capture the pre-hitstop speed only when starting fresh.
        if (_phase == Phase.Idle)
            _restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;

        _frozenScale     = frozenScale;
        _recoverDuration = recoverDuration;

        float end = Time.unscaledTime + seconds;
        if (!(_phase == Phase.Held && end <= _holdUntil))   // non-stacking: keep the longer hold
            _holdUntil = end;

        _phase         = Phase.Held;
        Time.timeScale = _frozenScale;
    }

    void Update()
    {
        if (_phase == Phase.Idle) return;

        if (_phase == Phase.Held)
        {
            if (Time.unscaledTime < _holdUntil) return;
            _phase        = Phase.Recovering;
            _recoverStart = Time.unscaledTime;
        }

        // Ease the speed back up — smooth, not a snap.
        if (_recoverDuration <= 0f)
        {
            Time.timeScale = _restoreScale;
            _phase = Phase.Idle;
            return;
        }

        float t = (Time.unscaledTime - _recoverStart) / _recoverDuration;
        if (t >= 1f)
        {
            Time.timeScale = _restoreScale;
            _phase = Phase.Idle;
        }
        else
        {
            // Smoothstep for an ease-out feel.
            Time.timeScale = Mathf.Lerp(_frozenScale, _restoreScale, t * t * (3f - 2f * t));
        }
    }
}
