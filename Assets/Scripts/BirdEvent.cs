using UnityEngine;

public class BirdEvent : MonoBehaviour
{
    const int FlockCount = 3;

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
    [SerializeField] float circleHeight = 6.5f;
    [SerializeField] float circleRadius = 3.5f;
    [SerializeField] float circleSpeed = 220f;
    [SerializeField] float fleeSpeed = 32f;

    AudioSource hornSource;
    Transform[] birds;
    Vector3[] fleeDirs;
    float[] orbitOffsets;
    Quaternion birdModelOffset = Quaternion.identity;
    BirdPhase phase = BirdPhase.Waiting;
    float waitLeft;
    float circleLeft;
    float orbitAngle;
    float fleeLeft;
    EventsHandler eventsHandler;

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
        eventsHandler = GetComponentInParent<EventsHandler>();
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }
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

        CacheBirdModelOffset();
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
        CacheBirdModelOffset();
    }

    void CacheBirdModelOffset()
    {
        birdModelOffset = birdPrefab != null ? birdPrefab.transform.rotation : Quaternion.identity;
    }

    void Update()
    {
        if (!ExperienceRestart.HasStarted)
        {
            return;
        }

        if (HornPressed())
        {
            Honk();
        }

        if (ExperienceRestart.IsEnded)
        {
            ClearBirds();
            return;
        }

        if (phase == BirdPhase.Waiting)
        {
            float drain = eventsHandler != null ? eventsHandler.EventWaitDrainRate() : 1f;
            waitLeft -= Time.deltaTime * drain;
            if (waitLeft <= 0f)
            {
                SpawnFlock();
            }
        }
    }

    void LateUpdate()
    {
        if (!HasBirds() || !ExperienceRestart.IsActive)
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
        return GameInput.HornPressedThisFrame;
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
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        waitLeft = eventsHandler != null
            ? eventsHandler.NextBirdWait()
            : Random.Range(spawnIntervalMin, spawnIntervalMax);
    }

    void SpawnFlock()
    {
        if (birdPrefab == null || truck == null)
        {
            ScheduleNextBird();
            return;
        }

        ClearBirds();
        CacheBirdModelOffset();

        birds = new Transform[FlockCount];
        fleeDirs = new Vector3[FlockCount];
        orbitOffsets = new float[FlockCount];

        Vector3 truckPos = truck.transform.position;
        float baseAngle = Random.Range(0f, Mathf.PI * 2f);
        orbitAngle = baseAngle;

        for (int i = 0; i < FlockCount; i++)
        {
            orbitOffsets[i] = i * (Mathf.PI * 2f / FlockCount);
            float spawnAngle = baseAngle + orbitOffsets[i] + Random.Range(-0.25f, 0.25f);
            float dist = Random.Range(spawnDistanceMin, spawnDistanceMax);
            Vector3 spawn = truckPos + new Vector3(Mathf.Cos(spawnAngle), 0f, Mathf.Sin(spawnAngle)) * dist;
            spawn.y = truckPos.y + Random.Range(spawnHeightMin, spawnHeightMax);

            GameObject spawned = Instantiate(birdPrefab, spawn, Quaternion.identity);
            spawned.name = "Bird " + (i + 1);
            spawned.SetActive(true);
            spawned.transform.SetParent(transform, true);
            DisableColliders(spawned);

            birds[i] = spawned.transform;
            FaceFlight(birds[i], OrbitPoint(i) - spawn);
        }

        phase = BirdPhase.Swooping;
        circleLeft = CurrentCircleSeconds();
        SoundManager.PlayBirdsFlyIn();
    }

    float CurrentCircleSeconds()
    {
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        return eventsHandler != null ? eventsHandler.BirdCircleSeconds() : circleBeforeStun;
    }

    float CurrentSwoopSpeed()
    {
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        return eventsHandler != null ? eventsHandler.BirdSwoopSpeed() : swoopSpeed;
    }

    float CurrentFleeSeconds()
    {
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        return eventsHandler != null ? eventsHandler.BirdFleeSeconds() : 3.5f;
    }

    void StepSwoop(float dt)
    {
        bool allArrived = true;
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            Vector3 target = OrbitPoint(i);
            float speed = CurrentSwoopSpeed();
            Vector3 next = Vector3.MoveTowards(birds[i].position, target, speed * dt);
            Vector3 delta = next - birds[i].position;
            birds[i].position = next;
            if (delta.sqrMagnitude > 0.0001f)
            {
                FaceFlight(birds[i], delta);
            }

            if ((next - target).sqrMagnitude > 0.35f)
            {
                allArrived = false;
            }
            else
            {
                birds[i].position = target;
            }
        }

        if (allArrived)
        {
            phase = BirdPhase.Circling;
            circleLeft = CurrentCircleSeconds();
        }
    }

    void StepCircle(float dt)
    {
        circleLeft -= dt;
        orbitAngle += circleSpeed * Mathf.Deg2Rad * dt;

        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            Vector3 next = OrbitPoint(i);
            next.y += Mathf.Sin(Time.time * 6f + orbitOffsets[i]) * 0.12f;
            Vector3 delta = next - birds[i].position;
            birds[i].position = next;

            float angle = orbitAngle + orbitOffsets[i];
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            FaceFlight(birds[i], delta.sqrMagnitude > 0.0001f ? Vector3.Lerp(tangent, delta, 0.35f) : tangent);
        }

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
        bool anyLeft = false;
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            anyLeft = true;
            birds[i].position += fleeDirs[i] * (fleeSpeed * dt);
            FaceFlight(birds[i], fleeDirs[i]);
        }

        if (fleeLeft <= 0f || !anyLeft)
        {
            ClearBirds();
            ScheduleNextBird();
        }
    }

    void BeginFlee()
    {
        if (!HasBirds())
        {
            ScheduleNextBird();
            return;
        }

        Vector3 center = TruckTop();
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                fleeDirs[i] = Vector3.up;
                continue;
            }

            Vector3 away = birds[i].position - center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                float angle = orbitAngle + orbitOffsets[i];
                away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            // Spread each bird onto its own outbound path.
            float yaw = i * (360f / FlockCount) + Random.Range(-18f, 18f);
            Vector3 spread = Quaternion.Euler(0f, yaw, 0f) * away.normalized;
            fleeDirs[i] = (spread + Vector3.up * Random.Range(0.35f, 0.7f)).normalized;
        }

        fleeLeft = CurrentFleeSeconds();
        phase = BirdPhase.Fleeing;
    }

    void ClearBirds()
    {
        if (birds != null)
        {
            for (int i = 0; i < birds.Length; i++)
            {
                if (birds[i] != null)
                {
                    Destroy(birds[i].gameObject);
                    birds[i] = null;
                }
            }
        }

        birds = null;
        fleeDirs = null;
        orbitOffsets = null;
        phase = BirdPhase.Waiting;
    }

    bool HasBirds()
    {
        if (birds == null)
        {
            return false;
        }

        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    Vector3 TruckTop()
    {
        if (truck == null)
        {
            return transform.position + Vector3.up * circleHeight;
        }

        Vector3 pos = truck.transform.position;
        pos.y += circleHeight;
        return pos;
    }

    Vector3 OrbitPoint(int index)
    {
        float angle = orbitAngle + (orbitOffsets != null ? orbitOffsets[index] : 0f);
        Vector3 center = TruckTop();
        return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * circleRadius;
    }

    void FaceFlight(Transform bird, Vector3 dir)
    {
        dir.y = 0f;
        if (bird == null || dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Prefab face is opposite Unity's forward, so look along -dir.
        bird.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up) * birdModelOffset;
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
