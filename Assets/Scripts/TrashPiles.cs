using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TrashPiles : MonoBehaviour
{
    const string RandomTrashFolder = "Assets/Prefab/randomTrash";

    [Header("Trash Piles")]
    [SerializeField, Tooltip("Auto-loaded from Assets/Prefab/randomTrash. Add or remove prefabs in that folder.")]
    GameObject[] randomTrash;
    [SerializeField] Transform border;
    [SerializeField, Min(3), Tooltip("Number of edges on the empty inner polygon.")] int count = 8;
    [SerializeField, Tooltip("Typical starting distance from the circle for blocks that sit further out.")] float radius = 12f;
    [SerializeField] float extraClearance = 2f;
    [SerializeField] float pileHeight = 0.8f;
    [SerializeField] float barrierHeight = 8f;
    [SerializeField, Tooltip("Empty space between each block and the polygon inner edge.")] float blockGap = 0.45f;
    [SerializeField, Tooltip("Minimum extra space kept between neighboring blocks.")] float blockSeparation = 0.85f;
    [SerializeField, Tooltip("How differently far neighboring corners can sit from the center. Stops deep spikes that fill the hole.")] float maxNeighborRadiusDelta = 2.75f;
    [SerializeField, Tooltip("Extra padding added on top of the compactor width for the minimum edge length.")] float minEdgeLengthPadding = 0.15f;
    [SerializeField, Tooltip("How much farther from the circle blocks start, so the player can read the layout.")] float startBackOffset = 8f;
    [SerializeField, Min(0), Tooltip("How many blocks start much closer to the circle than the rest.")] int closeThreatCount = 2;
    [SerializeField, Tooltip("How much farther from the circle those closest walls start, so a falling block does not land on the border.")] float closeThreatBackOffset = 3.5f;
    [SerializeField, Tooltip("Maximum distance from the circle a block can be pushed. Still cannot leave the floor.")] float maxPushDistance = 70f;
    [SerializeField] float creepSpeed = 0.8f;
    [SerializeField, Range(0.05f, 1f), Tooltip("How fast a wall creeps when no trash block is sitting on it.")] float emptyWallCreepScale = 0.4f;
    [SerializeField, Tooltip("Delay between each wall's first drop, closest to the border first.")] float firstFallStagger = 0.35f;
    [SerializeField, Range(0f, 0.95f), Tooltip("How far along the crawl the opening trash starts (0 = far out, 1 = at the lip).")] float closestHeadStart = 0.55f;
    [SerializeField] float stopHoldSeconds = 2.5f;
    [SerializeField] float pushSpeedThreshold = 0.12f;
    [SerializeField, Tooltip("How much farther the outermost starting blocks sit from the closest ones.")] float startSpread = 7f;
    [SerializeField, Tooltip("Hard cap on how far starting edges can sit from center. Also clamped to the camera view.")] float maxStartDistance = 24f;
    [SerializeField, Range(0.5f, 1f), Tooltip("Keep starting edges this far inside the camera ground footprint.")] float startViewPadding = 0.98f;
    [SerializeField, Min(1), Tooltip("Fail when this many non-front trash blocks touch the truck at once.")] int failContactCount = 3;
    [SerializeField, Range(0.05f, 0.95f), Tooltip("How centered a block must be in front of the truck to be pushable. Lower = wider front cone.")] float frontPushDot = 0.2f;
    [SerializeField] Material pileMaterial;
    [SerializeField] Color pileColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField, Tooltip("How far the trash floor extends past the real floor so corners stay covered.")] float coverPadding = 70f;
    [SerializeField] float crawlSpeedMin = 1.5f;
    [SerializeField] float crawlSpeedMax = 1.7f;
    [SerializeField, Tooltip("How long a landed block can be pushed before the pile swallows it.")] float pushWindowMin = 2.75f;
    [SerializeField, Tooltip("Longest time a landed block waits for a push.")] float pushWindowMax = 4.25f;
    [SerializeField, Tooltip("How much farther the wall recedes after the block is shoved into it and disappears.")] float finishPush = 3.6f;
    [SerializeField, Tooltip("How quickly a receding edge eases into place. Higher is snappier.")] float edgeRecedeLerp = 6f;
    [SerializeField] float consumeDuration = 0.85f;
    [SerializeField, Tooltip("How high expired trash floats while fading out.")] float consumeRiseHeight = 2.4f;
    [SerializeField] float respawnDelayMin = 0.5f;
    [SerializeField] float respawnDelayMax = 1.25f;
    [SerializeField] float fallDuration = 0.45f;

    enum BlockLife
    {
        Crawling,
        Falling,
        Placed,
        Shoving,
        Consuming,
        Hidden
    }

    Vector3 center;
    Vector3[] corners;
    float[] idleSeconds;
    bool[] pushedThisStep;
    bool[] vertexMovedThisStep;
    int[] overlapCount;
    int[] wallIgnoreCount;
    bool[] cubeHoldsWalls;
    Collider[] carColliders;
    float outerRadius;
    float minRadius;
    float borderRadius;
    float cubeExtent;
    float cubeY;
    float floorMinX;
    float floorMaxX;
    float floorMinZ;
    float floorMaxZ;
    float coverRadius;
    bool hasFloorBounds;
    bool dirty;
    int builtCount = -1;
    int lastSpawnEdge = -1;

    Transform worldRoot;
    Mesh pileMesh;
    MeshFilter pileFilter;
    Transform[] cornerBlocks;
    Rigidbody[] cornerBodies;
    Renderer[] blockRenderers;
    BlockLife[] blockLife;
    float[] crawlSpeed;
    Vector3[] crawlPos;
    Vector3[] fallFrom;
    float[] fallElapsed;
    float[] crawlHold;
    float[] openingHeadStart;
    float[] despawnLeft;
    float[] hiddenLeft;
    float[] recedeLeft;
    float[] shoveDepth;
    Quaternion[] blockSpin;
    Vector3[] blockBaseScale;
    bool[] consumeFadeReady;
    Transform[] edgeWalls;
    readonly List<Vector3> verts = new List<Vector3>(2048);
    readonly List<Vector2> uvs = new List<Vector2>(2048);
    readonly List<int> tris = new List<int>(4096);
    readonly List<Vector3> sectorOuter = new List<Vector3>(64);
    static MaterialPropertyBlock sharedFadeBlock;

    void Start()
    {
        RefreshRandomTrashFromFolder();
        BuildWorld();
    }

    void OnValidate()
    {
        count = Mathf.Max(3, count);
        pileHeight = Mathf.Max(0.05f, pileHeight);
        barrierHeight = Mathf.Max(pileHeight, barrierHeight);
        blockGap = Mathf.Max(0.05f, blockGap);
        blockSeparation = Mathf.Max(0.1f, blockSeparation);
        maxNeighborRadiusDelta = Mathf.Max(0.35f, maxNeighborRadiusDelta);
        minEdgeLengthPadding = Mathf.Max(0f, minEdgeLengthPadding);
        startBackOffset = Mathf.Max(0f, startBackOffset);
        closeThreatCount = Mathf.Clamp(closeThreatCount, 0, 8);
        closeThreatBackOffset = Mathf.Max(0f, closeThreatBackOffset);
        maxPushDistance = Mathf.Max(1f, maxPushDistance);
        coverPadding = Mathf.Max(10f, coverPadding);
        pushSpeedThreshold = Mathf.Max(0.01f, pushSpeedThreshold);
        failContactCount = Mathf.Max(1, failContactCount);
        frontPushDot = Mathf.Clamp(frontPushDot, 0.05f, 0.95f);
        startSpread = Mathf.Max(0.25f, startSpread);
        maxStartDistance = Mathf.Max(4f, maxStartDistance);
        startViewPadding = Mathf.Clamp(startViewPadding, 0.5f, 1f);
        emptyWallCreepScale = Mathf.Clamp(emptyWallCreepScale, 0.05f, 1f);
        firstFallStagger = Mathf.Max(0f, firstFallStagger);
        closestHeadStart = Mathf.Clamp01(closestHeadStart);
        crawlSpeedMin = Mathf.Max(0.1f, crawlSpeedMin);
        crawlSpeedMax = Mathf.Max(crawlSpeedMin, crawlSpeedMax);
        pushWindowMin = Mathf.Max(0.35f, pushWindowMin);
        pushWindowMax = Mathf.Max(pushWindowMin, pushWindowMax);
        finishPush = Mathf.Max(0.1f, finishPush);
        edgeRecedeLerp = Mathf.Max(0.5f, edgeRecedeLerp);
        consumeDuration = Mathf.Max(0.05f, consumeDuration);
        consumeRiseHeight = Mathf.Max(0.25f, consumeRiseHeight);
        respawnDelayMin = Mathf.Max(0f, respawnDelayMin);
        respawnDelayMax = Mathf.Max(respawnDelayMin, respawnDelayMax);
        fallDuration = Mathf.Max(0.05f, fallDuration);
        RefreshRandomTrashFromFolder();
    }

    void FixedUpdate()
    {
        if (count != builtCount)
        {
            BuildWorld();
        }

        if (worldRoot == null || corners == null || ExperienceRestart.IsEnded)
        {
            return;
        }

        ClearPushedFlags();
        PushContactingBlocks();
        ApplyRecede();
        ApplyIdleOrCreep();
        UpdateBlockLife();
        if (dirty)
        {
            ApplyShape();
        }

        ApplyBlockVisuals();
        CheckFailFromTrashContacts();
    }

    public bool IsBlockPushable(int index)
    {
        if (blockLife == null || index < 0 || index >= blockLife.Length)
        {
            return false;
        }

        BlockLife life = blockLife[index];
        return life == BlockLife.Placed || life == BlockLife.Shoving;
    }

    /// <summary>
    /// Picks a random point on the floor inside the creeping pile walls.
    /// </summary>
    public bool TrySamplePlayAreaPoint(out Vector3 point, float edgeInset = 1.5f, int maxAttempts = 64)
    {
        point = Vector3.zero;
        if (corners == null || corners.Length < 3)
        {
            return false;
        }

        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < corners.Length; i++)
        {
            centroid += corners[i];
        }

        centroid /= corners.Length;
        centroid.y = 0f;

        edgeInset = Mathf.Max(0f, edgeInset);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float hitDist = RayToPlayEdge(centroid, dir);
            if (hitDist <= edgeInset + 0.35f)
            {
                continue;
            }

            float maxReach = hitDist - edgeInset;
            Vector3 candidate = centroid + dir * Random.Range(0f, maxReach);
            candidate.y = 0.02f;
            if (!PointInPlayPolygon(candidate))
            {
                continue;
            }

            point = candidate;
            return true;
        }

        // Last resort: stay near the polygon middle, never the truck transform.
        Vector3 fallback = centroid;
        fallback.y = 0.02f;
        if (PointInPlayPolygon(fallback) && DistanceToPlayEdge(fallback) > 0.2f)
        {
            point = fallback;
            return true;
        }

        return false;
    }

    float RayToPlayEdge(Vector3 origin, Vector3 dir)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[Wrap(i + 1)];
            if (RaySegmentIntersectXZ(origin, dir, a, b, out float t) && t > 0.001f)
            {
                best = Mathf.Min(best, t);
            }
        }

        return float.IsInfinity(best) ? 0f : best;
    }

    static bool RaySegmentIntersectXZ(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, out float t)
    {
        t = 0f;
        Vector2 o = new Vector2(origin.x, origin.z);
        Vector2 d = new Vector2(dir.x, dir.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);
        Vector2 s = bb - aa;

        float denom = d.x * s.y - d.y * s.x;
        if (Mathf.Abs(denom) < 0.000001f)
        {
            return false;
        }

        Vector2 ao = aa - o;
        float rayT = (ao.x * s.y - ao.y * s.x) / denom;
        float segT = (ao.x * d.y - ao.y * d.x) / denom;
        if (rayT < 0f || segT < 0f || segT > 1f)
        {
            return false;
        }

        t = rayT;
        return true;
    }

    public bool PointInPlayPolygon(Vector3 worldPoint)
    {
        if (corners == null || corners.Length < 3)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[j];
            bool crosses = ((a.z > worldPoint.z) != (b.z > worldPoint.z))
                && (worldPoint.x < (b.x - a.x) * (worldPoint.z - a.z) / ((b.z - a.z) + Mathf.Epsilon) + a.x);
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public float AverageCornerRadius()
    {
        if (corners == null || corners.Length == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            sum += FlatDistanceFromCenter(i);
        }

        return sum / corners.Length;
    }

    public bool IsPlayAreaSpacious(float minAverageRadius)
    {
        return AverageCornerRadius() >= Mathf.Max(1f, minAverageRadius);
    }

    float DistanceToPlayEdge(Vector3 worldPoint)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[Wrap(i + 1)];
            best = Mathf.Min(best, DistancePointToSegmentXZ(worldPoint, a, b));
        }

        return best;
    }

    static float DistancePointToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);
        Vector2 ab = bb - aa;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f)
        {
            return Vector2.Distance(p, aa);
        }

        float t = Mathf.Clamp01(Vector2.Dot(p - aa, ab) / lenSq);
        Vector2 closest = aa + ab * t;
        return Vector2.Distance(p, closest);
    }

    public void SetBlockContact(int index, bool overlapping)
    {
        if (overlapCount == null || index < 0 || index >= overlapCount.Length)
        {
            return;
        }

        if (overlapping)
        {
            overlapCount[index]++;
        }
        else
        {
            overlapCount[index] = Mathf.Max(0, overlapCount[index] - 1);
        }
    }

    public void PushBlock(int index, Rigidbody carBody)
    {
        if (ExperienceRestart.IsEnded || corners == null || carBody == null)
        {
            return;
        }

        if (index < 0 || index >= corners.Length || !IsBlockPushable(index))
        {
            return;
        }

        if (!IsFrontPush(index, carBody.transform))
        {
            return;
        }

        CarController car = carBody.GetComponent<CarController>();
        if (car != null && car.IsArmRaised)
        {
            return;
        }

        Vector3 drive = DriveFromCar(carBody);
        if (drive.sqrMagnitude <= pushSpeedThreshold * pushSpeedThreshold)
        {
            return;
        }

        Vector3 pushDir = PushDirection(index, carBody);
        Vector3 outward = EdgeOutward(index);
        float intoPush = Vector3.Dot(drive, pushDir);
        float intoOut = Vector3.Dot(drive, outward);
        // Allow glancing drives as long as some motion is toward the pile/block.
        if (intoPush <= -0.15f && intoOut <= -0.15f)
        {
            return;
        }

        if (blockLife[index] == BlockLife.Placed)
        {
            BeginShove(index);
        }

        if (pushedThisStep[index])
        {
            return;
        }

        pushedThisStep[index] = true;
        DriveBlockIntoPile(index, drive);
    }

    void CheckFailFromTrashContacts()
    {
        if (ExperienceRestart.IsEnded || overlapCount == null)
        {
            return;
        }

        int touching = 0;
        int needed = Mathf.Max(1, failContactCount);
        for (int i = 0; i < overlapCount.Length; i++)
        {
            if (overlapCount[i] <= 0)
            {
                continue;
            }

            // Front trash is for pushing — only flanks/rear count toward fail.
            if (IsFrontPush(i, transform))
            {
                continue;
            }

            touching++;
            if (touching >= needed)
            {
                ExperienceRestart.NotifyFailed();
                return;
            }
        }
    }

    Bounds TruckBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        bool any = false;
        if (carColliders != null)
        {
            for (int i = 0; i < carColliders.Length; i++)
            {
                Collider col = carColliders[i];
                if (col == null || !col.enabled || col.isTrigger)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = col.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(col.bounds);
                }
            }
        }

        if (!any)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
        }

        return bounds;
    }

    bool IsEdgeInFront(int index, Transform truck)
    {
        if (truck == null)
        {
            return false;
        }

        Vector3 forward = truck.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 toEdge = EdgeMid(index) - truck.position;
        toEdge.y = 0f;
        if (toEdge.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(toEdge.normalized, forward) >= frontPushDot;
    }

    bool IsFrontPush(int index, Transform truck)
    {
        if (truck == null)
        {
            return false;
        }

        Vector3 forward = truck.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 toBlock = ActiveBlockPosition(index) - truck.position;
        toBlock.y = 0f;
        if (toBlock.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(toBlock.normalized, forward) >= frontPushDot;
    }

    Vector3 ActiveBlockPosition(int index)
    {
        if (blockLife != null && blockLife[index] == BlockLife.Shoving)
        {
            return crawlPos[index];
        }

        if (cornerBlocks != null && cornerBlocks[index] != null)
        {
            return cornerBlocks[index].position;
        }

        return BlockPosition(index);
    }

    void PushContactingBlocks()
    {
        if (overlapCount == null)
        {
            return;
        }

        Rigidbody carBody = GetComponent<Rigidbody>();
        if (carBody == null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (overlapCount[i] > 0)
            {
                PushBlock(i, carBody);
            }
        }
    }

    Vector3 DriveFromCar(Rigidbody carBody)
    {
        CarController car = carBody.GetComponent<CarController>();
        if (car != null && car.DriveVelocity.sqrMagnitude > 0.0001f)
        {
            return car.DriveVelocity;
        }

        Vector3 velocity = carBody.linearVelocity;
        velocity.y = 0f;
        return velocity;
    }

    Vector3 PushDirection(int index, Rigidbody carBody)
    {
        Vector3 dir;
        float depth;
        if (TryPenetration(index, carBody, out dir, out depth) && dir.sqrMagnitude > 0.0001f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                return dir.normalized;
            }
        }

        Vector3 toBlock = BlockPosition(index) - carBody.position;
        toBlock.y = 0f;
        if (toBlock.sqrMagnitude > 0.0001f)
        {
            return toBlock.normalized;
        }

        return EdgeOutward(index);
    }

    bool TryPenetration(int index, Rigidbody carBody, out Vector3 direction, out float distance)
    {
        direction = Vector3.zero;
        distance = 0f;
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return false;
        }

        Collider blockCollider = cornerBlocks[index].GetComponentInChildren<Collider>();
        if (blockCollider == null)
        {
            return false;
        }

        Collider[] colliders = carColliders;
        if (colliders == null || colliders.Length == 0 || carBody.gameObject != gameObject)
        {
            colliders = carBody.GetComponentsInChildren<Collider>();
        }
        bool found = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider other = colliders[i];
            if (other == null || !other.enabled || other.isTrigger)
            {
                continue;
            }

            Vector3 separate;
            float depth;
            if (!Physics.ComputePenetration(
                    blockCollider, blockCollider.transform.position, blockCollider.transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out separate, out depth))
            {
                continue;
            }

            if (depth > distance)
            {
                direction = separate;
                distance = depth;
                found = true;
            }
        }

        return found;
    }

    void SetCornerWallsIgnored(int index, bool ignore)
    {
        if (cubeHoldsWalls == null || index < 0 || index >= cubeHoldsWalls.Length)
        {
            return;
        }

        if (cubeHoldsWalls[index] == ignore)
        {
            return;
        }

        cubeHoldsWalls[index] = ignore;
        AdjustWallIgnore(index, ignore);
    }

    void AdjustWallIgnore(int wallIndex, bool add)
    {
        if (wallIgnoreCount == null || edgeWalls == null || carColliders == null)
        {
            return;
        }

        if (add)
        {
            wallIgnoreCount[wallIndex]++;
        }
        else
        {
            wallIgnoreCount[wallIndex] = Mathf.Max(0, wallIgnoreCount[wallIndex] - 1);
        }

        bool ignore = wallIgnoreCount[wallIndex] > 0;
        Collider wallCollider = edgeWalls[wallIndex].GetComponent<Collider>();
        if (wallCollider == null)
        {
            return;
        }

        for (int i = 0; i < carColliders.Length; i++)
        {
            Collider carCollider = carColliders[i];
            if (carCollider == null || !carCollider.enabled || carCollider.isTrigger)
            {
                continue;
            }

            Physics.IgnoreCollision(carCollider, wallCollider, ignore);
        }
    }

    void MoveEdge(int index, Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        int next = Wrap(index + 1);
        MoveVertex(index, delta);
        MoveVertex(next, delta);
    }

    void MoveVertex(int index, Vector3 delta)
    {
        if (vertexMovedThisStep[index] || delta.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        corners[index] += delta;
        ClampCorner(index);
        vertexMovedThisStep[index] = true;
        idleSeconds[index] = 0f;
    }

    void UpdateBlockLife()
    {
        if (blockLife == null)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        for (int i = 0; i < count; i++)
        {
            switch (blockLife[i])
            {
                case BlockLife.Crawling:
                    StepCrawl(i, dt);
                    break;
                case BlockLife.Falling:
                    StepFall(i, dt);
                    break;
                case BlockLife.Placed:
                    despawnLeft[i] -= dt;
                    if (despawnLeft[i] <= 0f)
                    {
                        BeginConsume(i);
                    }

                    break;
                case BlockLife.Shoving:
                    StepShove(i, dt);
                    break;
                case BlockLife.Consuming:
                    StepConsume(i, dt);
                    break;
                case BlockLife.Hidden:
                    hiddenLeft[i] -= dt;
                    if (hiddenLeft[i] <= 0f)
                    {
                        TryRespawnSpreadTrash(i);
                    }

                    break;
            }
        }
    }

    void BeginCrawl(int index)
    {
        ClearBlockContact(index);
        SetBlockInteractable(index, false);
        SetBlockVisible(index, true);
        blockLife[index] = BlockLife.Crawling;
        crawlSpeed[index] = Random.Range(crawlSpeedMin, crawlSpeedMax);
        ResetBlockConsumeVisuals(index);
        crawlPos[index] = CrawlStart(index);
        despawnLeft[index] = -1f;
        hiddenLeft[index] = 0f;
        if (blockSpin != null)
        {
            blockSpin[index] = WithRandomY(BlockSpin(index));
        }

        ApplyBlockTransform(index, crawlPos[index], BlockSpin(index));
        lastSpawnEdge = index;
    }

    void TryRespawnSpreadTrash(int readyIndex)
    {
        if (blockLife == null || readyIndex < 0 || readyIndex >= count)
        {
            return;
        }

        if (blockLife[readyIndex] != BlockLife.Hidden)
        {
            return;
        }

        int target = PickSpreadSpawnEdge();
        if (target < 0)
        {
            hiddenLeft[readyIndex] = Random.Range(respawnDelayMin, respawnDelayMax);
            return;
        }

        BeginCrawl(target);
        if (target != readyIndex && blockLife[readyIndex] == BlockLife.Hidden)
        {
            // This edge yielded to a farther slot — wait again for another turn.
            hiddenLeft[readyIndex] = Random.Range(respawnDelayMin, respawnDelayMax);
        }
    }

    int PickSpreadSpawnEdge()
    {
        int best = -1;
        float bestScore = float.NegativeInfinity;

        // Prefer edges that are not beside live trash; only fall back if every hidden slot is adjacent.
        for (int pass = 0; pass < 2; pass++)
        {
            bool requireOpenSide = pass == 0;
            for (int candidate = 0; candidate < count; candidate++)
            {
                if (blockLife[candidate] != BlockLife.Hidden)
                {
                    continue;
                }

                if (requireOpenSide && HasLiveNeighbor(candidate))
                {
                    continue;
                }

                float score = SpreadScoreForEdge(candidate);
                score += Random.Range(0f, 0.05f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best >= 0)
            {
                return best;
            }
        }

        return best;
    }

    float SpreadScoreForEdge(int index)
    {
        if (HasLiveNeighbor(index))
        {
            // Strong penalty so adjacent edges almost never win when a farther slot exists.
            return -1000f + EdgeSeparation(index, lastSpawnEdge >= 0 ? lastSpawnEdge : index) * 0.01f;
        }

        Vector3 point = EdgeMid(index);
        point.y = 0f;
        float nearest = float.PositiveInfinity;
        bool anyLive = false;
        for (int i = 0; i < count; i++)
        {
            if (i == index || !EdgeHasLiveTrash(i))
            {
                continue;
            }

            anyLive = true;
            nearest = Mathf.Min(nearest, EdgeSeparation(index, i));
        }

        if (!anyLive)
        {
            if (lastSpawnEdge < 0)
            {
                return Random.value;
            }

            return EdgeSeparation(index, lastSpawnEdge);
        }

        return nearest;
    }

    bool HasLiveNeighbor(int index)
    {
        return EdgeHasLiveTrash(Wrap(index - 1)) || EdgeHasLiveTrash(Wrap(index + 1));
    }

    bool EdgeHasLiveTrash(int index)
    {
        if (blockLife == null || index < 0 || index >= blockLife.Length)
        {
            return false;
        }

        BlockLife life = blockLife[index];
        return life != BlockLife.Hidden;
    }

    void StartOpeningCrawls()
    {
        // Start a spread-out subset; never place opening trash on neighboring edges.
        int initialCount = Mathf.Clamp(Mathf.Max(2, count / 2), 1, Mathf.Max(1, count / 2 + count % 2));
        bool[] selected = new bool[count];
        int[] chosen = new int[initialCount];
        int chosenCount = 0;

        chosen[0] = Random.Range(0, count);
        selected[chosen[0]] = true;
        chosenCount = 1;

        for (int n = 1; n < initialCount; n++)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int candidate = 0; candidate < count; candidate++)
            {
                if (selected[candidate] || IsNeighborOfSelected(candidate, selected))
                {
                    continue;
                }

                float nearest = float.PositiveInfinity;
                for (int s = 0; s < chosenCount; s++)
                {
                    nearest = Mathf.Min(nearest, EdgeSeparation(candidate, chosen[s]));
                }

                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = candidate;
                }
            }

            if (best < 0)
            {
                break;
            }

            chosen[chosenCount++] = best;
            selected[best] = true;
        }

        float stagger = Mathf.Max(0f, firstFallStagger);
        float headBase = Mathf.Clamp(closestHeadStart, 0.45f, 0.85f);
        for (int rank = 0; rank < chosenCount; rank++)
        {
            int index = chosen[rank];
            float rankT = chosenCount <= 1 ? 0f : rank / (float)(chosenCount - 1);
            openingHeadStart[index] = Mathf.Lerp(headBase + 0.12f, headBase, rankT);
            crawlHold[index] = rank * stagger * 0.25f;
            BeginCrawl(index);
        }

        for (int i = 0; i < count; i++)
        {
            if (selected[i])
            {
                continue;
            }

            BeginHidden(i);
            hiddenLeft[i] = Random.Range(respawnDelayMin, respawnDelayMax) + (i + 1) * stagger;
        }
    }

    bool IsNeighborOfSelected(int candidate, bool[] selected)
    {
        return selected[Wrap(candidate - 1)] || selected[Wrap(candidate + 1)];
    }

    float EdgeSeparation(int a, int b)
    {
        Vector3 pa = EdgeMid(a);
        Vector3 pb = EdgeMid(b);
        pa.y = 0f;
        pb.y = 0f;
        return Vector3.Distance(pa, pb);
    }

    void BeginFall(int index)
    {
        blockLife[index] = BlockLife.Falling;
        fallFrom[index] = crawlPos[index];
        fallElapsed[index] = 0f;
    }

    void BeginPlaced(int index)
    {
        blockLife[index] = BlockLife.Placed;
        despawnLeft[index] = Random.Range(pushWindowMin, pushWindowMax);
        SetBlockInteractable(index, true);
        SetBlockVisible(index, true);
        Vector3 pos = BlockPosition(index);
        ApplyBlockTransform(index, pos, BlockSpin(index));
    }

    void BeginShove(int index)
    {
        SetBlockInteractable(index, true);
        SetBlockVisible(index, true);
        Vector3 pos = cornerBlocks[index] != null ? cornerBlocks[index].position : BlockPosition(index);
        pos.y = 0f;
        crawlPos[index] = pos;
        shoveDepth[index] = Vector3.Dot(pos - EdgeMid(index), EdgeOutward(index));
        blockLife[index] = BlockLife.Shoving;
        // Keep the remaining push window so a partial shove still gets ignored/consumed.
        if (despawnLeft[index] <= 0f)
        {
            despawnLeft[index] = Random.Range(pushWindowMin, pushWindowMax);
        }

        SetCornerWallsIgnored(index, true);
    }

    void DriveBlockIntoPile(int index, Vector3 drive)
    {
        Vector3 outward = EdgeOutward(index);
        float along = Vector3.Dot(drive, outward);
        if (along <= 0f)
        {
            return;
        }

        shoveDepth[index] += along * Time.fixedDeltaTime;
        shoveDepth[index] = Mathf.Max(shoveDepth[index], -BlockInset());
        ApplyShovePose(index);
        CheckSwallow(index);
    }

    void ApplyShovePose(int index)
    {
        Vector3 pos = EdgeMid(index) + EdgeOutward(index) * shoveDepth[index];
        pos.y = 0f;
        crawlPos[index] = pos;
        ApplyBlockTransform(index, pos, BlockSpin(index));
    }

    void CheckSwallow(int index)
    {
        if (blockLife[index] != BlockLife.Shoving)
        {
            return;
        }

        if (shoveDepth[index] >= cubeExtent * 0.85f)
        {
            QueueRecede(index, finishPush);
            BeginHidden(index);
        }
    }

    void BeginConsume(int index)
    {
        ClearBlockContact(index);
        SetBlockInteractable(index, false);
        SetBlockVisible(index, true);
        blockLife[index] = BlockLife.Consuming;
        fallFrom[index] = cornerBlocks[index] != null ? cornerBlocks[index].position : BlockPosition(index);
        fallElapsed[index] = 0f;
        despawnLeft[index] = -1f;
        PrepareConsumeFade(index);
        SetBlockOpacity(index, 1f);
    }

    void BeginHidden(int index)
    {
        ClearBlockContact(index);
        SetBlockInteractable(index, false);
        ResetBlockConsumeVisuals(index);
        SetBlockVisible(index, false);
        blockLife[index] = BlockLife.Hidden;
        hiddenLeft[index] = Random.Range(respawnDelayMin, respawnDelayMax);
        despawnLeft[index] = -1f;
        crawlHold[index] = 0f;
    }

    void StepCrawl(int index, float dt)
    {
        if (crawlHold[index] > 0f)
        {
            crawlHold[index] -= dt;
            if (crawlHold[index] > 0f)
            {
                ApplyBlockTransform(index, crawlPos[index], BlockSpin(index));
                return;
            }
        }

        Vector3 target = CrawlLip(index);
        crawlPos[index] = Vector3.MoveTowards(crawlPos[index], target, crawlSpeed[index] * dt);
        ApplyBlockTransform(index, crawlPos[index], BlockSpin(index));
        if ((crawlPos[index] - target).sqrMagnitude <= 0.0004f)
        {
            crawlPos[index] = target;
            BeginFall(index);
        }
    }

    void StepFall(int index, float dt)
    {
        fallElapsed[index] += dt;
        float t = Mathf.Clamp01(fallElapsed[index] / fallDuration);
        Vector3 to = BlockPosition(index);
        Vector3 from = fallFrom[index];
        Vector3 pos;
        pos.x = Mathf.Lerp(from.x, to.x, t);
        pos.z = Mathf.Lerp(from.z, to.z, t);
        pos.y = Mathf.Lerp(from.y, to.y, t * t);
        ApplyBlockTransform(index, pos, BlockSpin(index));
        if (t >= 1f)
        {
            BeginPlaced(index);
        }
    }

    void StepShove(int index, float dt)
    {
        ApplyShovePose(index);
        despawnLeft[index] -= dt;
        if (despawnLeft[index] <= 0f)
        {
            BeginConsume(index);
        }
    }

    void StepConsume(int index, float dt)
    {
        fallElapsed[index] += dt;
        float t = Mathf.Clamp01(fallElapsed[index] / consumeDuration);
        float ease = t * t;
        Vector3 from = fallFrom[index];
        Vector3 pile = IntoPilePoint(index, cubeExtent * 1.6f, PileTopY());
        Vector3 pos;
        pos.x = Mathf.Lerp(from.x, pile.x, t);
        pos.z = Mathf.Lerp(from.z, pile.z, t);
        // Slide back into the pile while floating up and fading out.
        float startY = Mathf.Max(from.y, PileTopY());
        pos.y = Mathf.Lerp(startY, startY + consumeRiseHeight, ease);

        Transform root = cornerBlocks[index];
        if (root != null)
        {
            Quaternion rot = BlockSpin(index);
            root.SetPositionAndRotation(pos, rot);
            Vector3 baseScale = blockBaseScale != null ? blockBaseScale[index] : Vector3.one;
            root.localScale = baseScale * Mathf.Lerp(1f, 0.08f, ease);
            if (cornerBodies[index] != null)
            {
                cornerBodies[index].position = pos;
                cornerBodies[index].rotation = rot;
            }
        }

        SetBlockOpacity(index, 1f - ease);
        if (t >= 1f)
        {
            BeginHidden(index);
        }
    }

    void PrepareConsumeFade(int index)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        if (consumeFadeReady != null && consumeFadeReady[index])
        {
            return;
        }

        Renderer[] renderers = cornerBlocks[index].GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                SetupTransparentFade(materials[m]);
            }

            renderer.materials = materials;
        }

        if (consumeFadeReady != null)
        {
            consumeFadeReady[index] = true;
        }
    }

    void ResetBlockConsumeVisuals(int index)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        if (blockBaseScale != null)
        {
            cornerBlocks[index].localScale = blockBaseScale[index];
        }

        SetBlockOpacity(index, 1f);
        Renderer[] renderers = cornerBlocks[index].GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            if (renderers[r] != null)
            {
                renderers[r].SetPropertyBlock(null);
            }
        }
    }

    void SetBlockOpacity(int index, float alpha)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        alpha = Mathf.Clamp01(alpha);
        if (sharedFadeBlock == null)
        {
            sharedFadeBlock = new MaterialPropertyBlock();
        }

        Renderer[] renderers = cornerBlocks[index].GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            Material mat = renderer.sharedMaterial;
            Color color = Color.white;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    color = mat.GetColor("_BaseColor");
                }
                else if (mat.HasProperty("_Color"))
                {
                    color = mat.GetColor("_Color");
                }
                else
                {
                    color = mat.color;
                }
            }

            color.a = alpha;
            sharedFadeBlock.Clear();
            sharedFadeBlock.SetColor("_BaseColor", color);
            sharedFadeBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(sharedFadeBlock);

            // Keep instance materials in sync for shaders that ignore property blocks for alpha.
            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null)
                {
                    continue;
                }

                if (materials[m].HasProperty("_BaseColor"))
                {
                    Color baseColor = materials[m].GetColor("_BaseColor");
                    baseColor.a = alpha;
                    materials[m].SetColor("_BaseColor", baseColor);
                }

                if (materials[m].HasProperty("_Color"))
                {
                    Color tint = materials[m].GetColor("_Color");
                    tint.a = alpha;
                    materials[m].SetColor("_Color", tint);
                }

                materials[m].color = new Color(materials[m].color.r, materials[m].color.g, materials[m].color.b, alpha);
            }
        }
    }

    static void SetupTransparentFade(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
        }
        else if (material.shader != null && material.shader.name.Contains("Standard"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
    }

    Vector3 IntoPilePoint(int index, float extraOut, float y)
    {
        Vector3 pos = EdgeMid(index) + EdgeOutward(index) * extraOut;
        pos.y = y;
        return pos;
    }

    void QueueRecede(int index, float distance)
    {
        if (recedeLeft == null || index < 0 || index >= recedeLeft.Length || distance <= 0.0001f)
        {
            return;
        }

        recedeLeft[index] += distance;
    }

    void ApplyRecede()
    {
        if (recedeLeft == null)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        float lerp = 1f - Mathf.Exp(-edgeRecedeLerp * dt);
        for (int i = 0; i < count; i++)
        {
            if (recedeLeft[i] <= 0.0001f)
            {
                recedeLeft[i] = 0f;
                continue;
            }

            float step = recedeLeft[i] * lerp;
            if (recedeLeft[i] < 0.03f || step >= recedeLeft[i] - 0.001f)
            {
                step = recedeLeft[i];
            }

            recedeLeft[i] -= step;
            SlideEdgeOut(i, step);
            dirty = true;
        }
    }

    void SlideEdgeOut(int index, float distance)
    {
        if (distance <= 0.0001f)
        {
            return;
        }

        Vector3 delta = EdgeOutward(index) * distance;
        SlideVertex(index, delta);
        SlideVertex(Wrap(index + 1), delta);
    }

    void SlideVertex(int index, Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        corners[index] += delta;
        ClampCorner(index);
        vertexMovedThisStep[index] = true;
        idleSeconds[index] = 0f;
    }

    void ApplyBlockVisuals()
    {
        if (blockLife == null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (blockLife[i] == BlockLife.Placed)
            {
                ApplyBlockTransform(i, BlockPosition(i), BlockSpin(i));
            }
            else if (blockLife[i] == BlockLife.Shoving)
            {
                ApplyShovePose(i);
            }
        }
    }

    Vector3 CrawlStart(int index)
    {
        Vector3 dir = EdgeOutward(index);
        Vector3 lip = CrawlLip(index);
        Vector3 lipFlat = lip;
        lipFlat.y = 0f;
        float lipDist = Vector3.Distance(center, lipFlat);
        float farDist = OuterAlong(dir).magnitude;
        float dist = Mathf.Max(farDist - 0.35f, lipDist + 3f);
        Vector3 outer = center + dir * dist;
        outer.y = PileTopY();
        float head = openingHeadStart != null ? openingHeadStart[index] : 0f;
        if (openingHeadStart != null)
        {
            openingHeadStart[index] = 0f;
        }

        return Vector3.Lerp(outer, lip, Mathf.Clamp01(head));
    }

    float WallDistance(int index)
    {
        Vector3 mid = EdgeMid(index);
        mid.y = 0f;
        return Vector3.Distance(center, mid);
    }

    bool WallHasTrash(int index)
    {
        if (blockLife == null || index < 0 || index >= blockLife.Length)
        {
            return false;
        }

        BlockLife life = blockLife[index];
        return life == BlockLife.Placed || life == BlockLife.Shoving;
    }

    float WallCreepSpeed(int index)
    {
        if (WallHasTrash(index))
        {
            return creepSpeed;
        }

        float scale = emptyWallCreepScale <= 0f ? 0.4f : emptyWallCreepScale;
        return creepSpeed * Mathf.Clamp(scale, 0.05f, 1f);
    }

    float VertexCreepSpeed(int index)
    {
        return Mathf.Max(WallCreepSpeed(Wrap(index - 1)), WallCreepSpeed(index));
    }

    bool VertexHeld(int index)
    {
        // Only pause creep while an edge is receding after a block is swallowed.
        // Touching/shoving trash must not freeze the ring, or light bumps stall the game.
        if (recedeLeft == null)
        {
            return false;
        }

        int prev = Wrap(index - 1);
        return recedeLeft[index] > 0.0001f || recedeLeft[prev] > 0.0001f;
    }

    Vector3 CrawlLip(int index)
    {
        Vector3 lip = EdgeMid(index) + EdgeOutward(index) * cubeExtent;
        lip.y = PileTopY();
        return lip;
    }

    float PileTopY()
    {
        return Mathf.Max(0.05f, pileHeight);
    }

    void ApplyBlockTransform(int index, Vector3 pos, Quaternion rotation)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        float surfaceY = pos.y;
        Vector3 placed = new Vector3(pos.x, surfaceY, pos.z);
        cornerBlocks[index].SetPositionAndRotation(placed, rotation);
        placed.y = SnapSitY(cornerBlocks[index], surfaceY);
        cornerBlocks[index].SetPositionAndRotation(placed, rotation);
        if (cornerBodies[index] != null)
        {
            cornerBodies[index].position = placed;
            cornerBodies[index].rotation = rotation;
        }
    }

    static float SnapSitY(Transform root, float surfaceY)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return surfaceY;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return root.position.y + (surfaceY - bounds.min.y);
    }

    void SetBlockVisible(int index, bool visible)
    {
        if (blockRenderers != null && blockRenderers[index] != null)
        {
            blockRenderers[index].enabled = visible;
        }
    }

    void SetBlockInteractable(int index, bool interactable)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        Collider[] blockColliders = cornerBlocks[index].GetComponentsInChildren<Collider>();
        for (int i = 0; i < blockColliders.Length; i++)
        {
            if (blockColliders[i] != null)
            {
                blockColliders[i].enabled = interactable;
            }
        }
    }

    void ClearBlockContact(int index)
    {
        if (overlapCount == null || index < 0 || index >= overlapCount.Length)
        {
            return;
        }

        overlapCount[index] = 0;
        SetCornerWallsIgnored(index, false);
    }

    void BuildWorld()
    {
        if (worldRoot != null)
        {
            Destroy(worldRoot.gameObject);
            worldRoot = null;
        }

        RefreshRandomTrashFromFolder();
        if (!HasRandomTrash())
        {
            Debug.LogWarning(
                "TrashPiles: no prefabs found in " + RandomTrashFolder + ". Drop trash prefabs into that folder.",
                this);
            return;
        }

        count = Mathf.Max(3, count);
        builtCount = count;
        center = transform.position;
        center.y = 0f;
        if (border != null)
        {
            center = new Vector3(border.position.x, 0f, border.position.z);
            borderRadius = 0.5f * Mathf.Max(border.lossyScale.x, border.lossyScale.z);
        }
        else
        {
            borderRadius = 7f;
        }

        MeasureTrashSizes();
        minRadius = Mathf.Max(1.5f, cubeExtent + 0.5f);

        Transform floor = null;
        GameObject floorObject = GameObject.Find("Floor");
        if (floorObject != null)
        {
            floor = floorObject.transform;
        }

        hasFloorBounds = floor != null;
        if (hasFloorBounds)
        {
            float halfX = 5f * Mathf.Abs(floor.lossyScale.x);
            float halfZ = 5f * Mathf.Abs(floor.lossyScale.z);
            floorMinX = floor.position.x - halfX;
            floorMaxX = floor.position.x + halfX;
            floorMinZ = floor.position.z - halfZ;
            floorMaxZ = floor.position.z + halfZ;
            outerRadius = Mathf.Max(halfX, halfZ) + (floor.position - center).magnitude;
            float pad = Mathf.Max(10f, coverPadding);
            float reach = 0f;
            float minX = floorMinX - pad;
            float maxX = floorMaxX + pad;
            float minZ = floorMinZ - pad;
            float maxZ = floorMaxZ + pad;
            reach = Mathf.Max(reach, Vector3.Distance(center, new Vector3(minX, 0f, minZ)));
            reach = Mathf.Max(reach, Vector3.Distance(center, new Vector3(minX, 0f, maxZ)));
            reach = Mathf.Max(reach, Vector3.Distance(center, new Vector3(maxX, 0f, minZ)));
            reach = Mathf.Max(reach, Vector3.Distance(center, new Vector3(maxX, 0f, maxZ)));
            coverRadius = reach / Mathf.Cos(Mathf.PI / 48f);
        }
        else
        {
            outerRadius = Mathf.Max(radius, minRadius) + 20f;
            coverRadius = outerRadius + Mathf.Max(10f, coverPadding);
        }

        corners = new Vector3[count];
        idleSeconds = new float[count];
        pushedThisStep = new bool[count];
        vertexMovedThisStep = new bool[count];
        overlapCount = new int[count];
        wallIgnoreCount = new int[count];
        cubeHoldsWalls = new bool[count];
        blockLife = new BlockLife[count];
        crawlSpeed = new float[count];
        crawlPos = new Vector3[count];
        fallFrom = new Vector3[count];
        fallElapsed = new float[count];
        crawlHold = new float[count];
        openingHeadStart = new float[count];
        despawnLeft = new float[count];
        hiddenLeft = new float[count];
        recedeLeft = new float[count];
        shoveDepth = new float[count];
        blockSpin = new Quaternion[count];
        blockBaseScale = new Vector3[count];
        consumeFadeReady = new bool[count];
        carColliders = GetComponentsInChildren<Collider>();
        PlaceStartingCorners();

        worldRoot = new GameObject("TrashPiles").transform;
        worldRoot.SetParent(null);
        worldRoot.position = center;
        worldRoot.rotation = Quaternion.identity;
        worldRoot.localScale = Vector3.one;

        GameObject meshObject = new GameObject("TrashPileFloor");
        meshObject.transform.SetParent(worldRoot, false);
        pileFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ResolvePileMaterial();
        pileMesh = new Mesh { name = "TrashPiles" };
        pileMesh.MarkDynamic();
        pileFilter.sharedMesh = pileMesh;

        SpawnCorners();
        SpawnEdgeWalls();
        IgnoreBlockAndWallCollisions();

        dirty = true;
        ApplyShape();
        StartOpeningCrawls();
    }

    void PlaceStartingCorners()
    {
        float inset = BlockInset();
        float halfSpan = (Mathf.PI / Mathf.Max(3, count)) * 1.3f;
        float apothemScale = Mathf.Max(0.35f, Mathf.Cos(Mathf.Min(halfSpan, 1.2f)));
        float landClear = borderRadius + cubeExtent + inset + 1.75f;
        float borderSafe = landClear / apothemScale;
        float carSafe = CarClearanceRadius() + inset + extraClearance + 1.5f;
        float safeMin = Mathf.Max(minRadius, borderSafe, carSafe);
        float viewCap = EstimateVisibleStartDistance();
        float startCap = Mathf.Max(safeMin + 1.25f, Mathf.Min(maxStartDistance, viewCap));

        float threatClose = safeMin + closeThreatBackOffset;
        float threatFar = threatClose + Mathf.Min(1.5f, startSpread * 0.35f);
        float normalClose = safeMin + startBackOffset;
        float normalFar = normalClose + startSpread;

        safeMin = Mathf.Min(safeMin, startCap);
        threatClose = Mathf.Clamp(threatClose, safeMin + 0.25f, startCap);
        threatFar = Mathf.Clamp(threatFar, threatClose + 0.25f, startCap);
        normalClose = Mathf.Clamp(normalClose, Mathf.Min(threatFar + 0.35f, startCap), startCap);
        normalFar = Mathf.Clamp(normalFar, normalClose + 0.25f, startCap);

        bool[] closeThreat = new bool[count];
        int threatCount = Mathf.Min(closeThreatCount, Mathf.Max(0, count - 1));
        int lastPicked = -1;
        for (int t = 0; t < threatCount; t++)
        {
            int pick = Random.Range(0, count);
            int attempts = 0;
            while (attempts < count * 3 && (closeThreat[pick] || NeighborsThreat(closeThreat, pick, lastPicked)))
            {
                pick = (pick + 1 + Random.Range(0, count - 1)) % count;
                attempts++;
            }

            closeThreat[pick] = true;
            lastPicked = pick;
        }

        float sector = (Mathf.PI * 2f) / count;
        for (int i = 0; i < count; i++)
        {
            float angle = sector * i + Random.Range(-sector * 0.14f, sector * 0.14f);
            bool close = closeThreat[i] || closeThreat[Wrap(i - 1)];
            float dist = close
                ? Random.Range(threatClose, threatFar)
                : Random.Range(normalClose, normalFar);
            corners[i] = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
            idleSeconds[i] = stopHoldSeconds;
            pushedThisStep[i] = false;
            vertexMovedThisStep[i] = false;
        }
    }

    float EstimateVisibleStartDistance()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return maxStartDistance;
        }

        float maxDist = 0f;
        Vector3[] cornersUv =
        {
            new Vector3(0.08f, 0.08f, 0f),
            new Vector3(0.92f, 0.08f, 0f),
            new Vector3(0.08f, 0.92f, 0f),
            new Vector3(0.92f, 0.92f, 0f),
            new Vector3(0.5f, 0.12f, 0f),
            new Vector3(0.5f, 0.88f, 0f),
            new Vector3(0.12f, 0.5f, 0f),
            new Vector3(0.88f, 0.5f, 0f)
        };

        for (int i = 0; i < cornersUv.Length; i++)
        {
            Ray ray = cam.ViewportPointToRay(cornersUv[i]);
            if (Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                continue;
            }

            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
            {
                continue;
            }

            Vector3 hit = ray.origin + ray.direction * t;
            hit.y = 0f;
            maxDist = Mathf.Max(maxDist, Vector3.Distance(center, hit));
        }

        if (maxDist < 1f)
        {
            return maxStartDistance;
        }

        return maxDist * Mathf.Clamp(startViewPadding, 0.5f, 1f);
    }

    bool NeighborsThreat(bool[] closeThreat, int pick, int lastPicked)
    {
        if (count <= 3)
        {
            return false;
        }

        if (lastPicked >= 0 && Mathf.Min(Mathf.Abs(pick - lastPicked), count - Mathf.Abs(pick - lastPicked)) <= 1)
        {
            return true;
        }

        return closeThreat[Wrap(pick - 1)] || closeThreat[Wrap(pick + 1)];
    }

    void SpawnCorners()
    {
        cornerBlocks = new Transform[count];
        cornerBodies = new Rigidbody[count];
        blockRenderers = new Renderer[count];
        PhysicsMaterial noBounce = CreateNoBounceMaterial();

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = BlockPosition(i);
            GameObject prefab = PickRandomTrash();
            Quaternion rotation = TrashSpawnRotation(prefab);
            blockSpin[i] = rotation;
            GameObject spawned = Instantiate(prefab, pos, rotation, worldRoot);
            spawned.name = prefab.name + " Edge " + i;
            spawned.SetActive(true);
            PrepareTrashInstance(spawned, noBounce);

            TrashCube cube = spawned.GetComponent<TrashCube>();
            if (cube == null)
            {
                cube = spawned.AddComponent<TrashCube>();
            }

            cube.Attach(this, i);
            cornerBlocks[i] = spawned.transform;
            cornerBodies[i] = spawned.GetComponent<Rigidbody>();
            blockRenderers[i] = spawned.GetComponentInChildren<Renderer>();
            blockBaseScale[i] = spawned.transform.localScale;
            consumeFadeReady[i] = false;
            ApplyBlockTransform(i, pos, rotation);
        }
    }

    bool HasRandomTrash()
    {
        if (randomTrash == null || randomTrash.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < randomTrash.Length; i++)
        {
            if (randomTrash[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    void RefreshRandomTrashFromFolder()
    {
        List<GameObject> loaded = new List<GameObject>();

#if UNITY_EDITOR
        if (AssetDatabase.IsValidFolder(RandomTrashFolder))
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { RandomTrashFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    loaded.Add(prefab);
                }
            }
        }
#endif

        if (loaded.Count == 0)
        {
            GameObject[] fromResources = Resources.LoadAll<GameObject>("randomTrash");
            if (fromResources != null)
            {
                for (int i = 0; i < fromResources.Length; i++)
                {
                    if (fromResources[i] != null)
                    {
                        loaded.Add(fromResources[i]);
                    }
                }
            }
        }

        if (loaded.Count > 0)
        {
            randomTrash = loaded.ToArray();
        }
    }

    GameObject PickRandomTrash()
    {
        int guard = 0;
        while (guard < 32)
        {
            GameObject pick = randomTrash[Random.Range(0, randomTrash.Length)];
            if (pick != null)
            {
                return pick;
            }

            guard++;
        }

        for (int i = 0; i < randomTrash.Length; i++)
        {
            if (randomTrash[i] != null)
            {
                return randomTrash[i];
            }
        }

        return null;
    }

    void MeasureTrashSizes()
    {
        cubeExtent = 0.5f;
        cubeY = 0.5f;
        for (int i = 0; i < randomTrash.Length; i++)
        {
            GameObject prefab = randomTrash[i];
            if (prefab == null)
            {
                continue;
            }

            float xz;
            float y;
            EstimatePrefabSize(prefab, out xz, out y);
            cubeExtent = Mathf.Max(cubeExtent, xz);
            cubeY = Mathf.Max(cubeY, y);
        }
    }

    static void EstimatePrefabSize(GameObject prefab, out float xzExtent, out float halfHeight)
    {
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            Vector3[] corners = new Vector3[8];
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;
            corners[0] = new Vector3(min.x, min.y, min.z);
            corners[1] = new Vector3(min.x, min.y, max.z);
            corners[2] = new Vector3(min.x, max.y, min.z);
            corners[3] = new Vector3(min.x, max.y, max.z);
            corners[4] = new Vector3(max.x, min.y, min.z);
            corners[5] = new Vector3(max.x, min.y, max.z);
            corners[6] = new Vector3(max.x, max.y, min.z);
            corners[7] = new Vector3(max.x, max.y, max.z);
            for (int c = 0; c < 8; c++)
            {
                Vector3 local = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(corners[c]));
                if (!any)
                {
                    combined = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                {
                    combined.Encapsulate(local);
                }
            }
        }

        if (!any)
        {
            Vector3 scale = prefab.transform.localScale;
            xzExtent = 0.5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            halfHeight = 0.5f * Mathf.Abs(scale.y);
            return;
        }

        xzExtent = 0.5f * Mathf.Max(combined.size.x, combined.size.z);
        halfHeight = Mathf.Max(0.05f, combined.max.y);
        if (halfHeight < 0.05f)
        {
            halfHeight = 0.5f * combined.size.y;
        }
    }

    static void PrepareTrashInstance(GameObject spawned, PhysicsMaterial noBounce)
    {
        if (spawned.GetComponent<Rigidbody>() == null)
        {
            spawned.AddComponent<Rigidbody>();
        }

        Collider[] colliders = spawned.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            BoxCollider box = spawned.AddComponent<BoxCollider>();
            FitBoxColliderToRenderers(spawned, box);
            colliders = spawned.GetComponentsInChildren<Collider>();
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            collider.isTrigger = true;
            if (noBounce != null)
            {
                collider.material = noBounce;
            }
        }
    }

    static void FitBoxColliderToRenderers(GameObject root, BoxCollider box)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            box.size = Vector3.one;
            box.center = Vector3.zero;
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Transform t = root.transform;
        Vector3 localCenter = t.InverseTransformPoint(bounds.center);
        Vector3 localSize = t.InverseTransformVector(bounds.size);
        box.center = localCenter;
        box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
    }

    void SpawnEdgeWalls()
    {
        edgeWalls = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            GameObject wall = new GameObject("TrashPileEdge " + i);
            wall.transform.SetParent(worldRoot, false);
            wall.SetActive(false);

            Rigidbody body = wall.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.material = CreateBarrierMaterial();

            edgeWalls[i] = wall.transform;
        }
    }

    void IgnoreBlockAndWallCollisions()
    {
        for (int i = 0; i < count; i++)
        {
            if (cornerBlocks[i] == null)
            {
                continue;
            }

            Collider[] blockColliders = cornerBlocks[i].GetComponentsInChildren<Collider>();
            for (int b = 0; b < blockColliders.Length; b++)
            {
                Collider blockCollider = blockColliders[b];
                if (blockCollider == null)
                {
                    continue;
                }

                for (int j = 0; j < count; j++)
                {
                    Collider wallCollider = edgeWalls[j].GetComponent<Collider>();
                    if (wallCollider != null)
                    {
                        Physics.IgnoreCollision(blockCollider, wallCollider, true);
                    }
                }
            }
        }
    }

    float CarClearanceRadius()
    {
        float reach = 4f;
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Bounds bounds = colliders[i].bounds;
            Vector3 point = bounds.center;
            point.y = 0f;
            Vector3 extents = bounds.extents;
            float toCenter = Vector3.Distance(center, point);
            float xzExtent = Mathf.Sqrt(extents.x * extents.x + extents.z * extents.z);
            reach = Mathf.Max(reach, toCenter + xzExtent);
        }

        return reach;
    }

    void ApplyIdleOrCreep()
    {
        float target = Mathf.Max(minRadius, borderRadius);
        bool moved = false;

        for (int i = 0; i < count; i++)
        {
            if (vertexMovedThisStep[i] || VertexHeld(i))
            {
                idleSeconds[i] = 0f;
                continue;
            }

            if (idleSeconds[i] < CurrentCreepHoldSeconds())
            {
                idleSeconds[i] += Time.fixedDeltaTime;
                continue;
            }

            Vector3 offset = corners[i] - center;
            offset.y = 0f;
            float dist = offset.magnitude;
            if (dist <= target + 0.001f)
            {
                continue;
            }

            float step = VertexCreepSpeed(i) * Time.fixedDeltaTime;
            float next = Mathf.MoveTowards(dist, target, step);
            corners[i] = center + offset / dist * next;
            ClampCorner(i);
            moved = true;
        }

        if (moved)
        {
            SanitizePolygon();
            dirty = true;
        }
    }

    void ApplyShape()
    {
        dirty = false;
        SanitizePolygon();
        RebuildMesh();
        PlaceCorners();
        PlaceEdgeWalls();
    }

    void PlaceCorners()
    {
        for (int i = 0; i < count; i++)
        {
            if (blockLife != null && blockLife[i] != BlockLife.Placed)
            {
                continue;
            }

            Vector3 pos = BlockPosition(i);
            ApplyBlockTransform(i, pos, BlockSpin(i));
        }
    }

    void PlaceEdgeWalls()
    {
        float height = Mathf.Max(3f, barrierHeight);
        for (int i = 0; i < count; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[Wrap(i + 1)];
            Vector3 delta = b - a;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < 0.05f)
            {
                edgeWalls[i].gameObject.SetActive(false);
                continue;
            }

            float thickness = 1.2f;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 outward = EdgeOutward(i);
            mid += outward * (thickness * 0.5f);
            mid.y = height * 0.5f;
            edgeWalls[i].SetPositionAndRotation(mid, Quaternion.LookRotation(delta.normalized, Vector3.up));
            BoxCollider box = edgeWalls[i].GetComponent<BoxCollider>();
            box.size = new Vector3(thickness, height, length);
            edgeWalls[i].gameObject.SetActive(true);
        }
    }

    void RebuildMesh()
    {
        verts.Clear();
        uvs.Clear();
        tris.Clear();

        float top = Mathf.Max(0.05f, pileHeight);
        float bottom = 0.02f;
        int n = count;
        const int outerCount = 48;

        for (int i = 0; i < n; i++)
        {
            int next = Wrap(i + 1);
            Vector3 innerA = VertexOffset(i);
            Vector3 innerB = VertexOffset(next);
            Vector3 dirA = FlatDir(innerA);
            Vector3 dirB = FlatDir(innerB);
            Vector3 outerA = dirA * coverRadius;
            Vector3 outerB = dirB * coverRadius;

            float angleA = Mathf.Atan2(dirA.z, dirA.x);
            float angleB = Mathf.Atan2(dirB.z, dirB.x);
            float span = RepeatTwoPi(angleB - angleA);

            sectorOuter.Clear();
            for (int k = 0; k < outerCount; k++)
            {
                float ang = (Mathf.PI * 2f * k) / outerCount;
                float t = RepeatTwoPi(ang - angleA);
                if (t <= 0.0001f || t >= span - 0.0001f)
                {
                    continue;
                }

                Vector3 point = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * coverRadius;
                int insertAt = sectorOuter.Count;
                for (int s = 0; s < sectorOuter.Count; s++)
                {
                    float existing = RepeatTwoPi(Mathf.Atan2(sectorOuter[s].z, sectorOuter[s].x) - angleA);
                    if (t < existing)
                    {
                        insertAt = s;
                        break;
                    }
                }

                sectorOuter.Insert(insertAt, point);
            }

            Vector3 innerATop = innerA + Vector3.up * top;
            Vector3 innerBTop = innerB + Vector3.up * top;
            Vector3 prevOuterTop = outerA + Vector3.up * top;
            for (int s = 0; s < sectorOuter.Count; s++)
            {
                Vector3 outerTop = sectorOuter[s] + Vector3.up * top;
                AddTopTri(innerATop, outerTop, prevOuterTop);
                prevOuterTop = outerTop;
            }

            Vector3 outerBTop = outerB + Vector3.up * top;
            AddTopTri(innerATop, outerBTop, prevOuterTop);
            AddTopTri(innerATop, innerBTop, outerBTop);
            AddWall(innerA + Vector3.up * bottom, innerB + Vector3.up * bottom, innerBTop, innerATop);
        }

        pileMesh.Clear();
        pileMesh.SetVertices(verts);
        pileMesh.SetUVs(0, uvs);
        pileMesh.SetTriangles(tris, 0);
        pileMesh.RecalculateNormals();
        pileMesh.RecalculateBounds();
    }

    void AddTopTri(Vector3 a, Vector3 b, Vector3 c)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        uvs.Add(WorldUv(a));
        uvs.Add(WorldUv(b));
        uvs.Add(WorldUv(c));
        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
    }

    static Vector3 FlatDir(Vector3 offset)
    {
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        return offset.normalized;
    }

    void AddWall(Vector3 bottomA, Vector3 bottomB, Vector3 topB, Vector3 topA)
    {
        int start = verts.Count;
        verts.Add(bottomA);
        verts.Add(bottomB);
        verts.Add(topB);
        verts.Add(topA);
        float wallLength = Vector3.Distance(bottomA, bottomB);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(wallLength * 0.15f, 0f));
        uvs.Add(new Vector2(wallLength * 0.15f, 1f));
        uvs.Add(new Vector2(0f, 1f));
        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start);
        tris.Add(start + 2);
        tris.Add(start + 3);
    }

    void ClampCorner(int index)
    {
        ClampRadiusOnly(index);
        KeepNeighborSpacing(index);
        LimitNeighborRadiusDelta(index);
    }

    void SanitizePolygon()
    {
        if (corners == null || corners.Length < 3)
        {
            return;
        }

        // A few passes so radius limits, angle order, and edge length reinforce each other.
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                ClampRadiusOnly(i);
            }

            for (int i = 0; i < count; i++)
            {
                LimitNeighborRadiusDelta(i);
            }

            for (int i = 0; i < count; i++)
            {
                int prev = Wrap(i - 1);
                int next = Wrap(i + 1);
                ConstrainAngleOrder(i, prev, next);
            }

            for (int i = 0; i < count; i++)
            {
                EnsureMinEdgeLength(i);
                KeepEdgeClearOfCenter(i);
            }
        }
    }

    void LimitNeighborRadiusDelta(int index)
    {
        int prev = Wrap(index - 1);
        int next = Wrap(index + 1);
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = Vector3.forward;
        }

        float dist = offset.magnitude;
        Vector3 dir = offset / dist;
        float prevDist = FlatDistanceFromCenter(prev);
        float nextDist = FlatDistanceFromCenter(next);
        float maxDelta = Mathf.Max(0.35f, maxNeighborRadiusDelta);
        float minAllowed = Mathf.Max(minRadius, Mathf.Min(prevDist, nextDist) - maxDelta);
        float maxAllowed = Mathf.Max(prevDist, nextDist) + maxDelta;
        dist = Mathf.Clamp(dist, minAllowed, Mathf.Min(maxAllowed, MaxRadius(dir)));
        corners[index] = center + dir * dist;
    }

    void EnsureMinEdgeLength(int index)
    {
        int next = Wrap(index + 1);
        Vector3 a = corners[index];
        Vector3 b = corners[next];
        a.y = 0f;
        b.y = 0f;
        Vector3 delta = b - a;
        float len = delta.magnitude;
        float minLen = CurrentMinEdgeLength();
        if (len >= minLen)
        {
            return;
        }

        if (len < 0.0001f)
        {
            Vector3 split = Quaternion.Euler(0f, (360f / count) * 0.5f, 0f) * CornerRadial(index);
            corners[index] = center + FlatDir(corners[index] - center) * Mathf.Max(minRadius, FlatDistanceFromCenter(index));
            corners[next] = center + FlatDir(split) * Mathf.Max(minRadius, FlatDistanceFromCenter(next));
            ClampRadiusOnly(index);
            ClampRadiusOnly(next);
            return;
        }

        Vector3 push = delta / len * ((minLen - len) * 0.5f);
        corners[index] -= push;
        corners[next] += push;
        ClampRadiusOnly(index);
        ClampRadiusOnly(next);
        ConstrainAngleOrder(index, Wrap(index - 1), next);
        ConstrainAngleOrder(next, index, Wrap(next + 1));
    }

    void KeepEdgeClearOfCenter(int index)
    {
        int next = Wrap(index + 1);
        Vector3 a = corners[index];
        Vector3 b = corners[next];
        a.y = 0f;
        b.y = 0f;
        float clear = Mathf.Max(minRadius, cubeExtent + 0.35f);
        float dist = DistancePointToSegmentXZ(center, a, b);
        if (dist >= clear)
        {
            return;
        }

        Vector3 mid = (a + b) * 0.5f;
        Vector3 outward = mid - center;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = EdgeOutward(index);
        }

        outward.Normalize();
        float push = clear - dist + 0.05f;
        corners[index] += outward * push;
        corners[next] += outward * push;
        ClampRadiusOnly(index);
        ClampRadiusOnly(next);
    }

    float CurrentCreepHoldSeconds()
    {
        return Mathf.Max(0.05f, stopHoldSeconds);
    }

    float CurrentMinEdgeLength()
    {
        return Mathf.Max(0.75f, CompactorWidth() + minEdgeLengthPadding);
    }

    float CompactorWidth()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 scale = transform.lossyScale;
            float x = Mathf.Abs(box.size.x * scale.x);
            float z = Mathf.Abs(box.size.z * scale.z);
            // Width is the shorter horizontal axis on the truck body.
            return Mathf.Max(0.75f, Mathf.Min(x, z));
        }

        Bounds bounds = TruckBounds();
        return Mathf.Max(0.75f, Mathf.Min(bounds.size.x, bounds.size.z));
    }

    float FlatDistanceFromCenter(int index)
    {
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        return Mathf.Max(minRadius, offset.magnitude);
    }

    void ClampRadiusOnly(int index)
    {
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = Vector3.forward;
        }

        float dist = offset.magnitude;
        Vector3 dir = offset / dist;
        dist = Mathf.Clamp(dist, minRadius, MaxRadius(dir));
        corners[index] = center + dir * dist;
    }

    void KeepNeighborSpacing(int index)
    {
        int prev = Wrap(index - 1);
        int next = Wrap(index + 1);
        ConstrainAngleOrder(index, prev, next);
        ResolveBlockOverlap(index, prev);
        ResolveBlockOverlap(index, next);
        ConstrainAngleOrder(index, prev, next);
    }

    void ConstrainAngleOrder(int index, int prev, int next)
    {
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        float radius = Mathf.Max(minRadius, offset.magnitude);
        float prevAngle = Mathf.Atan2(corners[prev].z - center.z, corners[prev].x - center.x);
        float nextAngle = Mathf.Atan2(corners[next].z - center.z, corners[next].x - center.x);
        float angle = Mathf.Atan2(offset.z, offset.x);
        float span = RepeatTwoPi(nextAngle - prevAngle);
        float fromPrev = RepeatTwoPi(angle - prevAngle);
        float pad = 0.03f;
        if (span <= pad * 2f)
        {
            angle = prevAngle + span * 0.5f;
        }
        else
        {
            fromPrev = Mathf.Clamp(fromPrev, pad, span - pad);
            angle = prevAngle + fromPrev;
        }

        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        radius = Mathf.Clamp(radius, minRadius, MaxRadius(dir));
        corners[index] = center + dir * radius;
    }

    void ResolveBlockOverlap(int index, int other)
    {
        Vector3 a = BlockPosition(index);
        Vector3 b = BlockPosition(other);
        a.y = 0f;
        b.y = 0f;
        Vector3 delta = a - b;
        float minDist = cubeExtent * 2f + blockSeparation;
        float dist = delta.magnitude;
        if (dist >= minDist)
        {
            return;
        }

        if (dist < 0.0001f)
        {
            delta = CornerRadial(index);
            dist = 1f;
        }

        corners[index] += delta / dist * (minDist - dist);
        corners[Wrap(index + 1)] += delta / dist * (minDist - dist);
        ClampRadiusOnly(index);
        ClampRadiusOnly(Wrap(index + 1));
    }

    static float RepeatTwoPi(float angle)
    {
        while (angle < 0f)
        {
            angle += Mathf.PI * 2f;
        }

        while (angle >= Mathf.PI * 2f)
        {
            angle -= Mathf.PI * 2f;
        }

        return angle;
    }

    Vector3 BlockPosition(int index)
    {
        Vector3 outward = EdgeOutward(index);
        Vector3 pos = EdgeMid(index) - outward * BlockInset();
        pos.y = 0f;
        return pos;
    }

    Vector3 EdgeMid(int index)
    {
        Vector3 a = corners[index];
        Vector3 b = corners[Wrap(index + 1)];
        Vector3 mid = (a + b) * 0.5f;
        mid.y = 0f;
        return mid;
    }

    Vector3 EdgeOutward(int index)
    {
        Vector3 a = corners[index];
        Vector3 b = corners[Wrap(index + 1)];
        Vector3 delta = b - a;
        delta.y = 0f;
        Vector3 outward = new Vector3(delta.z, 0f, -delta.x);
        if (outward.sqrMagnitude < 0.0001f)
        {
            return CornerRadial(index);
        }

        outward.Normalize();
        Vector3 fromCenter = EdgeMid(index) - center;
        fromCenter.y = 0f;
        if (Vector3.Dot(outward, fromCenter) < 0f)
        {
            outward = -outward;
        }

        return outward;
    }

    Quaternion FaceEdge(int index)
    {
        Vector3 inward = -EdgeOutward(index);
        if (inward.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(inward, Vector3.up);
    }

    Quaternion BlockSpin(int index)
    {
        if (blockSpin == null || index < 0 || index >= blockSpin.Length)
        {
            return Quaternion.identity;
        }

        return blockSpin[index];
    }

    static Quaternion TrashSpawnRotation(GameObject prefab)
    {
        Vector3 euler = prefab != null ? prefab.transform.rotation.eulerAngles : Vector3.zero;
        return Quaternion.Euler(euler.x, Random.Range(0f, 360f), euler.z);
    }

    static Quaternion WithRandomY(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        return Quaternion.Euler(euler.x, Random.Range(0f, 360f), euler.z);
    }

    float BlockInset()
    {
        return cubeExtent + Mathf.Max(0.05f, blockGap);
    }

    Vector3 CornerRadial(int index)
    {
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        if (offset.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        return offset.normalized;
    }

    Vector3 VertexOffset(int index)
    {
        Vector3 offset = corners[index] - center;
        offset.y = 0f;
        return offset;
    }

    Vector3 OuterAlong(Vector3 innerOffset)
    {
        Vector3 dir = innerOffset;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = Vector3.forward;
        }
        else
        {
            dir.Normalize();
        }

        if (!hasFloorBounds)
        {
            return dir * outerRadius;
        }

        float t = float.PositiveInfinity;
        if (Mathf.Abs(dir.x) > 0.0001f)
        {
            float boundX = dir.x > 0f ? floorMaxX : floorMinX;
            float tx = (boundX - center.x) / dir.x;
            if (tx > 0f)
            {
                t = Mathf.Min(t, tx);
            }
        }

        if (Mathf.Abs(dir.z) > 0.0001f)
        {
            float boundZ = dir.z > 0f ? floorMaxZ : floorMinZ;
            float tz = (boundZ - center.z) / dir.z;
            if (tz > 0f)
            {
                t = Mathf.Min(t, tz);
            }
        }

        if (float.IsInfinity(t))
        {
            return dir * outerRadius;
        }

        return dir * t;
    }

    float MaxRadius(Vector3 dir)
    {
        float floorLimit = OuterAlong(dir).magnitude - 0.75f;
        return Mathf.Min(Mathf.Max(minRadius + 0.1f, floorLimit), maxPushDistance);
    }

    void ClearPushedFlags()
    {
        if (pushedThisStep == null)
        {
            return;
        }

        for (int i = 0; i < pushedThisStep.Length; i++)
        {
            pushedThisStep[i] = false;
            if (vertexMovedThisStep != null)
            {
                vertexMovedThisStep[i] = false;
            }
        }
    }

    int Wrap(int index)
    {
        int wrapped = index % count;
        if (wrapped < 0)
        {
            wrapped += count;
        }

        return wrapped;
    }

    static Vector2 WorldUv(Vector3 local)
    {
        return new Vector2(local.x * 0.08f, local.z * 0.08f);
    }

    Material ResolvePileMaterial()
    {
        if (pileMaterial != null)
        {
            return pileMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        ApplyPileColor(material, pileColor);
        return material;
    }

    static void ApplyPileColor(Material material, Color color)
    {
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }

    static PhysicsMaterial CreateBarrierMaterial()
    {
        return new PhysicsMaterial("TrashPileBarrier")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }

    static PhysicsMaterial CreateNoBounceMaterial()
    {
        return new PhysicsMaterial("TrashPiles")
        {
            dynamicFriction = 0.15f,
            staticFriction = 0.3f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }
}
