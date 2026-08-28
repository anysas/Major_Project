using System.Collections.Generic;
using UnityEngine;

public class TrashPiles : MonoBehaviour
{
    [Header("Trash Piles")]
    [SerializeField] GameObject trashPrefab;
    [SerializeField] Transform border;
    [SerializeField, Min(3), Tooltip("Number of edges on the empty inner polygon.")] int count = 8;
    [SerializeField, Tooltip("Typical starting distance from the circle for blocks that sit further out.")] float radius = 12f;
    [SerializeField] float extraClearance = 2f;
    [SerializeField] float pileHeight = 0.8f;
    [SerializeField] float barrierHeight = 8f;
    [SerializeField, Tooltip("Empty space between each block and the polygon inner edge.")] float blockGap = 0.45f;
    [SerializeField, Tooltip("Minimum extra space kept between neighboring blocks.")] float blockSeparation = 0.85f;
    [SerializeField, Tooltip("How much farther from the circle blocks start, so the player can read the layout.")] float startBackOffset = 8f;
    [SerializeField, Min(0), Tooltip("How many blocks start much closer to the circle than the rest.")] int closeThreatCount = 2;
    [SerializeField, Tooltip("How much farther from the circle those closest walls start, so a falling block does not land on the border.")] float closeThreatBackOffset = 3.5f;
    [SerializeField, Tooltip("Maximum distance from the circle a block can be pushed. Still cannot leave the floor.")] float maxPushDistance = 70f;
    [SerializeField] float creepSpeed = 0.8f;
    [SerializeField, Range(0.05f, 1f), Tooltip("How fast a wall creeps when no trash block is sitting on it.")] float emptyWallCreepScale = 0.4f;
    [SerializeField, Tooltip("Delay between each wall's first drop, closest to the border first.")] float firstFallStagger = 0.35f;
    [SerializeField, Range(0f, 0.95f), Tooltip("How far along the crawl the closest walls start, so they drop first.")] float closestHeadStart = 0.72f;
    [SerializeField] float stopHoldSeconds = 2.5f;
    [SerializeField] float pushSpeedThreshold = 0.25f;
    [SerializeField, Tooltip("How much farther the outermost starting blocks sit from the closest ones.")] float startSpread = 5.5f;
    [SerializeField] Material pileMaterial;
    [SerializeField] Color pileColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField, Tooltip("How far the trash floor extends past the real floor so corners stay covered.")] float coverPadding = 70f;
    [SerializeField] float crawlSpeedMin = 1.5f;
    [SerializeField] float crawlSpeedMax = 1.7f;
    [SerializeField] float despawnDelayMin = 1f;
    [SerializeField] float despawnDelayMax = 2f;
    [SerializeField] float respawnDelayMin = 0.5f;
    [SerializeField] float respawnDelayMax = 1.25f;
    [SerializeField] float fallDuration = 0.45f;

    enum BlockLife
    {
        Crawling,
        Falling,
        Placed,
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
    Transform[] edgeWalls;
    readonly List<Vector3> verts = new List<Vector3>(2048);
    readonly List<Vector2> uvs = new List<Vector2>(2048);
    readonly List<int> tris = new List<int>(4096);
    readonly List<Vector3> sectorOuter = new List<Vector3>(64);

    void Start()
    {
        BuildWorld();
    }

    void OnValidate()
    {
        count = Mathf.Max(3, count);
        pileHeight = Mathf.Max(0.05f, pileHeight);
        barrierHeight = Mathf.Max(pileHeight, barrierHeight);
        blockGap = Mathf.Max(0.05f, blockGap);
        blockSeparation = Mathf.Max(0.1f, blockSeparation);
        startBackOffset = Mathf.Max(0f, startBackOffset);
        closeThreatCount = Mathf.Clamp(closeThreatCount, 0, 8);
        closeThreatBackOffset = Mathf.Max(0f, closeThreatBackOffset);
        maxPushDistance = Mathf.Max(1f, maxPushDistance);
        coverPadding = Mathf.Max(10f, coverPadding);
        startSpread = Mathf.Max(0.5f, startSpread);
        emptyWallCreepScale = Mathf.Clamp(emptyWallCreepScale, 0.05f, 1f);
        firstFallStagger = Mathf.Max(0f, firstFallStagger);
        closestHeadStart = Mathf.Clamp01(closestHeadStart);
        crawlSpeedMin = Mathf.Max(0.1f, crawlSpeedMin);
        crawlSpeedMax = Mathf.Max(crawlSpeedMin, crawlSpeedMax);
        despawnDelayMin = Mathf.Max(0.1f, despawnDelayMin);
        despawnDelayMax = Mathf.Max(despawnDelayMin, despawnDelayMax);
        respawnDelayMin = Mathf.Max(0f, respawnDelayMin);
        respawnDelayMax = Mathf.Max(respawnDelayMin, respawnDelayMax);
        fallDuration = Mathf.Max(0.05f, fallDuration);
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
        ApplyIdleOrCreep();
        UpdateBlockLife();
        if (dirty)
        {
            ApplyShape();
        }

        ApplyBlockVisuals();
    }

    public bool IsBlockPushable(int index)
    {
        return blockLife != null && index >= 0 && index < blockLife.Length && blockLife[index] == BlockLife.Placed;
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

        SetCornerWallsIgnored(index, overlapCount[index] > 0);
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

        if (pushedThisStep[index])
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
        if (intoPush <= 0f && intoOut <= 0f)
        {
            return;
        }

        pushedThisStep[index] = true;
        if (despawnLeft[index] < 0f)
        {
            despawnLeft[index] = Random.Range(despawnDelayMin, despawnDelayMax);
        }

        MoveEdge(index, drive * Time.fixedDeltaTime);
        dirty = true;
        ApplyShape();
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

        Collider blockCollider = cornerBlocks[index].GetComponent<Collider>();
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
                    if (despawnLeft[i] >= 0f)
                    {
                        despawnLeft[i] -= dt;
                        if (despawnLeft[i] <= 0f)
                        {
                            BeginHidden(i);
                        }
                    }

                    break;
                case BlockLife.Hidden:
                    hiddenLeft[i] -= dt;
                    if (hiddenLeft[i] <= 0f)
                    {
                        BeginCrawl(i);
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
        crawlPos[index] = CrawlStart(index);
        despawnLeft[index] = -1f;
        hiddenLeft[index] = 0f;
        ApplyBlockTransform(index, crawlPos[index], FaceAlong(EdgeOutward(index) * -1f));
    }

    void StartOpeningCrawls()
    {
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        for (int a = 0; a < count - 1; a++)
        {
            int best = a;
            for (int b = a + 1; b < count; b++)
            {
                if (WallDistance(order[b]) < WallDistance(order[best]))
                {
                    best = b;
                }
            }

            int swap = order[a];
            order[a] = order[best];
            order[best] = swap;
        }

        float stagger = Mathf.Max(0f, firstFallStagger);
        float head = Mathf.Clamp01(closestHeadStart);
        for (int rank = 0; rank < count; rank++)
        {
            int index = order[rank];
            float closeness = count <= 1 ? 1f : 1f - rank / (float)(count - 1);
            openingHeadStart[index] = closeness * head;
            crawlHold[index] = rank * stagger;
            BeginCrawl(index);
        }
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
        despawnLeft[index] = -1f;
        SetBlockInteractable(index, true);
        SetBlockVisible(index, true);
        Vector3 pos = BlockPosition(index);
        ApplyBlockTransform(index, pos, FaceEdge(index));
    }

    void BeginHidden(int index)
    {
        ClearBlockContact(index);
        SetBlockInteractable(index, false);
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
                ApplyBlockTransform(index, crawlPos[index], FaceAlong(-EdgeOutward(index)));
                return;
            }
        }

        Vector3 target = CrawlLip(index);
        crawlPos[index] = Vector3.MoveTowards(crawlPos[index], target, crawlSpeed[index] * dt);
        Vector3 travel = target - crawlPos[index];
        travel.y = 0f;
        Quaternion rotation = travel.sqrMagnitude > 0.0001f
            ? FaceAlong(travel)
            : FaceAlong(-EdgeOutward(index));
        ApplyBlockTransform(index, crawlPos[index], rotation);
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
        Quaternion rotation = Quaternion.Slerp(FaceAlong(-EdgeOutward(index)), FaceEdge(index), t);
        ApplyBlockTransform(index, pos, rotation);
        if (t >= 1f)
        {
            BeginPlaced(index);
        }
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
                Vector3 pos = BlockPosition(i);
                ApplyBlockTransform(i, pos, FaceEdge(i));
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
        return blockLife != null && index >= 0 && index < blockLife.Length && blockLife[index] == BlockLife.Placed;
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

    Vector3 CrawlLip(int index)
    {
        Vector3 lip = EdgeMid(index) + EdgeOutward(index) * cubeExtent;
        lip.y = PileTopY();
        return lip;
    }

    float PileTopY()
    {
        return Mathf.Max(0.05f, pileHeight) + cubeY;
    }

    Quaternion FaceAlong(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    void ApplyBlockTransform(int index, Vector3 pos, Quaternion rotation)
    {
        if (cornerBlocks == null || cornerBlocks[index] == null)
        {
            return;
        }

        cornerBlocks[index].SetPositionAndRotation(pos, rotation);
        if (cornerBodies[index] != null)
        {
            cornerBodies[index].position = pos;
            cornerBodies[index].rotation = rotation;
        }
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

        Collider blockCollider = cornerBlocks[index].GetComponent<Collider>();
        if (blockCollider != null)
        {
            blockCollider.enabled = interactable;
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

        if (trashPrefab == null)
        {
            trashPrefab = GameObject.Find("Trash_prefab");
        }

        if (border == null)
        {
            GameObject borderObject = GameObject.Find("Border");
            if (borderObject != null)
            {
                border = borderObject.transform;
            }
        }

        Transform floor = null;
        GameObject floorObject = GameObject.Find("Floor");
        if (floorObject != null)
        {
            floor = floorObject.transform;
        }

        if (trashPrefab == null)
        {
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

        cubeExtent = 0.5f * Mathf.Max(trashPrefab.transform.localScale.x, trashPrefab.transform.localScale.z);
        cubeY = trashPrefab.transform.localScale.y * 0.5f;
        minRadius = Mathf.Max(1.5f, cubeExtent + 0.5f);
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
        HideTemplate();

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
        float threatClose = safeMin + closeThreatBackOffset;
        float threatFar = threatClose + 2.5f;
        float normalClose = safeMin + startBackOffset;
        float normalFar = normalClose + startSpread * 1.5f;
        safeMin = Mathf.Min(safeMin, maxPushDistance);
        threatClose = Mathf.Min(Mathf.Max(safeMin + 0.35f, threatClose), maxPushDistance);
        threatFar = Mathf.Min(Mathf.Max(threatClose + 0.35f, threatFar), maxPushDistance);
        normalClose = Mathf.Min(Mathf.Max(threatFar + 0.75f, normalClose), maxPushDistance);
        normalFar = Mathf.Min(Mathf.Max(normalClose + 0.75f, normalFar), maxPushDistance);

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
            Quaternion rotation = FaceEdge(i);
            GameObject spawned = Instantiate(trashPrefab, pos, rotation, worldRoot);
            spawned.name = "TrashEdge " + i;
            spawned.SetActive(true);

            Collider spawnedCollider = spawned.GetComponent<Collider>();
            if (spawnedCollider != null)
            {
                spawnedCollider.material = noBounce;
            }

            TrashCube cube = spawned.GetComponent<TrashCube>();
            if (cube == null)
            {
                cube = spawned.AddComponent<TrashCube>();
            }

            cube.Attach(this, i);
            cornerBlocks[i] = spawned.transform;
            cornerBodies[i] = spawned.GetComponent<Rigidbody>();
            blockRenderers[i] = spawned.GetComponentInChildren<Renderer>();
            if (cornerBodies[i] != null)
            {
                cornerBodies[i].position = pos;
                cornerBodies[i].rotation = rotation;
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
            Collider blockCollider = cornerBlocks[i].GetComponent<Collider>();
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

    void HideTemplate()
    {
        if (trashPrefab == null || trashPrefab == gameObject)
        {
            return;
        }

        if (trashPrefab.GetComponent<CarController>() != null)
        {
            return;
        }

        trashPrefab.SetActive(false);
    }

    void ApplyIdleOrCreep()
    {
        float target = Mathf.Max(minRadius, borderRadius);
        bool moved = false;

        for (int i = 0; i < count; i++)
        {
            if (vertexMovedThisStep[i])
            {
                idleSeconds[i] = 0f;
                continue;
            }

            if (idleSeconds[i] < stopHoldSeconds)
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
            moved = true;
        }

        if (moved)
        {
            dirty = true;
        }
    }

    void ApplyShape()
    {
        dirty = false;
        RebuildMesh();
        PlaceCorners();
        PlaceEdgeWalls();
        CheckBorderReached();
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
            Quaternion rotation = FaceEdge(i);
            cornerBlocks[i].SetPositionAndRotation(pos, rotation);
            if (cornerBodies[i] != null)
            {
                cornerBodies[i].position = pos;
                cornerBodies[i].rotation = rotation;
            }
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

    void CheckBorderReached()
    {
        if (border == null)
        {
            return;
        }

        Vector3 borderCenter = new Vector3(border.position.x, 0f, border.position.z);
        for (int i = 0; i < count; i++)
        {
            if (!IsBlockPushable(i))
            {
                continue;
            }

            Vector3 pos = BlockPosition(i);
            pos.y = 0f;
            if ((pos - borderCenter).magnitude <= borderRadius + cubeExtent)
            {
                ExperienceRestart.NotifyBorderTouched();
                return;
            }
        }
    }

    Vector3 BlockPosition(int index)
    {
        Vector3 outward = EdgeOutward(index);
        Vector3 pos = EdgeMid(index) - outward * BlockInset();
        pos.y = cubeY;
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
