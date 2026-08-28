using UnityEngine;
using UnityEngine.InputSystem;

public class BirdEvent : MonoBehaviour
{
    enum BirdPhase
    {
        Waiting,
        Swooping,
        Circling,
        Fleeing
    }

    [Header("Setup")]
    [SerializeField] GameObject birdPrefab;
    [SerializeField] CarController truck;
    [SerializeField, Tooltip("Drag a short horn clip here.")] AudioClip hornClip;

    [Header("Timing")]
    [SerializeField, Tooltip("Shortest wait before a bird appears.")] float spawnIntervalMin = 8f;
    [SerializeField, Tooltip("Longest wait before a bird appears.")] float spawnIntervalMax = 16f;
    [SerializeField, Tooltip("How long the bird can circle before the truck is stunned.")] float circleBeforeStun = 3f;
    [SerializeField, Tooltip("How long the truck stays stunned if the bird is not horned away.")] float stunDuration = 1f;

    [Header("Flight")]
    [SerializeField] float spawnDistanceMin = 28f;
    [SerializeField] float spawnDistanceMax = 40f;
    [SerializeField] float spawnHeightMin = 12f;
    [SerializeField] float spawnHeightMax = 18f;
    [SerializeField] float swoopSpeed = 22f;
    [SerializeField] float circleHeight = 3.2f;
    [SerializeField] float circleRadius = 2.4f;
    [SerializeField] float circleSpeed = 220f;
    [SerializeField] float fleeSpeed = 32f;
    [SerializeField, Tooltip("Extra pitch for the bird model. The capsule prefab needs 90; a +Z facing model should use 0.")] float modelPitch = 90f;

    AudioSource hornSource;
    Transform bird;
    BirdPhase phase = BirdPhase.Waiting;
    float waitLeft;
    float circleLeft;
    float orbitAngle;
    Vector3 fleeDir;
    float fleeLeft;

    void Awake()
    {
        hornSource = GetComponent<AudioSource>();
        if (hornSource == null)
        {
            hornSource = gameObject.AddComponent<AudioSource>();
        }

        hornSource.playOnAwake = false;
        hornSource.spatialBlend = 0f;
        hornSource.loop = false;
    }

    void Start()
    {
        if (truck == null)
        {
            truck = FindFirstObjectByType<CarController>();
        }

        if (birdPrefab == null)
        {
            birdPrefab = Resources.Load<GameObject>("Bird");
        }

        ScheduleNextBird();
    }

    void OnValidate()
    {
        spawnIntervalMin = Mathf.Max(0.5f, spawnIntervalMin);
        spawnIntervalMax = Mathf.Max(spawnIntervalMin, spawnIntervalMax);
        circleBeforeStun = Mathf.Max(0.25f, circleBeforeStun);
        stunDuration = Mathf.Max(0.05f, stunDuration);
        spawnDistanceMin = Mathf.Max(4f, spawnDistanceMin);
        spawnDistanceMax = Mathf.Max(spawnDistanceMin, spawnDistanceMax);
        spawnHeightMin = Mathf.Max(1f, spawnHeightMin);
        spawnHeightMax = Mathf.Max(spawnHeightMin, spawnHeightMax);
        swoopSpeed = Mathf.Max(1f, swoopSpeed);
        circleHeight = Mathf.Max(0.5f, circleHeight);
        circleRadius = Mathf.Max(0.4f, circleRadius);
        circleSpeed = Mathf.Max(10f, circleSpeed);
        fleeSpeed = Mathf.Max(1f, fleeSpeed);
    }

    void Update()
    {
        if (HornPressed())
        {
            Honk();
        }

        if (ExperienceRestart.IsEnded)
        {
            ClearBird();
            return;
        }

        if (phase == BirdPhase.Waiting)
        {
            waitLeft -= Time.deltaTime;
            if (waitLeft <= 0f)
            {
                SpawnBird();
            }
        }
    }

    void LateUpdate()
    {
        if (bird == null || ExperienceRestart.IsEnded)
        {
            return;
        }

        switch (phase)
        {
            case BirdPhase.Swooping:
                StepSwoop(Time.deltaTime);
                break;
            case BirdPhase.Circling:
                StepCircle(Time.deltaTime);
                break;
            case BirdPhase.Fleeing:
                StepFlee(Time.deltaTime);
                break;
        }
    }

