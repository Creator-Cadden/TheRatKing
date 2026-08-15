using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Distance-based footsteps for the rat. Emits steps per distance TRAVELLED, so
/// the cadence tracks the real movement speed automatically — walk, sprint, and
/// Speed-stat changes all just work, and there's no foot-sliding audio.
///
/// Each stride optionally fires a "front feet then back feet" pair; the gap
/// between them is a fraction of the stride time, so it tightens as the rat
/// speeds up (matching the sped-up animation). A downward raycast reads the
/// SurfaceType under the rat to choose the clip set. Clips are randomised and
/// pitch-jittered so they don't machine-gun.
///
/// Put this on the Player (needs a CharacterController). Fill the surface sets in
/// the Inspector with your friends' one-shot paw WAVs.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FootstepSystem : MonoBehaviour
{
    [Serializable]
    public class SurfaceFootsteps
    {
        public SurfaceKind surface;
        [Tooltip("3–5 one-shot paw-plant variants; one is chosen at random per plant.")]
        public AudioClip[] clips;
    }

    [Header("Clip sets (one entry per surface)")]
    public SurfaceFootsteps[] surfaceSets;

    [Tooltip("Surface used when the ground isn't tagged with a SurfaceType or has no set.")]
    public SurfaceKind defaultSurface = SurfaceKind.Stone;

    [Header("Cadence")]
    [Tooltip("Distance (world units) the rat travels per STRIDE before a step fires. " +
             "Lower = more frequent steps. Tune to match the animation's stride length.")]
    public float strideLength = 1.2f;

    [Tooltip("Emit TWO plants per stride (front feet, then back feet). Off = one per stride.")]
    public bool frontThenBack = true;

    [Tooltip("Gap between the front-feet plant and the back-feet plant, as a FRACTION of " +
             "the stride time — so it shrinks automatically as the rat speeds up. Watch " +
             "the animation: if the back feet land ~15% of a stride after the front, use 0.15.")]
    [Range(0.02f, 0.5f)]
    public float backFootDelayFraction = 0.15f;

    [Header("Ground Raycast")]
    [Tooltip("How far down to look for the ground surface.")]
    public float rayDistance = 1.5f;
    [Tooltip("Which layers count as ground. Exclude the Player/Enemy layers.")]
    public LayerMask groundMask = ~0;

    [Header("Sound")]
    [Tooltip("Random pitch variation (±) per step so repeats don't sound identical.")]
    [Range(0f, 0.3f)] public float pitchJitter = 0.08f;
    [Range(0f, 1f)]   public float volume = 0.9f;
    [Tooltip("0 = 2D (same volume everywhere, typical for the player). 1 = full 3D.")]
    [Range(0f, 1f)]   public float spatialBlend = 0f;
    [Tooltip("Optional — route footsteps to your SFX mixer group for global volume control.")]
    public AudioMixerGroup outputGroup;

    [Header("Gate")]
    [Tooltip("Below this horizontal speed (units/sec) the rat counts as stopped — no steps.")]
    public float minSpeed = 0.3f;

    [Tooltip("Hard floor on time (seconds) between strides — stops footsteps from " +
             "machine-gunning at high sprint speeds. 0.18 ≈ max ~5 strides/sec.")]
    public float minStepInterval = 0.18f;

    // ── Runtime ──
    private CharacterController _cc;
    private AudioSource         _src;
    private Vector3             _lastPos;
    private float               _distanceAccum;
    private float               _lastStepTime = -999f;

    void Awake()
    {
        _cc  = GetComponent<CharacterController>();
        _src = GetComponent<AudioSource>();
        if (_src == null) _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake  = false;
        _src.spatialBlend = spatialBlend;
        if (outputGroup != null) _src.outputAudioMixerGroup = outputGroup;

        _lastPos = transform.position;
    }

    void Update()
    {
        Vector3 delta = transform.position - _lastPos;
        _lastPos = transform.position;
        delta.y  = 0f;

        // Not grounded → no steps, and reset the accumulator so we don't dump a
        // step the instant we land.
        if (!_cc.isGrounded) { _distanceAccum = 0f; return; }

        float dist  = delta.magnitude;
        float speed = dist / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed < minSpeed) return;

        _distanceAccum += dist;
        if (_distanceAccum >= strideLength)
        {
            if (Time.time - _lastStepTime >= minStepInterval)
            {
                _distanceAccum = 0f;
                _lastStepTime  = Time.time;
                EmitStride(speed);
            }
            else
            {
                // Rate-capped at high speed — hold at one stride so no backlog builds.
                _distanceAccum = strideLength;
            }
        }
    }

    private void EmitStride(float speed)
    {
        SurfaceKind surf = DetectSurface();
        PlayStep(surf);   // front feet

        if (frontThenBack)
        {
            // Back feet land a fraction of the stride-time after the front, so the
            // gap scales with speed (faster stride = tighter gap).
            float strideTime = strideLength / Mathf.Max(speed, 0.01f);
            StartCoroutine(BackFoot(surf, strideTime * backFootDelayFraction));
        }
    }

    private IEnumerator BackFoot(SurfaceKind surf, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayStep(surf);
    }

    /// <summary>Public hook — call from an Animation Event if you ever want a
    /// frame-perfect step instead of (or on top of) the distance-based ones.</summary>
    public void PlayFootstep() => PlayStep(DetectSurface());

    private SurfaceKind DetectSurface()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                            rayDistance, groundMask, QueryTriggerInteraction.Ignore))
            return SurfaceType.Resolve(hit.collider, defaultSurface);
        return defaultSurface;
    }

    private void PlayStep(SurfaceKind surf)
    {
        AudioClip[] clips = ClipsFor(surf);
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip == null) return;

        _src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
        _src.PlayOneShot(clip, volume);
    }

    private AudioClip[] ClipsFor(SurfaceKind surf)
    {
        if (surfaceSets != null)
            foreach (var s in surfaceSets)
                if (s.surface == surf && s.clips != null && s.clips.Length > 0)
                    return s.clips;

        // Fall back to the default surface's set (guard against infinite recursion).
        return surf != defaultSurface ? ClipsFor(defaultSurface) : null;
    }
}
