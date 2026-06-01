using UnityEngine;
using System.Collections.Generic;
using System.Collections;

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
        // Add more sound types as needed
    }

    [System.Serializable]
    public class Sound
    {
        public SoundType Type;
        public AudioClip Clip;

        [Range(0f, 1f)]
        public float Volume = 1f;

        [HideInInspector]
        public AudioSource Source;
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

        //Creates a new sound object
        var soundObj = new GameObject($"Sound_{type}");
        var audioSrc = soundObj.AddComponent<AudioSource>();

        //Assigns your sound properties
        audioSrc.clip = s.Clip;
        audioSrc.volume = s.Volume;

        //Play the sound
        audioSrc.Play();

        //Destroy the object
        Destroy(soundObj, s.Clip.length);
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