    static bool HornPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame;
    }

    void Honk()
    {
        if (hornClip != null && hornSource != null)
        {
            hornSource.PlayOneShot(hornClip);
        }

        if (phase == BirdPhase.Swooping || phase == BirdPhase.Circling)
        {
            BeginFlee();
        }
    }

    void ScheduleNextBird()
    {
        phase = BirdPhase.Waiting;
        waitLeft = Random.Range(spawnIntervalMin, spawnIntervalMax);
    }

    void SpawnBird()
    {
        if (birdPrefab == null || truck == null)
        {
            ScheduleNextBird();
            return;
        }

        Vector3 truckPos = truck.transform.position;
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float dist = Random.Range(spawnDistanceMin, spawnDistanceMax);
        Vector3 spawn = truckPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        spawn.y = truckPos.y + Random.Range(spawnHeightMin, spawnHeightMax);

        GameObject spawned = Instantiate(birdPrefab, spawn, Quaternion.identity);
        spawned.name = "Bird";
        spawned.SetActive(true);
        spawned.transform.SetParent(transform, true);
        DisableColliders(spawned);

        bird = spawned.transform;
        orbitAngle = Mathf.Atan2(spawn.z - truckPos.z, spawn.x - truckPos.x);
        phase = BirdPhase.Swooping;
        circleLeft = circleBeforeStun;
        FaceFlight(OrbitPoint() - spawn);
    }

    void StepSwoop(float dt)
    {
        Vector3 target = OrbitPoint();
        Vector3 next = Vector3.MoveTowards(bird.position, target, swoopSpeed * dt);
        Vector3 delta = next - bird.position;
        bird.position = next;
        if (delta.sqrMagnitude > 0.0001f)
        {
            FaceFlight(delta);
        }

        if ((next - target).sqrMagnitude <= 0.25f)
        {
            bird.position = target;
            phase = BirdPhase.Circling;
            circleLeft = circleBeforeStun;
        }
    }

    void StepCircle(float dt)
    {
        circleLeft -= dt;
        orbitAngle += circleSpeed * Mathf.Deg2Rad * dt;
        Vector3 next = OrbitPoint();
        next.y += Mathf.Sin(Time.time * 6f) * 0.12f;
        Vector3 delta = next - bird.position;
        bird.position = next;
        Vector3 tangent = new Vector3(-Mathf.Sin(orbitAngle), 0f, Mathf.Cos(orbitAngle));
        FaceFlight(delta.sqrMagnitude > 0.0001f ? Vector3.Lerp(tangent, delta, 0.35f) : tangent);

        if (circleLeft <= 0f)
        {
            if (truck != null)
            {
                truck.Stun(stunDuration);
            }

            BeginFlee();
        }
    }

    void StepFlee(float dt)
    {
        fleeLeft -= dt;
        bird.position += fleeDir * (fleeSpeed * dt);
        FaceFlight(fleeDir);
        if (fleeLeft <= 0f)
        {
            ClearBird();
            ScheduleNextBird();
        }
    }

    void BeginFlee()
    {
        if (bird == null)
        {
            ScheduleNextBird();
            return;
        }

        Vector3 away = bird.position - TruckTop();
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            away = bird.forward;
            away.y = 0f;
        }

        fleeDir = (away.normalized + Vector3.up * 0.45f).normalized;
        fleeLeft = 3.5f;
        phase = BirdPhase.Fleeing;
    }

    void ClearBird()
    {
        if (bird != null)
        {
            Destroy(bird.gameObject);
            bird = null;
        }

        phase = BirdPhase.Waiting;
    }

    Vector3 TruckTop()
    {
        if (truck == null)
        {
            return transform.position;
        }

        Vector3 pos = truck.transform.position;
        pos.y += circleHeight;
        return pos;
    }

    Vector3 OrbitPoint()
    {
        Vector3 center = TruckTop();
        return center + new Vector3(Mathf.Cos(orbitAngle), 0f, Mathf.Sin(orbitAngle)) * circleRadius;
    }

    void FaceFlight(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        bird.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(modelPitch, 0f, 0f);
    }

    static void DisableColliders(GameObject spawned)
    {
        Collider[] colliders = spawned.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }
}
