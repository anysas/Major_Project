using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    [SerializeField, Range(0f, 1f), Tooltip("Horn playback volume.")] float hornVolume = 1f;
    [SerializeField, Min(0.1f), Tooltip("How large spawned birds are compared to the prefab.")] float birdScale = 1.35f;
    [SerializeField, Tooltip("Wing flap clip on the seagull. Uses flap_Armature from the model if left empty.")] AnimationClip flapClip;
    [SerializeField, Min(0.1f), Tooltip("How fast the wing flap plays. 1 is the clip's normal speed.")] float flapSpeed = 1.6f;

    [Header("Timing")]
    [SerializeField, Tooltip("Shortest wait before a bird appears.")] float spawnIntervalMin = 8f;
    [SerializeField, Tooltip("Longest wait before a bird appears.")] float spawnIntervalMax = 16f;
    [SerializeField, Tooltip("How long the bird can circle before the truck is stunned.")] float circleBeforeStun = 3f;
    [SerializeField, Tooltip("How long the truck stays stunned if the bird is not horned away.")] float stunDuration = 1f;

    [Header("Flight")]
    [SerializeField] float spawnDistanceMin = 56f;
    [SerializeField] float spawnDistanceMax = 84f;
    [SerializeField] float spawnHeightMin = 12f;
    [SerializeField] float spawnHeightMax = 18f;
    [SerializeField] float swoopSpeed = 28f;
    [SerializeField] float circleHeight = 6.5f;
    [SerializeField] float circleRadius = 3.5f;
    [SerializeField] float circleSpeed = 230f;
    [SerializeField] float fleeSpeed = 36f;

    struct FlapPlayback
    {
        public PlayableGraph graph;
        public AnimationClipPlayable playable;
        public float length;
    }

    AudioSource hornSource;
    Transform[] birds;
    FlapPlayback[] flaps;
    bool flapMissingLogged;
    Vector3[] fleeDirs;
    Vector3[] fleeFromDirs;
    bool[] joinedCircle;
    float[] orbitOffsets;
    Quaternion birdModelOffset = Quaternion.identity;
    BirdPhase phase = BirdPhase.Waiting;
    float waitLeft;
    float circleLeft;
    float circleBlend;
    float orbitAngle;
    float fleeLeft;
    float fleeTurn;
    Vector3 lastTruckPosition;
    Vector3 truckStepDelta;
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
        hornSource.volume = hornVolume;
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
        birdScale = Mathf.Max(0.1f, birdScale);
        swoopSpeed = Mathf.Max(1f, swoopSpeed);
        circleHeight = Mathf.Max(0.5f, circleHeight);
        circleRadius = Mathf.Max(0.4f, circleRadius);
        circleSpeed = Mathf.Max(10f, circleSpeed);
        fleeSpeed = Mathf.Max(1f, fleeSpeed);
        hornVolume = Mathf.Clamp01(hornVolume);
        CacheBirdModelOffset();
    }

    void CacheBirdModelOffset()
    {
        birdModelOffset = birdPrefab != null ? birdPrefab.transform.rotation : Quaternion.identity;
    }

    void Update()
    {
        if (ExperienceRestart.IsEnded)
        {
            ClearBirds();
            return;
        }

        if (!ExperienceRestart.IsActive || Time.frameCount == ExperienceRestart.StartedFrame)
        {
            return;
        }

        if (HornPressed())
        {
            Honk();
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

        truckStepDelta = ConsumeTruckMove();

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

        StepFlaps();
    }

    static bool HornPressed()
    {
        return GameInput.HornPressedThisFrame;
    }

    void Honk()
    {
        if (hornClip != null && hornSource != null)
        {
            hornSource.PlayOneShot(hornClip, hornVolume);
        }

        if (phase == BirdPhase.Circling)
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
        flaps = new FlapPlayback[FlockCount];
        fleeDirs = new Vector3[FlockCount];
        fleeFromDirs = new Vector3[FlockCount];
        joinedCircle = new bool[FlockCount];
        orbitOffsets = new float[FlockCount];

        Vector3 truckPos = truck.transform.position;
        float baseAngle = Random.Range(0f, Mathf.PI * 2f);
        orbitAngle = baseAngle;
        circleBlend = 0f;
        lastTruckPosition = truckPos;

        for (int i = 0; i < FlockCount; i++)
        {
            orbitOffsets[i] = i * (Mathf.PI * 2f / FlockCount);
            float spawnAngle = baseAngle + orbitOffsets[i] + Random.Range(-0.25f, 0.25f);
            Vector3 spawn = SpawnOutsideView(truckPos, spawnAngle, i);

            GameObject spawned = Instantiate(birdPrefab, spawn, Quaternion.identity);
            spawned.name = "Bird " + (i + 1);
            spawned.SetActive(true);
            spawned.transform.localScale = birdPrefab.transform.localScale * birdScale;
            spawned.transform.SetParent(transform, true);
            DisableColliders(spawned);
            flaps[i] = StartFlap(spawned, i);

            birds[i] = spawned.transform;
            Vector3 launch = OrbitPoint(i) - spawn;
            if (launch.sqrMagnitude > 0.01f)
            {
                FaceFlight(birds[i], launch);
            }
        }

        phase = BirdPhase.Swooping;
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

    float CurrentStunSeconds()
    {
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        return eventsHandler != null ? eventsHandler.BirdStunSeconds() : stunDuration;
    }

    void StepSwoop(float dt)
    {
        if (AnyJoined())
        {
            AdvanceOrbit(dt, 0.32f);
        }

        bool allArrived = true;
        bool anyBird = false;
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            anyBird = true;
            if (joinedCircle[i])
            {
                FollowSlot(i, dt);
                continue;
            }

            if (FlyTowardOrbit(i, dt))
            {
                joinedCircle[i] = true;
            }
            else
            {
                allArrived = false;
            }
        }

        if (anyBird && allArrived)
        {
            phase = BirdPhase.Circling;
            circleLeft = CurrentCircleSeconds();
        }
    }

    void StepCircle(float dt)
    {
        circleLeft -= dt;
        AdvanceOrbit(dt, 1f);

        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            FollowSlot(i, dt);
        }

        if (circleLeft <= 0f)
        {
            if (truck != null)
            {
                truck.Stun(CurrentStunSeconds());
            }

            BeginFlee();
        }
    }

    void AdvanceOrbit(float dt, float target)
    {
        float step = dt / (target < 0.5f ? 0.35f : 0.55f);
        circleBlend = Mathf.MoveTowards(circleBlend, target, step);
        orbitAngle += circleSpeed * Mathf.Deg2Rad * SpinAmount() * dt;
    }

    float SpinAmount()
    {
        return circleBlend * circleBlend * (3f - 2f * circleBlend);
    }

    void FollowSlot(int index, float dt)
    {
        birds[index].position += truckStepDelta;
        Vector3 pos = birds[index].position;
        Vector3 slot = OrbitPoint(index);
        slot.y += Mathf.Sin(Time.time * 6f + orbitOffsets[index]) * 0.12f;
        float dist = Vector3.Distance(pos, slot);
        float followSpeed = Mathf.Lerp(CurrentSwoopSpeed() * 0.45f, TangentSpeed(), SpinAmount());
        birds[index].position = Vector3.MoveTowards(pos, slot, (followSpeed + dist * 3f) * dt);
        FaceFlight(birds[index], OrbitTangent(index));
    }

    bool AnyJoined()
    {
        if (joinedCircle == null)
        {
            return false;
        }

        for (int i = 0; i < joinedCircle.Length; i++)
        {
            if (joinedCircle[i])
            {
                return true;
            }
        }

        return false;
    }

    bool FlyTowardOrbit(int index, float dt)
    {
        if (birds == null || index < 0 || index >= birds.Length || birds[index] == null)
        {
            return false;
        }

        birds[index].position += truckStepDelta;

        Vector3 pos = birds[index].position;
        Vector3 slot = OrbitPoint(index);
        Vector3 toSlot = slot - pos;
        float dist = toSlot.magnitude;
        Vector3 tangent = OrbitTangent(index);
        Vector3 inbound = dist > 0.2f ? toSlot / dist : tangent;
        float joinRange = Mathf.Max(8f, circleRadius * 0.55f);
        float along = 1f - Mathf.Clamp01(dist / joinRange);
        float bank = along * along * (3f - 2f * along);

        float swoop = CurrentSwoopSpeed();
        float slotSpeed = TangentSpeed() * SpinAmount();
        float speed = Mathf.Lerp(swoop * 1.85f, Mathf.Max(swoop, slotSpeed + 12f), bank);
        float lead = Mathf.Lerp(Mathf.Min(4f, circleRadius * 0.25f), 0f, bank);
        Vector3 aim = slot + tangent * lead;
        birds[index].position = Vector3.MoveTowards(pos, aim, speed * dt);

        Vector3 face = dist < 1.2f ? tangent : Vector3.Slerp(inbound, tangent, bank);
        FaceFlight(birds[index], face);
        return Vector3.Distance(birds[index].position, slot) <= 1.8f;
    }

    Vector3 SpawnOutsideView(Vector3 truckPos, float angle, int index)
    {
        float height = Random.Range(spawnHeightMin, spawnHeightMax);
        float dist = Random.Range(spawnDistanceMin, spawnDistanceMax);
        Vector3 spawn = RingPoint(truckPos, angle, dist, height);
        Camera cam = Camera.main;
        if (cam == null || !IsOnScreen(cam, spawn))
        {
            return spawn;
        }

        Vector3 towardCamera = cam.transform.position - truckPos;
        towardCamera.y = 0f;
        float behindDeg = Mathf.Atan2(towardCamera.z, towardCamera.x) * Mathf.Rad2Deg;
        float angleDeg = angle * Mathf.Rad2Deg;
        float spread = (index - 1) * 22f;
        for (int n = 0; n < 12; n++)
        {
            angleDeg = Mathf.LerpAngle(angleDeg, behindDeg + spread, 0.45f);
            dist += 12f;
            spawn = RingPoint(truckPos, angleDeg * Mathf.Deg2Rad, dist, height);
            if (!IsOnScreen(cam, spawn))
            {
                return spawn;
            }
        }

        return spawn;
    }

    static Vector3 RingPoint(Vector3 truckPos, float angle, float dist, float height)
    {
        Vector3 spawn = truckPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
        spawn.y = truckPos.y + height;
        return spawn;
    }

    static bool IsOnScreen(Camera cam, Vector3 world)
    {
        Vector3 view = cam.WorldToViewportPoint(world);
        if (view.z <= 0.2f)
        {
            return false;
        }

        const float pad = 0.14f;
        return view.x > -pad && view.x < 1f + pad && view.y > -pad && view.y < 1f + pad;
    }

    float TangentSpeed()
    {
        return circleSpeed * Mathf.Deg2Rad * Mathf.Max(0.5f, circleRadius);
    }

    Vector3 OrbitTangent(int index)
    {
        float angle = orbitAngle + (orbitOffsets != null ? orbitOffsets[index] : 0f);
        return new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
    }

    void StepFlee(float dt)
    {
        fleeLeft -= dt;
        fleeTurn = Mathf.MoveTowards(fleeTurn, 1f, dt / 0.55f);
        float bank = fleeTurn * fleeTurn * (3f - 2f * fleeTurn);
        bool anyLeft = false;
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                continue;
            }

            anyLeft = true;
            Vector3 from = fleeFromDirs != null ? fleeFromDirs[i] : fleeDirs[i];
            Vector3 dir = Vector3.Slerp(from, fleeDirs[i], bank);
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = fleeDirs[i];
            }

            float speed = Mathf.Lerp(TangentSpeed(), fleeSpeed, bank);
            birds[i].position += dir.normalized * (speed * dt);
            FaceFlight(birds[i], dir);
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

        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null)
            {
                fleeDirs[i] = Vector3.up;
                continue;
            }

            Vector3 forward = OrbitTangent(i);
            fleeFromDirs[i] = forward;
            float yaw = (i - 1) * 85f + Random.Range(-8f, 8f);
            float climb = 0.15f + i * 0.4f;
            Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * forward;
            fleeDirs[i] = (heading + Vector3.up * climb).normalized;
        }

        fleeTurn = 0f;
        fleeLeft = CurrentFleeSeconds();
        phase = BirdPhase.Fleeing;
    }

    void OnDestroy()
    {
        StopFlaps();
    }

    FlapPlayback StartFlap(GameObject spawned, int index)
    {
        FlapPlayback playback = new FlapPlayback();
        AnimationClip clip = FindFlapClip();
        if (clip == null || spawned == null)
        {
            if (!flapMissingLogged)
            {
                flapMissingLogged = true;
                Debug.LogWarning("BirdEvent: seagull has no flap_Armature clip to play.");
            }

            return playback;
        }

        Animator animator = spawned.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = spawned.AddComponent<Animator>();
        }

        if (animator.avatar == null && birdPrefab != null)
        {
            Animator source = birdPrefab.GetComponentInChildren<Animator>();
            if (source != null)
            {
                animator.avatar = source.avatar;
            }
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;

        playback.graph = PlayableGraph.Create(spawned.name + " Flap");
        playback.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(playback.graph, "Flap", animator);
        playback.playable = AnimationClipPlayable.Create(playback.graph, clip);
        playback.length = Mathf.Max(0.01f, clip.length);
        float cycle = (index + Random.Range(0.05f, 0.85f)) / Mathf.Max(1, FlockCount);
        playback.playable.SetTime(playback.length * Mathf.Repeat(cycle, 1f));
        playback.playable.SetSpeed(Mathf.Max(0.1f, flapSpeed));
        output.SetSourcePlayable(playback.playable);
        playback.graph.Play();
        return playback;
    }

    void StepFlaps()
    {
        if (flaps == null)
        {
            return;
        }

        for (int i = 0; i < flaps.Length; i++)
        {
            if (!flaps[i].graph.IsValid() || flaps[i].length <= 0.01f)
            {
                continue;
            }

            double time = flaps[i].playable.GetTime();
            if (time >= flaps[i].length)
            {
                flaps[i].playable.SetTime(time % flaps[i].length);
            }
        }
    }

    void StopFlaps()
    {
        if (flaps == null)
        {
            return;
        }

        for (int i = 0; i < flaps.Length; i++)
        {
            if (flaps[i].graph.IsValid())
            {
                flaps[i].graph.Destroy();
            }
        }

        flaps = null;
    }

    AnimationClip FindFlapClip()
    {
        if (flapClip != null)
        {
            return flapClip;
        }

#if UNITY_EDITOR
        if (birdPrefab == null)
        {
            return null;
        }

        string path = AssetDatabase.GetAssetPath(birdPrefab);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        AnimationClip fallback = null;
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__"))
            {
                continue;
            }

            if (clip.name.IndexOf("flap", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                flapClip = clip;
                return flapClip;
            }

            if (fallback == null)
            {
                fallback = clip;
            }
        }

        flapClip = fallback;
#endif
        return flapClip;
    }

    void ClearBirds()
    {
        StopFlaps();
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
        fleeFromDirs = null;
        joinedCircle = null;
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

    Vector3 ConsumeTruckMove()
    {
        if (truck == null)
        {
            return Vector3.zero;
        }

        Vector3 pos = truck.transform.position;
        Vector3 delta = pos - lastTruckPosition;
        lastTruckPosition = pos;
        return delta;
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

        bird.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * birdModelOffset;
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
