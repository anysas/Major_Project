using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Music")]
    [SerializeField, Tooltip("Loops in the background.")] AudioClip music;
    [SerializeField, Range(0f, 1f)] float musicVolume = 0.4f;

    [Header("Clips")]
    [SerializeField, Tooltip("Plays when a flock of birds flies in.")] AudioClip birdsFlyIn;
    [SerializeField, Tooltip("Plays when the truck arm starts going up.")] AudioClip armUp;
    [SerializeField, Tooltip("Loops while an explosion warning circle is flashing.")] AudioClip explosionWarning;
    [SerializeField, Tooltip("Plays when an explosion detonates.")] AudioClip explosion;
    [SerializeField, Tooltip("Loops while the truck is moving.")] AudioClip motor;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] float birdsVolume = 1f;
    [SerializeField, Range(0f, 1f)] float armVolume = 1f;
    [SerializeField, Range(0f, 1f)] float explosionWarningVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] float explosionVolume = 1f;
    [SerializeField, Range(0f, 1f)] float motorVolume = 0.55f;
    [SerializeField, Tooltip("Motor pitch at a crawl.")] float motorMinPitch = 0.85f;
    [SerializeField, Tooltip("Motor pitch at full speed.")] float motorMaxPitch = 1.2f;

    AudioSource oneShots;
    AudioSource warningSource;
    AudioSource motorSource;
    AudioSource musicSource;

    void Awake()
    {
        Instance = this;
        oneShots = CreateSource("OneShots", false);
        warningSource = CreateSource("ExplosionWarning", true);
        motorSource = CreateSource("Motor", true);
        musicSource = CreateSource("Music", true);
    }

    void Start()
    {
        StartMusic();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (!ExperienceRestart.IsActive)
        {
            SetExplosionWarning(false);
            UpdateMotor(0f);
        }
    }

    public static void PlayBirdsFlyIn()
    {
        if (Instance != null)
        {
            Instance.PlayOneShot(Instance.birdsFlyIn, Instance.birdsVolume);
        }
    }

    public static void PlayArmUp()
    {
        if (Instance != null)
        {
            Instance.PlayOneShot(Instance.armUp, Instance.armVolume);
        }
    }

    public static void PlayExplosion()
    {
        if (Instance != null)
        {
            Instance.PlayOneShot(Instance.explosion, Instance.explosionVolume);
        }
    }

    public static void SetExplosionWarning(bool active)
    {
        if (Instance != null)
        {
            Instance.SetWarning(active);
        }
    }

    public static void UpdateMotor(float speed)
    {
        if (Instance != null)
        {
            Instance.SetMotor(speed);
        }
    }

    void StartMusic()
    {
        if (musicSource == null || music == null)
        {
            return;
        }

        musicSource.clip = music;
        musicSource.volume = musicVolume;
        if (!musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip == null || oneShots == null)
        {
            return;
        }

        oneShots.PlayOneShot(clip, volume);
    }

    void SetWarning(bool active)
    {
        if (warningSource == null || explosionWarning == null)
        {
            return;
        }

        if (active)
        {
            if (warningSource.clip != explosionWarning)
            {
                warningSource.clip = explosionWarning;
            }

            warningSource.volume = explosionWarningVolume;
            if (!warningSource.isPlaying)
            {
                warningSource.Play();
            }
        }
        else if (warningSource.isPlaying)
        {
            warningSource.Stop();
        }
    }

    void SetMotor(float speed)
    {
        if (motorSource == null || motor == null)
        {
            return;
        }

        float moving = Mathf.Max(0f, speed);
        if (moving < 0.35f || !ExperienceRestart.IsActive)
        {
            if (motorSource.isPlaying)
            {
                motorSource.Stop();
            }

            return;
        }

        if (motorSource.clip != motor)
        {
            motorSource.clip = motor;
        }

        float t = Mathf.Clamp01(moving / 12f);
        motorSource.volume = Mathf.Lerp(motorVolume * 0.35f, motorVolume, t);
        motorSource.pitch = Mathf.Lerp(motorMinPitch, motorMaxPitch, t);
        if (!motorSource.isPlaying)
        {
            motorSource.Play();
        }
    }

    AudioSource CreateSource(string childName, bool loop)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }
}
