using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Global audio singleton (AudioManager.Instance). Lives on the Audio prefab dropped
/// into each level. Play one-shots or loops via Play(SoundType) / PlayLooping(SoundType);
/// add new sounds to the SoundType enum + the clip list in the Inspector.
/// </summary>
public class AudioManager : MonoBehaviour
{

    private void Start()
    {
        PlayLooping(SoundType.Eviro);
    }

    public void PlayLooping(SoundType type)
    {
        if (!_soundDictionary.TryGetValue(type, out Sound s))
        {
            Debug.LogWarning($"Sound type {type} not found!");
            return;
        }

        var soundObj = new GameObject($"Sound_{type}_Loop");
        var audioSrc = soundObj.AddComponent<AudioSource>();
        audioSrc.clip = s.Clip;
        audioSrc.volume = s.Volume;
        audioSrc.loop = true;          // Loop it
        DontDestroyOnLoad(soundObj);   // Persist across scenes if needed
        audioSrc.Play();

        s.Source = audioSrc; // Store reference so you can stop it later
    }

    public void PlayDelayed(SoundType type, float delay)
    {
        StartCoroutine(DelayedPlay(type, delay));
    }

    private IEnumerator DelayedPlay(SoundType type, float delay)
    {
        yield return new WaitForSeconds(delay);
        Play(type);
    }

    public enum SoundType
    {
        Jump,
        Xp,
        Attack,
        Music_Menu,
        Eviro,
        AirAttk,
        EnemyAttk,
        EnemyDeath,
        BowDraw,
        BowShot,
        BowFullDraw,
        HammerSwing,
        BladeSwing,
        HitShrug,     // powered-through hit (no reaction)
        HitFlinch,    // flinch-tier hit
        HitStagger,   // stagger-tier hit
        HitDelayed,   // decal hyper-armor "delayed" reaction
        // Add more sound types as needed
    }

    [System.Serializable]
    public class Sound
    {
        public SoundType Type;

        [Tooltip("Single clip. If 'Clips' (variations) below has entries, those are used instead.")]
        public AudioClip Clip;

        [Tooltip("Variations — a random one plays each time. Leave empty to just use Clip. " +
                 "Drop your 3 bow-shot / 2 bow-draw / 3 hammer clips here.")]
        public AudioClip[] Clips;

        [Range(0f, 1f)]
        public float Volume = 1f;

        [Tooltip("Random pitch range per play (1,1 = none). ~0.95–1.05 adds subtle life " +
                 "so variations don't sound identical.")]
        public Vector2 PitchRange = new Vector2(1f, 1f);

        [HideInInspector]
        public AudioSource Source;
    }

    /// <summary>Picks a random variation if any, else the single Clip.</summary>
    private static AudioClip PickClip(Sound s)
    {
        if (s.Clips != null && s.Clips.Length > 0)
            return s.Clips[Random.Range(0, s.Clips.Length)];
        return s.Clip;
    }

    //Singleton
    public static AudioManager Instance;

    //All sounds and their associated type - Set these in the inspector
    public Sound[] AllSounds;

    //Runtime collections
    private Dictionary<SoundType, Sound> _soundDictionary = new Dictionary<SoundType, Sound>();
    private AudioSource _musicSource;

    private void Awake()
    {
        //Assign singleton
        Instance = this;

        //Set up sounds
        foreach (var s in AllSounds)
        {
            _soundDictionary[s.Type] = s;
        }
    }



    //Call this method to play a sound
    public void Play(SoundType type)
    {
        //Make sure there's a sound assigned to your specified type
        if (!_soundDictionary.TryGetValue(type, out Sound s))
        {
            Debug.LogWarning($"Sound type {type} not found!");
            return;
        }

        AudioClip clip = PickClip(s);
        if (clip == null)
        {
            Debug.LogWarning($"Sound type {type} has no clip assigned!");
            return;
        }

        //Creates a new sound object
        var soundObj = new GameObject($"Sound_{type}");
        var audioSrc = soundObj.AddComponent<AudioSource>();

        //Assigns your sound properties (random variation + pitch)
        audioSrc.clip = clip;
        audioSrc.volume = s.Volume;
        audioSrc.pitch = Random.Range(s.PitchRange.x, s.PitchRange.y);

        //Play the sound
        audioSrc.Play();

        //Destroy the object once it's finished (account for pitch changing length)
        Destroy(soundObj, clip.length / Mathf.Max(0.05f, audioSrc.pitch) + 0.1f);
    }

    //Call this method to change music tracks
    public void ChangeMusic(SoundType type)
    {
        if (!_soundDictionary.TryGetValue(type, out Sound track))
        {
            Debug.LogWarning($"Music track {type} not found!");
            return;
        }

        if (_musicSource == null)
        {
            var container = new GameObject("SoundTrackObj");
            _musicSource = container.AddComponent<AudioSource>();
            _musicSource.loop = true;
        }

        _musicSource.clip = track.Clip;
        _musicSource.Play();
    }
}