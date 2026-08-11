using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Global camera juice — screen shake + FOV punch + a sustained sprint FOV widen.
/// Call the statics from anywhere; it auto-creates its runner and finds the Main
/// Camera (for shake) and the Cinemachine vcams (for FOV). Runs AFTER the
/// CinemachineBrain (high execution order) so the shake offset isn't overwritten.
/// </summary>
[DefaultExecutionOrder(10000)]
public class CameraJuice : MonoBehaviour
{
    private static CameraJuice _inst;

    [Header("Shake")]
    [Tooltip("How fast shake fades (trauma per second).")]
    public float traumaDecay = 1.8f;
    [Tooltip("Max positional shake at full trauma (world units).")]
    public float maxShakePos = 0.35f;
    [Tooltip("Max rotational shake at full trauma (degrees).")]
    public float maxShakeRot = 2.5f;

    [Header("FOV")]
    [Tooltip("Extra FOV added while sprinting (speed sensation). Keep modest to avoid stretch.")]
    public float sprintFovDelta = 6f;
    [Tooltip("How fast FOV eases toward its target.")]
    public float fovLerp = 8f;

    private Transform _camT;
    private float     _trauma;

    private CinemachineCamera _freeLook, _aim;
    private float _baseFovFree, _baseFovAim;
    private bool  _fovCaptured;
    private float _fovPunch;          // transient additive FOV
    private float _sprintFovTarget;   // 0 or sprintFovDelta
    private float _sprintFovCurrent;

    // ── Public API ──

    /// <summary>Add screen shake (trauma 0..1 — 0.1 tap, 0.3 solid, 0.6 big).</summary>
    public static void Shake(float trauma)
    {
        Ensure();
        _inst._trauma = Mathf.Clamp01(_inst._trauma + trauma);
    }

    /// <summary>Briefly kick the FOV (positive = widen, negative = zoom in).</summary>
    public static void PunchFOV(float amount)
    {
        Ensure();
        _inst._fovPunch += amount;
    }

    /// <summary>Sustained sprint FOV widen — call each frame with the sprint state.</summary>
    public static void SetSprint(bool sprinting)
    {
        Ensure();
        _inst._sprintFovTarget = sprinting ? _inst.sprintFovDelta : 0f;
    }

    private static void Ensure()
    {
        if (_inst != null) return;
        var go = new GameObject("[CameraJuice]");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<CameraJuice>();
    }

    void Update()
    {
        if (_camT == null && Camera.main != null) _camT = Camera.main.transform;
        ResolveVcams();

        _trauma = Mathf.Max(0f, _trauma - traumaDecay * Time.unscaledDeltaTime);

        _fovPunch         = Mathf.Lerp(_fovPunch, 0f, fovLerp * Time.deltaTime);
        _sprintFovCurrent = Mathf.Lerp(_sprintFovCurrent, _sprintFovTarget, fovLerp * Time.deltaTime);

        ApplyFOV();
    }

    void LateUpdate()
    {
        if (_camT == null) return;

        float shake = _trauma * _trauma;   // squared = punchier ramp
        if (shake <= 0.0001f) return;

        Vector3 posOffset = new Vector3(Random.value * 2f - 1f,
                                        Random.value * 2f - 1f, 0f) * (maxShakePos * shake);
        Vector3 rotOffset = new Vector3(Random.value * 2f - 1f,
                                        Random.value * 2f - 1f,
                                        Random.value * 2f - 1f) * (maxShakeRot * shake);

        // Applied AFTER the CinemachineBrain placed the camera this frame.
        _camT.position += _camT.rotation * posOffset;
        _camT.rotation *= Quaternion.Euler(rotOffset);
    }

    private void ResolveVcams()
    {
        if (_freeLook != null && _aim != null) return;

        var cams = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach (var c in cams)
        {
            bool isFree = c.GetComponent<CinemachineOrbitalFollow>() != null;
            if (isFree && _freeLook == null) _freeLook = c;
            else if (!isFree && _aim == null) _aim = c;
        }

        if (!_fovCaptured && _freeLook != null)
        {
            _baseFovFree = _freeLook.Lens.FieldOfView;
            _baseFovAim  = _aim != null ? _aim.Lens.FieldOfView : _baseFovFree;
            _fovCaptured = true;
        }
    }

    private void ApplyFOV()
    {
        if (!_fovCaptured) return;

        if (_freeLook != null)
            _freeLook.Lens.FieldOfView = _baseFovFree + _fovPunch + _sprintFovCurrent;
        if (_aim != null)
            _aim.Lens.FieldOfView = _baseFovAim + _fovPunch;   // aim doesn't get the sprint widen
    }
}
