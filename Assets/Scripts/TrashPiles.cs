using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
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
    [Header("Fail State")]
    [SerializeField, Min(0.5f), Tooltip("Radius of the invisible circle that follows the truck.")] float dangerZoneRadius = 5.5f;
    [SerializeField, Min(1), Tooltip("How many trash objects must stay inside the circle to start the fail timer.")] int failTrashCount = 3;
    [SerializeField, Min(0.05f), Tooltip("How long 3+ trash objects must remain inside the circle before game over.")] float failHoldSeconds = 2f;
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
    [SerializeField, Tooltip("Inner polygon cannot recede past these decorative piles.")] Transform decorativePiles;
    [SerializeField, Tooltip("Keep the inner wall this far inside the decorative piles.")] float decorativePileInset = 0.75f;

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
    GameObject[] cornerPrefabs;
    PhysicsMaterial trashPhysics;
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
    float[] blockGroundY;
    Quaternion[] blockSpin;
    Vector3[] blockBaseScale;
    bool[] consumeFadeReady;
    Transform[] edgeWalls;
    readonly List<Vector3> verts = new List<Vector3>(2048);
    readonly List<Vector2> uvs = new List<Vector2>(2048);
    readonly List<int> tris = new List<int>(4096);
    readonly List<Vector3> sectorOuter = new List<Vector3>(64);
    static MaterialPropertyBlock sharedFadeBlock;
    Vector3[] decorativeRing;
    float dangerZoneTimer;

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
        dangerZoneRadius = Mathf.Max(0.5f, dangerZoneRadius);
        failTrashCount = Mathf.Max(1, failTrashCount);
        failHoldSeconds = Mathf.Max(0.05f, failHoldSeconds);
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
        decorativePileInset = Mathf.Max(0f, decorativePileInset);
        RefreshRandomTrashFromFolder();
    }

    void FixedUpdate()
    {
        if (count != builtCount)
        {
            BuildWorld();
        }

        if (worldRoot == null || corners == null || !ExperienceRestart.IsActive)
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
        ContainTruckInPlayArea();
        CheckFailFromDangerZone();
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
        ClosestPointOnSegmentXZ(point, a, b, out Vector3 closest);
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 c = new Vector2(closest.x, closest.z);
        return Vector2.Distance(p, c);
    }

    static void ClosestPointOnSegmentXZ(Vector3 point, Vector3 a, Vector3 b, out Vector3 closest)
    {
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);
        Vector2 ab = bb - aa;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f)
        {
            closest = new Vector3(aa.x, 0f, aa.y);
            return;
        }

        float t = Mathf.Clamp01(Vector2.Dot(p - aa, ab) / lenSq);
        Vector2 c = aa + ab * t;
        closest = new Vector3(c.x, 0f, c.y);
    }

    bool TryClosestPlayEdge(Vector3 point, out Vector3 closest, out Vector3 inward)
    {
        closest = point;
        inward = Vector3.zero;
        if (corners == null || corners.Length < 3)
        {
            return false;
        }

        float best = float.PositiveInfinity;
        int bestEdge = -1;
        Vector3 bestPoint = point;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[Wrap(i + 1)];
            ClosestPointOnSegmentXZ(point, a, b, out Vector3 onEdge);
            float dist = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(onEdge.x, onEdge.z));
            if (dist < best)
            {
                best = dist;
                bestEdge = i;
                bestPoint = onEdge;
            }
        }

        if (bestEdge < 0)
        {
            return false;
        }

        closest = bestPoint;
        inward = -EdgeOutward(bestEdge);
        if (inward.sqrMagnitude < 0.0001f)
        {
            inward = center - closest;
            inward.y = 0f;
        }

        if (inward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        inward.Normalize();
        return true;
    }

    void ContainTruckInPlayArea()
    {
        Rigidbody carBody = GetComponent<Rigidbody>();
        if (carBody == null || corners == null || corners.Length < 3)
        {
            return;
        }

        Vector3 correction = Vector3.zero;
        const float pad = 0.18f;
        Vector3 origin = carBody.position;
        origin.y = 0f;
        if (!PointInPlayPolygon(origin))
        {
            AccumulateContainment(origin, pad, ref correction);
        }

        if (carColliders != null)
        {
            for (int w = 0; w < count; w++)
            {
                Vector3 outward = EdgeOutward(w);
                Vector3 far = EdgeMid(w) + outward * 40f;
                far.y = origin.y;
                for (int i = 0; i < carColliders.Length; i++)
                {
                    Collider col = carColliders[i];
                    if (col == null || !col.enabled || col.isTrigger)
                    {
                        continue;
                    }

                    Vector3 support = col.ClosestPoint(far);
                    support.y = 0f;
                    if (!PointInPlayPolygon(support) && DistanceToPlayEdge(support) > 0.55f)
                    {
                        AccumulateContainment(support, pad, ref correction);
                    }
                }
            }
        }

        if (correction.sqrMagnitude < 0.00001f)
        {
            return;
        }

        carBody.MovePosition(carBody.position + correction);
        Vector3 velocity = carBody.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        if (Vector3.Dot(horizontal, correction) < 0f)
        {
            carBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }
    }

    void AccumulateContainment(Vector3 probe, float pad, ref Vector3 correction)
    {
        if (!TryClosestPlayEdge(probe, out Vector3 closest, out Vector3 inward))
        {
            return;
        }

        Vector3 target = closest + inward * pad;
        Vector3 delta = target - probe;
        delta.y = 0f;
        if (delta.sqrMagnitude > correction.sqrMagnitude)
        {
            correction = delta;
        }
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
        if (!ExperienceRestart.IsActive || corners == null || carBody == null)
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

    void CheckFailFromDangerZone()
    {
        if (!ExperienceRestart.IsActive || cornerBlocks == null || blockLife == null)
        {
            dangerZoneTimer = 0f;
            return;
        }

        Vector3 origin = transform.position;
        origin.y = 0f;
        float radius = Mathf.Max(0.5f, dangerZoneRadius);
        float radiusSq = radius * radius;
        int inside = 0;
        int n = Mathf.Min(count, Mathf.Min(cornerBlocks.Length, blockLife.Length));
        for (int i = 0; i < n; i++)
        {
            if (blockLife[i] == BlockLife.Hidden)
            {
                continue;
            }

            Vector3 pos = ActiveBlockPosition(i);
            pos.y = 0f;
            if ((pos - origin).sqrMagnitude <= radiusSq)
            {
                inside++;
            }
        }

        int needed = Mathf.Max(1, failTrashCount);
        if (inside >= needed)
        {
            dangerZoneTimer += Time.fixedDeltaTime;
            if (dangerZoneTimer >= failHoldSeconds)
            {
                ExperienceRestart.NotifyFailed();
            }
        }
        else
        {
            dangerZoneTimer = 0f;
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        origin.y += 0.05f;
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(origin, Mathf.Max(0.5f, dangerZoneRadius));
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

        Collider blockCollider = SolidBlockCollider(cornerBlocks[index]);
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

    void BeginCrawl(int index, bool respinTrash = true)
    {
        ClearBlockContact(index);
        SetBlockInteractable(index, false);
        blockLife[index] = BlockLife.Crawling;
        crawlSpeed[index] = Random.Range(crawlSpeedMin, crawlSpeedMax);
        ResetBlockConsumeVisuals(index);
        crawlPos[index] = CrawlStart(index);
        despawnLeft[index] = -1f;
        hiddenLeft[index] = 0f;
        if (respinTrash)
        {
            RespinTrash(index);
        }
        else
        {
            ApplyBlockTransform(index, crawlPos[index], BlockSpin(index));
        }

        SetBlockVisible(index, true);
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
            BeginCrawl(index, false);
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
        if (cornerBlocks[index] != null)
        {
            blockGroundY[index] = cornerBlocks[index].position.y;
        }

        blockLife[index] = BlockLife.Shoving;
        SetTrashSolidIgnoredByTruck(index, true);
        // Keep the remaining push window so a partial shove still gets ignored/consumed.
        if (despawnLeft[index] <= 0f)
        {
            despawnLeft[index] = Random.Range(pushWindowMin, pushWindowMax);
        }
    }

    void DriveBlockIntoPile(int index, Vector3 drive)
    {
        Vector3 outward = EdgeOutward(index);
        float along = Vector3.Dot(drive, outward);
        if (along <= 0f)
        {
            return;
        }

        Vector3 pos = crawlPos[index];
        pos += outward * (along * Time.fixedDeltaTime);
        pos.y = blockGroundY != null ? blockGroundY[index] : pos.y;
        crawlPos[index] = pos;
        RefreshShoveDepth(index);
        ApplyShovePose(index);
        CheckSwallow(index);
    }

    void RefreshShoveDepth(int index)
    {
        Vector3 mid = EdgeMid(index);
        shoveDepth[index] = Vector3.Dot(crawlPos[index] - mid, EdgeOutward(index));
    }

    void ApplyShovePose(int index)
    {
        Vector3 pos = crawlPos[index];
        pos.y = blockGroundY != null ? blockGroundY[index] : pos.y;
        ApplyBlockTransform(index, pos, BlockSpin(index));
    }

    void CheckSwallow(int index)
    {
        if (blockLife[index] != BlockLife.Shoving)
        {
            return;
        }

        RefreshShoveDepth(index);
        if (shoveDepth[index] >= SwallowDepth())
        {
            QueueRecede(index, finishPush);
            BeginHidden(index);
        }
    }

    float SwallowDepth()
    {
        return Mathf.Max(0.12f, cubeExtent * 0.18f);
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
        if (!pushedThisStep[index])
        {
            Rigidbody carBody = GetComponent<Rigidbody>();
            if (carBody != null && PlayerStillDrivingInto(index, carBody))
            {
                pushedThisStep[index] = true;
                DriveBlockIntoPile(index, DriveFromCar(carBody));
            }
        }

        despawnLeft[index] -= dt;
        if (despawnLeft[index] <= 0f)
        {
            BeginConsume(index);
        }
    }

    bool PlayerStillDrivingInto(int index, Rigidbody carBody)
    {
        CarController car = carBody.GetComponent<CarController>();
        if (car != null && car.IsArmRaised)
        {
            return false;
        }

        Vector3 drive = DriveFromCar(carBody);
        if (drive.sqrMagnitude <= pushSpeedThreshold * pushSpeedThreshold)
        {
            return false;
        }

        if (Vector3.Dot(drive, EdgeOutward(index)) <= 0.05f)
        {
            return false;
        }

        return overlapCount != null && overlapCount[index] > 0;
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

        Vector3 placed = pos;
        bool shoving = blockLife != null && index < blockLife.Length && blockLife[index] == BlockLife.Shoving;
        if (!shoving)
        {
            placed.y = pos.y;
            cornerBlocks[index].SetPositionAndRotation(placed, rotation);
            placed.y = SnapSitY(cornerBlocks[index], pos.y);
        }

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
        SetTrashSolidIgnoredByTruck(index, false);
    }

    void SetTrashSolidIgnoredByTruck(int index, bool ignore)
    {
        if (cornerBlocks == null || index < 0 || index >= cornerBlocks.Length || cornerBlocks[index] == null)
        {
            return;
        }

        if (carColliders == null)
        {
            return;
        }

        Collider[] trashColliders = cornerBlocks[index].GetComponentsInChildren<Collider>(true);
        for (int t = 0; t < trashColliders.Length; t++)
        {
            Collider trashCollider = trashColliders[t];
            if (trashCollider == null || trashCollider.isTrigger)
            {
                continue;
            }

            for (int c = 0; c < carColliders.Length; c++)
            {
                Collider carCollider = carColliders[c];
                if (carCollider == null || carCollider.isTrigger)
                {
                    continue;
                }

                Physics.IgnoreCollision(carCollider, trashCollider, ignore);
            }
        }
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

        CacheDecorativePileRing();

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
        blockGroundY = new float[count];
        blockSpin = new Quaternion[count];
        blockBaseScale = new Vector3[count];
        consumeFadeReady = new bool[count];
        cornerPrefabs = new GameObject[count];
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
        if (cornerPrefabs == null || cornerPrefabs.Length != count)
        {
            cornerPrefabs = new GameObject[count];
        }

        trashPhysics = CreateNoBounceMaterial();
        GameObject[] deck = ShuffledTrashDeck();
        int deckIndex = 0;

        for (int i = 0; i < count; i++)
        {
            if (deck.Length == 0)
            {
                break;
            }

            if (deckIndex >= deck.Length)
            {
                deck = ShuffledTrashDeck();
                deckIndex = 0;
            }

            GameObject prefab = deck[deckIndex++];
            Vector3 pos = BlockPosition(i);
            Quaternion rotation = TrashSpawnRotation(prefab);
            SpawnCornerInstance(i, prefab, pos, rotation);
        }
    }

    void RespinTrash(int index)
    {
        GameObject prefab = PickRandomTrash(index);
        if (prefab == null)
        {
            return;
        }

        Vector3 pos = crawlPos != null ? crawlPos[index] : BlockPosition(index);
        Quaternion rotation = TrashSpawnRotation(prefab);
        SpawnCornerInstance(index, prefab, pos, rotation);
    }

    void SpawnCornerInstance(int index, GameObject prefab, Vector3 pos, Quaternion rotation)
    {
        if (prefab == null)
        {
            return;
        }

        if (cornerBlocks != null && index >= 0 && index < cornerBlocks.Length && cornerBlocks[index] != null)
        {
            Destroy(cornerBlocks[index].gameObject);
            cornerBlocks[index] = null;
            cornerBodies[index] = null;
            blockRenderers[index] = null;
        }

        if (trashPhysics == null)
        {
            trashPhysics = CreateNoBounceMaterial();
        }

        GameObject spawned = Instantiate(prefab, pos, rotation, worldRoot);
        spawned.name = prefab.name + " Edge " + index;
        spawned.SetActive(true);
        PrepareTrashInstance(spawned, trashPhysics);

        TrashCube cube = spawned.GetComponent<TrashCube>();
        if (cube == null)
        {
            cube = spawned.AddComponent<TrashCube>();
        }

        cube.Attach(this, index);
        cornerBlocks[index] = spawned.transform;
        cornerBodies[index] = spawned.GetComponent<Rigidbody>();
        blockRenderers[index] = spawned.GetComponentInChildren<Renderer>();
        if (blockBaseScale != null)
        {
            blockBaseScale[index] = spawned.transform.localScale;
        }

        if (consumeFadeReady != null)
        {
            consumeFadeReady[index] = false;
        }

        if (blockSpin != null)
        {
            blockSpin[index] = rotation;
        }

        if (cornerPrefabs != null && index < cornerPrefabs.Length)
        {
            cornerPrefabs[index] = prefab;
        }

        IgnoreWallsForBlock(index);
        ApplyBlockTransform(index, pos, rotation);
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
        HashSet<string> seen = new HashSet<string>();

#if UNITY_EDITOR
        LoadTrashPrefabsFromEditorFolder(loaded, seen);
#endif

        if (loaded.Count == 0)
        {
            GameObject[] fromResources = Resources.LoadAll<GameObject>("randomTrash");
            if (fromResources != null)
            {
                for (int i = 0; i < fromResources.Length; i++)
                {
                    AddUniqueTrash(loaded, seen, fromResources[i]);
                }
            }
        }

        if (loaded.Count == 0 && randomTrash != null)
        {
            for (int i = 0; i < randomTrash.Length; i++)
            {
                AddUniqueTrash(loaded, seen, randomTrash[i]);
            }
        }

        if (loaded.Count > 0)
        {
            loaded.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            randomTrash = loaded.ToArray();
        }
    }

#if UNITY_EDITOR
    static void LoadTrashPrefabsFromEditorFolder(List<GameObject> loaded, HashSet<string> seen)
    {
        if (!AssetDatabase.IsValidFolder(RandomTrashFolder) && !Directory.Exists(RandomTrashFolder))
        {
            return;
        }

        if (Directory.Exists(RandomTrashFolder))
        {
            string[] files = Directory.GetFiles(RandomTrashFolder, "*.prefab", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                AddUniqueTrash(loaded, seen, prefab);
            }
        }

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
                AddUniqueTrash(loaded, seen, prefab);
            }
        }
    }
#endif

    static void AddUniqueTrash(List<GameObject> loaded, HashSet<string> seen, GameObject prefab)
    {
        if (prefab == null || seen == null || loaded == null)
        {
            return;
        }

        string key = prefab.name;
        if (!seen.Add(key))
        {
            return;
        }

        loaded.Add(prefab);
    }

    GameObject[] ValidTrashPrefabs()
    {
        if (randomTrash == null || randomTrash.Length == 0)
        {
            return System.Array.Empty<GameObject>();
        }

        List<GameObject> valid = new List<GameObject>(randomTrash.Length);
        HashSet<GameObject> seen = new HashSet<GameObject>();
        for (int i = 0; i < randomTrash.Length; i++)
        {
            GameObject prefab = randomTrash[i];
            if (prefab != null && seen.Add(prefab))
            {
                valid.Add(prefab);
            }
        }

        return valid.ToArray();
    }

    GameObject[] ShuffledTrashDeck()
    {
        GameObject[] deck = ValidTrashPrefabs();
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int swap = Random.Range(0, i + 1);
            GameObject temp = deck[i];
            deck[i] = deck[swap];
            deck[swap] = temp;
        }

        return deck;
    }

    GameObject PickRandomTrash(int forIndex = -1)
    {
        GameObject[] pool = ValidTrashPrefabs();
        if (pool.Length == 0)
        {
            return null;
        }

        List<GameObject> unused = new List<GameObject>(pool.Length);
        for (int i = 0; i < pool.Length; i++)
        {
            if (!TrashPrefabInUse(pool[i], forIndex))
            {
                unused.Add(pool[i]);
            }
        }

        GameObject[] pickFrom = unused.Count > 0 ? unused.ToArray() : pool;
        return pickFrom[Random.Range(0, pickFrom.Length)];
    }

    bool TrashPrefabInUse(GameObject prefab, int ignoreIndex)
    {
        if (prefab == null || cornerPrefabs == null)
        {
            return false;
        }

        for (int i = 0; i < cornerPrefabs.Length; i++)
        {
            if (i == ignoreIndex || cornerPrefabs[i] != prefab)
            {
                continue;
            }

            if (blockLife != null && i < blockLife.Length && blockLife[i] == BlockLife.Hidden)
            {
                continue;
            }

            return true;
        }

        return false;
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

        ReplaceWithBoxCollider(spawned, noBounce);
        AddPushSensor(spawned);
    }

    static void ReplaceWithBoxCollider(GameObject spawned, PhysicsMaterial noBounce)
    {
        BoxCollider box = spawned.GetComponent<BoxCollider>();
        Collider[] existing = spawned.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            Collider collider = existing[i];
            if (collider == null || collider == box || collider.isTrigger)
            {
                continue;
            }

            collider.enabled = false;
            Object.Destroy(collider);
        }

        if (box == null)
        {
            box = spawned.AddComponent<BoxCollider>();
        }

        FitBoxColliderToRenderers(spawned, box);
        box.enabled = true;
        box.isTrigger = false;
        if (noBounce != null)
        {
            box.material = noBounce;
        }
    }

    static Collider SolidBlockCollider(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.enabled && !collider.isTrigger)
            {
                return collider;
            }
        }

        return null;
    }

    static void AddPushSensor(GameObject spawned)
    {
        Transform existing = spawned.transform.Find("PushSensor");
        if (existing != null)
        {
            return;
        }

        GameObject sensor = new GameObject("PushSensor");
        sensor.transform.SetParent(spawned.transform, false);
        sensor.layer = spawned.layer;

        BoxCollider box = sensor.AddComponent<BoxCollider>();
        box.isTrigger = true;

        BoxCollider source = spawned.GetComponent<BoxCollider>();
        if (source != null)
        {
            box.center = source.center;
            box.size = source.size;
        }
        else
        {
            FitBoxColliderToRenderers(spawned, box);
        }

        // Thin floor items (forks, spoons) sit under the bumper. Grow the
        // trigger up so the truck can still register a shove.
        GrowBoxAlongWorldUp(box, 0.85f);
        box.size = PaddedLocalSize(box.size, spawned.transform.lossyScale);
    }

    static void GrowBoxAlongWorldUp(BoxCollider box, float minWorldHeight)
    {
        if (box == null || minWorldHeight <= 0f)
        {
            return;
        }

        Transform t = box.transform;
        Vector3 size = box.size;
        Vector3 center = box.center;
        Vector3 axisX = t.TransformVector(new Vector3(size.x, 0f, 0f));
        Vector3 axisY = t.TransformVector(new Vector3(0f, size.y, 0f));
        Vector3 axisZ = t.TransformVector(new Vector3(0f, 0f, size.z));
        float worldHeight = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        if (worldHeight >= minWorldHeight)
        {
            return;
        }

        Vector3 localUp = t.InverseTransformDirection(Vector3.up);
        Vector3 abs = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
        Vector3 localAxis = Vector3.up;
        if (abs.x >= abs.y && abs.x >= abs.z)
        {
            localAxis = Vector3.right;
        }
        else if (abs.z >= abs.x && abs.z >= abs.y)
        {
            localAxis = Vector3.forward;
        }

        float worldYPerLocal = Mathf.Abs(t.TransformVector(localAxis).y);
        if (worldYPerLocal < 0.0001f)
        {
            return;
        }

        float extraLocal = (minWorldHeight - worldHeight) / worldYPerLocal;
        size += localAxis * extraLocal;
        float sign = Vector3.Dot(localAxis, localUp) >= 0f ? 1f : -1f;
        center += localAxis * (sign * extraLocal * 0.5f);
        box.size = size;
        box.center = center;
    }

    static Vector3 PaddedLocalSize(Vector3 localSize, Vector3 lossyScale)
    {
        return new Vector3(
            localSize.x + WorldPaddingToLocal(0.05f, lossyScale.x),
            localSize.y + WorldPaddingToLocal(0.03f, lossyScale.y),
            localSize.z + WorldPaddingToLocal(0.05f, lossyScale.z));
    }

    static float WorldPaddingToLocal(float worldPadding, float axisScale)
    {
        return worldPadding / Mathf.Max(0.0001f, Mathf.Abs(axisScale));
    }

    static void FitBoxColliderToRenderers(GameObject root, BoxCollider box)
    {
        Transform t = root.transform;
        bool any = false;
        Bounds local = new Bounds(Vector3.zero, Vector3.zero);

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            EncapsulateWorldCorners(t, filter.transform, filter.sharedMesh.bounds, ref local, ref any);
        }

        if (!any)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                EncapsulateWorldCorners(t, renderer.transform, renderer.localBounds, ref local, ref any);
            }
        }

        if (!any)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one;
            return;
        }

        box.center = local.center;
        box.size = new Vector3(
            Mathf.Abs(local.size.x),
            Mathf.Abs(local.size.y),
            Mathf.Abs(local.size.z));
    }

    static void EncapsulateWorldCorners(Transform root, Transform source, Bounds bounds, ref Bounds local, ref bool any)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int z = 0; z < 2; z++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Vector3 corner = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 localPoint = root.InverseTransformPoint(source.TransformPoint(corner));
                    if (!any)
                    {
                        local = new Bounds(localPoint, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(localPoint);
                    }
                }
            }
        }
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
            IgnoreWallsForBlock(i);
        }
    }

    void IgnoreWallsForBlock(int index)
    {
        if (cornerBlocks == null || edgeWalls == null || index < 0 || index >= cornerBlocks.Length)
        {
            return;
        }

        if (cornerBlocks[index] == null)
        {
            return;
        }

        Collider[] blockColliders = cornerBlocks[index].GetComponentsInChildren<Collider>();
        for (int b = 0; b < blockColliders.Length; b++)
        {
            Collider blockCollider = blockColliders[b];
            if (blockCollider == null)
            {
                continue;
            }

            for (int j = 0; j < edgeWalls.Length; j++)
            {
                if (edgeWalls[j] == null)
                {
                    continue;
                }

                Collider wallCollider = edgeWalls[j].GetComponent<Collider>();
                if (wallCollider != null)
                {
                    Physics.IgnoreCollision(blockCollider, wallCollider, true);
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
        // Extend well below the floor so PhysX never picks Y as the shortest
        // depenetration axis when the truck is squeezed by closing walls.
        const float buried = 40f;
        float colliderHeight = height + buried;
        const float thickness = 6f;
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

            Vector3 mid = (a + b) * 0.5f;
            Vector3 outward = EdgeOutward(i);
            mid += outward * (thickness * 0.5f);
            mid.y = (height - buried) * 0.5f;
            Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            Rigidbody body = edgeWalls[i].GetComponent<Rigidbody>();
            bool wasActive = edgeWalls[i].gameObject.activeSelf;
            if (!wasActive)
            {
                edgeWalls[i].SetPositionAndRotation(mid, rotation);
                edgeWalls[i].gameObject.SetActive(true);
            }
            else if (body != null)
            {
                body.MovePosition(mid);
                body.MoveRotation(rotation);
            }
            else
            {
                edgeWalls[i].SetPositionAndRotation(mid, rotation);
            }

            BoxCollider box = edgeWalls[i].GetComponent<BoxCollider>();
            box.size = new Vector3(thickness, colliderHeight, length + 0.8f);
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
        float cap = Mathf.Min(Mathf.Max(minRadius + 0.1f, floorLimit), maxPushDistance);
        float decorative = DecorativePileRadius(dir);
        if (decorative < cap)
        {
            cap = Mathf.Max(minRadius + 0.1f, decorative);
        }

        return cap;
    }

    void CacheDecorativePileRing()
    {
        decorativeRing = null;
        Transform root = decorativePiles;
        if (root == null)
        {
            GameObject found = GameObject.Find("RoundDecorativePiles");
            if (found != null)
            {
                root = found.transform;
                decorativePiles = root;
            }
        }

        if (root == null)
        {
            return;
        }

        List<Vector3> points = new List<Vector3>(root.childCount);
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Renderer[] childRenderers = child.GetComponentsInChildren<Renderer>();
            if (childRenderers == null || childRenderers.Length == 0)
            {
                continue;
            }

            Vector3 inner = Vector3.zero;
            float bestSqr = float.PositiveInfinity;
            for (int r = 0; r < childRenderers.Length; r++)
            {
                Renderer renderer = childRenderers[r];
                if (renderer == null)
                {
                    continue;
                }

                Vector3 closest = ClosestPointOnRendererBoundsXZ(renderer, center);
                float sqr = (closest - center).sqrMagnitude;
                if (sqr < 0.25f || sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                inner = closest;
            }

            if (float.IsInfinity(bestSqr))
            {
                continue;
            }

            points.Add(inner);
        }

        if (points.Count < 3)
        {
            return;
        }

        points.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.z - center.z, a.x - center.x);
            float angleB = Mathf.Atan2(b.z - center.z, b.x - center.x);
            return angleA.CompareTo(angleB);
        });
        decorativeRing = points.ToArray();
    }

    float DecorativePileRadius(Vector3 dir)
    {
        if (decorativeRing == null || decorativeRing.Length < 3)
        {
            return maxPushDistance;
        }

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return maxPushDistance;
        }

        dir.Normalize();
        float best = float.PositiveInfinity;
        for (int i = 0; i < decorativeRing.Length; i++)
        {
            Vector3 a = decorativeRing[i];
            Vector3 b = decorativeRing[(i + 1) % decorativeRing.Length];
            float hit;
            if (!RayHitsSegmentXZ(center, dir, a, b, out hit))
            {
                continue;
            }

            if (hit < best)
            {
                best = hit;
            }
        }

        if (float.IsInfinity(best))
        {
            return maxPushDistance;
        }

        return Mathf.Max(0.1f, best - decorativePileInset);
    }

    static bool RayHitsSegmentXZ(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, out float distance)
    {
        distance = 0f;
        Vector3 ao = a - origin;
        Vector3 ab = b - a;
        dir.y = 0f;
        ao.y = 0f;
        ab.y = 0f;
        if (dir.sqrMagnitude < 0.0000001f)
        {
            return false;
        }

        dir.Normalize();
        float denom = dir.x * ab.z - dir.z * ab.x;
        if (Mathf.Abs(denom) < 0.000001f)
        {
            return false;
        }

        float t = (ao.x * ab.z - ao.z * ab.x) / denom;
        float s = (ao.x * dir.z - ao.z * dir.x) / denom;
        if (t <= 0.0001f || s < 0f || s > 1f)
        {
            return false;
        }

        distance = t;
        return true;
    }

    static Vector3 ClosestPointOnRendererBoundsXZ(Renderer renderer, Vector3 worldPoint)
    {
        Transform tf = renderer.transform;
        Vector3 local = tf.InverseTransformPoint(worldPoint);
        Bounds box = renderer.localBounds;
        local.x = Mathf.Clamp(local.x, box.min.x, box.max.x);
        local.y = Mathf.Clamp(local.y, box.min.y, box.max.y);
        local.z = Mathf.Clamp(local.z, box.min.z, box.max.z);
        Vector3 closest = tf.TransformPoint(local);
        closest.y = 0f;
        return closest;
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
            dynamicFriction = 0.45f,
            staticFriction = 0.6f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }
}
