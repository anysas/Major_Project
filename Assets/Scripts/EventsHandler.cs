using UnityEngine;

/// <summary>
/// Parent for gameplay events and wall pressure. One session clock drives birds,
/// explosions, creep speed, post-push stall, and how far a compact recedes the wall.
/// Early: slow walls, long rest after a shove, big pushback, sparse events.
/// Late: fast walls, almost no rest, small pushback, overlapping hazards.
/// </summary>
[DefaultExecutionOrder(-20)]
public class EventsHandler : MonoBehaviour
{
    public static EventsHandler Instance { get; private set; }

    [Header("Session")]
    [SerializeField, Tooltip("Ideal run length used to ramp difficulty.")] float targetDurationSeconds = 120f;
    [SerializeField, Tooltip("Seconds at the start that stay at easy intensity.")] float openingGraceSeconds = 8f;
    [SerializeField, Tooltip("0 = start of ramp, 1 = end. Higher early values = harder sooner.")] AnimationCurve intensityCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.7f),
        new Keyframe(0.25f, 0.2f),
        new Keyframe(0.5f, 0.5f),
        new Keyframe(0.75f, 0.8f),
        new Keyframe(1f, 1f, 0.4f, 0f));
    [SerializeField, Tooltip("Below 1 pulls mid-session pressure up so the ramp is felt sooner."), Range(0.4f, 1f)]
    float pressureExponent = 0.85f;

    [Header("Birds (easy → hard)")]
    [SerializeField] Vector2 birdIntervalEasy = new Vector2(10f, 14f);
    [SerializeField] Vector2 birdIntervalHard = new Vector2(3f, 5f);
    [SerializeField] float birdCircleEasy = 3.2f;
    [SerializeField] float birdCircleHard = 1.25f;
    [SerializeField] float birdSwoopSpeedEasy = 22f;
    [SerializeField] float birdSwoopSpeedHard = 32f;
    [SerializeField] float birdFleeSecondsEasy = 3.2f;
    [SerializeField] float birdFleeSecondsHard = 1.6f;
    [SerializeField] float birdStunEasy = 0.7f;
    [SerializeField] float birdStunHard = 1.45f;
    [SerializeField, Tooltip("First bird is delayed this long so the player can settle in.")] float birdFirstDelay = 8f;

    [Header("Explosions (easy → hard)")]
    [SerializeField] Vector2 explosionIntervalEasy = new Vector2(12f, 16f);
    [SerializeField] Vector2 explosionIntervalHard = new Vector2(4.5f, 7f);
    [SerializeField] float explosionWarningEasy = 3f;
    [SerializeField] float explosionWarningHard = 1.4f;
    [SerializeField] float explosionStunEasy = 0.7f;
    [SerializeField] float explosionStunHard = 1.5f;
    [SerializeField] float explosionRadiusEasy = 2.9f;
    [SerializeField] float explosionRadiusHard = 3.7f;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly warnings hug the truck. 0 = anywhere in the arena.")]
    float explosionTruckBiasEasy = 0.05f;
    [SerializeField, Range(0f, 1f)] float explosionTruckBiasHard = 0.8f;
    [SerializeField, Range(0f, 1f), Tooltip("Dual explosions only after this pressure, and only if the arena is still roomy.")]
    float multiExplosionFromPressure = 0.42f;
    [SerializeField, Tooltip("First explosion is delayed this long so it does not overlap the first bird.")] float explosionFirstDelay = 13f;

    [Header("Walls (easy → hard)")]
    [SerializeField, Tooltip("How fast the pile edges crawl inward.")] float creepSpeedEasy = 0.7f;
    [SerializeField] float creepSpeedHard = 1.85f;
    [SerializeField, Tooltip("How long a wall waits after being shoved back before it creeps again.")] float stallSecondsEasy = 3.4f;
    [SerializeField] float stallSecondsHard = 0.65f;
    [SerializeField, Tooltip("How far a swallowed block recedes its wall. Bigger early, stingier late.")] float finishPushEasy = 5.6f;
    [SerializeField] float finishPushHard = 1.6f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Empty edges creep this fraction of the current wall speed.")]
    float emptyCreepScaleEasy = 0.5f;
    [SerializeField, Range(0.05f, 1f)] float emptyCreepScaleHard = 0.9f;

    [Header("Shared")]
    [SerializeField, Tooltip("How much faster idle event timers drain at full pressure (1 = normal).")] float waitDrainHard = 1.2f;

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
        birdStunEasy = Mathf.Max(0.05f, birdStunEasy);
        birdStunHard = Mathf.Max(birdStunEasy, birdStunHard);
        explosionWarningEasy = Mathf.Max(0.5f, explosionWarningEasy);
        explosionWarningHard = Mathf.Max(0.35f, explosionWarningHard);
        explosionStunEasy = Mathf.Max(0.05f, explosionStunEasy);
        explosionStunHard = Mathf.Max(explosionStunEasy, explosionStunHard);
        explosionRadiusEasy = Mathf.Max(0.5f, explosionRadiusEasy);
        explosionRadiusHard = Mathf.Max(explosionRadiusEasy, explosionRadiusHard);
        explosionTruckBiasEasy = Mathf.Clamp01(explosionTruckBiasEasy);
        explosionTruckBiasHard = Mathf.Clamp01(explosionTruckBiasHard);
        multiExplosionFromPressure = Mathf.Clamp01(multiExplosionFromPressure);
        birdFirstDelay = Mathf.Max(0f, birdFirstDelay);
        explosionFirstDelay = Mathf.Max(0f, explosionFirstDelay);
        creepSpeedEasy = Mathf.Max(0.05f, creepSpeedEasy);
        creepSpeedHard = Mathf.Max(creepSpeedEasy, creepSpeedHard);
        stallSecondsEasy = Mathf.Max(0.05f, stallSecondsEasy);
        stallSecondsHard = Mathf.Clamp(stallSecondsHard, 0.05f, stallSecondsEasy);
        finishPushEasy = Mathf.Max(0.25f, finishPushEasy);
        finishPushHard = Mathf.Clamp(finishPushHard, 0.2f, finishPushEasy);
        emptyCreepScaleEasy = Mathf.Clamp(emptyCreepScaleEasy, 0.05f, 1f);
        emptyCreepScaleHard = Mathf.Clamp(emptyCreepScaleHard, emptyCreepScaleEasy, 1f);
        waitDrainHard = Mathf.Max(1f, waitDrainHard);
        if (intensityCurve == null || intensityCurve.length == 0)
        {
            intensityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }

    void Update()
    {
        if (!ExperienceRestart.IsActive)
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

    public float BirdStunSeconds()
    {
        return Mathf.Lerp(birdStunEasy, birdStunHard, Pressure);
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

    public float ExplosionStunSeconds()
    {
        return Mathf.Lerp(explosionStunEasy, explosionStunHard, Pressure);
    }

    public float ExplosionBlastRadius()
    {
        return Mathf.Lerp(explosionRadiusEasy, explosionRadiusHard, Pressure);
    }

    public float ExplosionTruckBias()
    {
        return Mathf.Lerp(explosionTruckBiasEasy, explosionTruckBiasHard, Pressure);
    }

    public bool AllowMultiExplosion()
    {
        return Pressure >= multiExplosionFromPressure;
    }

    public float WallCreepSpeed()
    {
        return Mathf.Lerp(creepSpeedEasy, creepSpeedHard, Pressure);
    }

    public float WallStallSeconds()
    {
        return Mathf.Lerp(stallSecondsEasy, stallSecondsHard, Pressure);
    }

    public float WallFinishPush()
    {
        return Mathf.Lerp(finishPushEasy, finishPushHard, Pressure);
    }

    public float WallEmptyCreepScale()
    {
        return Mathf.Lerp(emptyCreepScaleEasy, emptyCreepScaleHard, Pressure);
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
