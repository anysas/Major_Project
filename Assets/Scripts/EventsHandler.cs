using UnityEngine;

/// <summary>
/// Parent for gameplay events. Ramps spawn pressure over the target session length.
/// </summary>
public class EventsHandler : MonoBehaviour
{
    public static EventsHandler Instance { get; private set; }

    [Header("Session")]
    [SerializeField, Tooltip("Ideal run length used to ramp difficulty.")] float targetDurationSeconds = 120f;
    [SerializeField, Tooltip("Seconds at the start that stay at easy intensity.")] float openingGraceSeconds = 4f;
    [SerializeField, Tooltip("0 = start of ramp, 1 = end. Higher early values = harder sooner.")] AnimationCurve intensityCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 3f),
        new Keyframe(0.15f, 0.4f),
        new Keyframe(0.35f, 0.7f),
        new Keyframe(0.6f, 0.9f),
        new Keyframe(1f, 1f, 0.2f, 0f));
    [SerializeField, Tooltip("Below 1 pulls mid-session pressure up so the ramp is felt sooner."), Range(0.4f, 1f)]
    float pressureExponent = 0.65f;

    [Header("Birds (easy → hard)")]
    [SerializeField] Vector2 birdIntervalEasy = new Vector2(6f, 9f);
    [SerializeField] Vector2 birdIntervalHard = new Vector2(0.6f, 1.6f);
    [SerializeField] float birdCircleEasy = 3f;
    [SerializeField] float birdCircleHard = 1f;
    [SerializeField] float birdSwoopSpeedEasy = 22f;
    [SerializeField] float birdSwoopSpeedHard = 38f;
    [SerializeField] float birdFleeSecondsEasy = 3.2f;
    [SerializeField] float birdFleeSecondsHard = 1.4f;
    [SerializeField, Tooltip("First bird is delayed this long so the player can settle in.")] float birdFirstDelay = 4f;

    [Header("Explosions (easy → hard)")]
    [SerializeField] Vector2 explosionIntervalEasy = new Vector2(7f, 11f);
    [SerializeField] Vector2 explosionIntervalHard = new Vector2(0.8f, 2f);
    [SerializeField] float explosionWarningEasy = 2.8f;
    [SerializeField] float explosionWarningHard = 1f;
    [SerializeField, Tooltip("First explosion is delayed this long so it does not overlap the first bird.")] float explosionFirstDelay = 6f;

    [Header("Shared")]
    [SerializeField, Tooltip("How much faster idle event timers drain at full pressure (1 = normal, 2.5 = much snappier).")] float waitDrainHard = 2.4f;

    float elapsed;
    bool birdPrimed;
    bool explosionPrimed;

    public float ElapsedSeconds => elapsed;
    public float SessionProgress => targetDurationSeconds > 0.01f ? Mathf.Clamp01(elapsed / targetDurationSeconds) : 1f;
    public float Intensity { get; private set; }

    /// <summary>Intensity remapped so mid-session already feels busy.</summary>
    public float Pressure { get; private set; }

    void Awake()
    {
        Instance = this;
        elapsed = 0f;
        Intensity = 0f;
        Pressure = 0f;
        birdPrimed = false;
        explosionPrimed = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void OnValidate()
    {
        targetDurationSeconds = Mathf.Max(30f, targetDurationSeconds);
        openingGraceSeconds = Mathf.Clamp(openingGraceSeconds, 0f, targetDurationSeconds * 0.5f);
        pressureExponent = Mathf.Clamp(pressureExponent, 0.4f, 1f);
        birdIntervalEasy = SanitizeInterval(birdIntervalEasy, 0.35f);
        birdIntervalHard = SanitizeInterval(birdIntervalHard, 0.25f);
        explosionIntervalEasy = SanitizeInterval(explosionIntervalEasy, 0.35f);
        explosionIntervalHard = SanitizeInterval(explosionIntervalHard, 0.25f);
        birdCircleEasy = Mathf.Max(0.5f, birdCircleEasy);
        birdCircleHard = Mathf.Max(0.35f, birdCircleHard);
        birdSwoopSpeedEasy = Mathf.Max(1f, birdSwoopSpeedEasy);
        birdSwoopSpeedHard = Mathf.Max(birdSwoopSpeedEasy, birdSwoopSpeedHard);
        birdFleeSecondsEasy = Mathf.Max(0.4f, birdFleeSecondsEasy);
        birdFleeSecondsHard = Mathf.Clamp(birdFleeSecondsHard, 0.35f, birdFleeSecondsEasy);
        explosionWarningEasy = Mathf.Max(0.5f, explosionWarningEasy);
        explosionWarningHard = Mathf.Max(0.35f, explosionWarningHard);
        birdFirstDelay = Mathf.Max(0f, birdFirstDelay);
        explosionFirstDelay = Mathf.Max(0f, explosionFirstDelay);
        waitDrainHard = Mathf.Max(1f, waitDrainHard);
        if (intensityCurve == null || intensityCurve.length == 0)
        {
            intensityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }

    void Update()
    {
        if (ExperienceRestart.IsEnded)
        {
            return;
        }

        elapsed += Time.deltaTime;
        Intensity = EvaluateIntensity(elapsed);
        Pressure = Mathf.Pow(Intensity, pressureExponent);
    }

    float EvaluateIntensity(float time)
    {
        float rampLength = Mathf.Max(1f, targetDurationSeconds - openingGraceSeconds);
        float t = Mathf.Clamp01((time - openingGraceSeconds) / rampLength);
        if (intensityCurve != null && intensityCurve.length > 0)
        {
            return Mathf.Clamp01(intensityCurve.Evaluate(t));
        }

        return t;
    }

    public float EventWaitDrainRate()
    {
        return Mathf.Lerp(1f, waitDrainHard, Pressure);
    }

    public float NextBirdWait()
    {
        if (!birdPrimed)
        {
            birdPrimed = true;
            return birdFirstDelay;
        }

        return RandomRange(birdIntervalEasy, birdIntervalHard, Pressure);
    }

    public float BirdCircleSeconds()
    {
        return Mathf.Lerp(birdCircleEasy, birdCircleHard, Pressure);
    }

    public float BirdSwoopSpeed()
    {
        return Mathf.Lerp(birdSwoopSpeedEasy, birdSwoopSpeedHard, Pressure);
    }

    public float BirdFleeSeconds()
    {
        return Mathf.Lerp(birdFleeSecondsEasy, birdFleeSecondsHard, Pressure);
    }

    public float NextExplosionWait()
    {
        if (!explosionPrimed)
        {
            explosionPrimed = true;
            return explosionFirstDelay;
        }

        return RandomRange(explosionIntervalEasy, explosionIntervalHard, Pressure);
    }

    public float ExplosionWarningSeconds()
    {
        return Mathf.Lerp(explosionWarningEasy, explosionWarningHard, Pressure);
    }

    static float RandomRange(Vector2 easy, Vector2 hard, float intensity)
    {
        float min = Mathf.Lerp(easy.x, hard.x, intensity);
        float max = Mathf.Lerp(easy.y, hard.y, intensity);
        if (max < min)
        {
            max = min;
        }

        return Random.Range(min, max);
    }

    static Vector2 SanitizeInterval(Vector2 interval, float minValue)
    {
        float min = Mathf.Max(minValue, interval.x);
        float max = Mathf.Max(min, interval.y);
        return new Vector2(min, max);
    }
}
