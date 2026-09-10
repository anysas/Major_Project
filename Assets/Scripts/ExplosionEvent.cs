using UnityEngine;

public class ExplosionEvent : MonoBehaviour
{
    const int MaxSlots = 2;

    enum BlastPhase
    {
        Waiting,
        Warning,
        Exploding
    }

    class BlastSlot
    {
        public BlastPhase phase = BlastPhase.Waiting;
        public float waitLeft;
        public float warningLeft;
        public float flashLeft;
        public float explodeLeft;
        public float activeWarningDuration;
        public bool flashOn;
        public Vector3 blastPos;
        public Transform warningRoot;
        public MeshRenderer warningRenderer;
        public Material warningMaterial;
    }

    [Header("Setup")]
    [SerializeField] CarController truck;
    [SerializeField] TrashPiles piles;
    [SerializeField] Transform floor;

    [Header("Timing")]
    [SerializeField, Tooltip("Shortest wait before the next explosion warning.")] float spawnIntervalMin = 10f;
    [SerializeField, Tooltip("Longest wait before the next explosion warning.")] float spawnIntervalMax = 18f;
    [SerializeField, Tooltip("How long the floor indicator flashes before detonation.")] float warningDuration = 3f;
    [SerializeField, Tooltip("Flash interval at the start of the warning.")] float flashIntervalStart = 0.45f;
    [SerializeField, Tooltip("Flash interval just before detonation.")] float flashIntervalEnd = 0.06f;
    [SerializeField, Tooltip("How long the truck stays stunned if caught in the blast.")] float stunDuration = 1f;

    [Header("Blast")]
    [SerializeField, Tooltip("Radius of the warning circle and stun check.")] float blastRadius = 3.2f;
    [SerializeField, Tooltip("Keep warnings this far inside the pile walls.")] float edgeInset = 1.25f;
    [SerializeField] Color warningColor = new Color(1f, 0.15f, 0.08f, 0.55f);
    [SerializeField] int debrisCount = 28;
    [SerializeField] float debrisLifetime = 1.6f;
    [SerializeField, Tooltip("If the pile's average radius is at least this, a second explosion can run at the same time.")] float spaciousRadiusForMulti = 15f;
    [SerializeField, Tooltip("Minimum distance between simultaneous explosion markers.")] float multiBlastSeparation = 6f;

    BlastSlot[] slots;
    Material debrisMaterial;
    float floorY;
    EventsHandler eventsHandler;

    void Start()
    {
        eventsHandler = GetComponentInParent<EventsHandler>();
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        if (truck == null)
        {
            truck = FindFirstObjectByType<CarController>();
        }

        if (piles == null)
        {
            piles = FindFirstObjectByType<TrashPiles>();
        }

        if (floor == null)
        {
            GameObject floorObject = GameObject.Find("Floor");
            if (floorObject != null)
            {
                floor = floorObject.transform;
            }
        }

        CacheFloorY();
        slots = new BlastSlot[MaxSlots];
        for (int i = 0; i < MaxSlots; i++)
        {
            slots[i] = new BlastSlot();
            BuildWarningDisc(slots[i], i);
            ScheduleNext(slots[i], i == 0);
        }

        // Second slot starts dormant until the arena is spacious.
        slots[1].waitLeft = 9999f;
        slots[1].phase = BlastPhase.Waiting;
    }

    void OnValidate()
    {
        spawnIntervalMin = Mathf.Max(0.5f, spawnIntervalMin);
        spawnIntervalMax = Mathf.Max(spawnIntervalMin, spawnIntervalMax);
        warningDuration = Mathf.Max(0.5f, warningDuration);
        flashIntervalStart = Mathf.Max(0.05f, flashIntervalStart);
        flashIntervalEnd = Mathf.Clamp(flashIntervalEnd, 0.02f, flashIntervalStart);
        stunDuration = Mathf.Max(0.05f, stunDuration);
        blastRadius = Mathf.Max(0.5f, blastRadius);
        edgeInset = Mathf.Max(0f, edgeInset);
        debrisCount = Mathf.Max(4, debrisCount);
        debrisLifetime = Mathf.Max(0.2f, debrisLifetime);
        spaciousRadiusForMulti = Mathf.Max(6f, spaciousRadiusForMulti);
        multiBlastSeparation = Mathf.Max(blastRadius, multiBlastSeparation);
    }

    void OnDestroy()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                if (slots[i].warningRoot != null)
                {
                    Destroy(slots[i].warningRoot.gameObject);
                }

                if (slots[i].warningMaterial != null)
                {
                    Destroy(slots[i].warningMaterial);
                }
            }
        }

        if (debrisMaterial != null)
        {
            Destroy(debrisMaterial);
        }
    }

    void Update()
    {
        if (slots == null)
        {
            return;
        }

        if (!ExperienceRestart.HasStarted)
        {
            return;
        }

        if (ExperienceRestart.IsEnded)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                ClearWarning(slots[i]);
                slots[i].phase = BlastPhase.Waiting;
                slots[i].waitLeft = 9999f;
            }

            SoundManager.SetExplosionWarning(false);
            return;
        }

        bool allowMulti = piles != null && piles.IsPlayAreaSpacious(spaciousRadiusForMulti);
        int activeLimit = allowMulti ? MaxSlots : 1;

        for (int i = 0; i < slots.Length; i++)
        {
            BlastSlot slot = slots[i];
            if (i >= activeLimit)
            {
                if (slot.phase == BlastPhase.Waiting)
                {
                    slot.waitLeft = 9999f;
                }

                continue;
            }

            if (i > 0 && slot.phase == BlastPhase.Waiting && slot.waitLeft > 100f)
            {
                // Wake the extra slot once the arena opens up.
                ScheduleNext(slot, false);
            }

            StepSlot(slot, Time.deltaTime);
        }
    }

    void StepSlot(BlastSlot slot, float dt)
    {
        switch (slot.phase)
        {
            case BlastPhase.Waiting:
                float drain = eventsHandler != null ? eventsHandler.EventWaitDrainRate() : 1f;
                slot.waitLeft -= dt * drain;
                if (slot.waitLeft <= 0f)
                {
                    BeginWarning(slot);
                }

                break;

            case BlastPhase.Warning:
                StepWarning(slot, dt);
                break;

            case BlastPhase.Exploding:
                slot.explodeLeft -= dt;
                if (slot.explodeLeft <= 0f)
                {
                    ScheduleNext(slot, false);
                }

                break;
        }
    }

    void CacheFloorY()
    {
        floorY = 0.06f;
        if (floor == null)
        {
            return;
        }

        Renderer renderer = floor.GetComponent<Renderer>();
        if (renderer != null)
        {
            floorY = renderer.bounds.max.y + 0.06f;
            return;
        }

        floorY = floor.position.y + 0.06f;
    }

    void ScheduleNext(BlastSlot slot, bool first)
    {
        ClearWarning(slot);
        slot.phase = BlastPhase.Waiting;
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        if (first)
        {
            slot.waitLeft = eventsHandler != null
                ? eventsHandler.NextExplosionWait()
                : Random.Range(spawnIntervalMin, spawnIntervalMax);
            return;
        }

        float wait = eventsHandler != null
            ? eventsHandler.NextExplosionWait()
            : Random.Range(spawnIntervalMin, spawnIntervalMax);
        // Offset extra slots so dual blasts don't always sync.
        slot.waitLeft = wait + Random.Range(0.75f, 2.25f);
    }

    void BeginWarning(BlastSlot slot)
    {
        CacheFloorY();
        if (piles == null || !TrySampleBlastPoint(out slot.blastPos))
        {
            slot.waitLeft = 1.5f;
            slot.phase = BlastPhase.Waiting;
            return;
        }

        slot.blastPos.y = floorY;
        if (slot.warningRoot != null)
        {
            slot.warningRoot.SetPositionAndRotation(slot.blastPos, Quaternion.identity);
            slot.warningRoot.localScale = new Vector3(blastRadius * 2f, 1f, blastRadius * 2f);
            slot.warningRoot.gameObject.SetActive(true);
        }

        slot.flashOn = true;
        SetWarningVisible(slot, true);
        slot.activeWarningDuration = CurrentWarningSeconds();
        slot.warningLeft = slot.activeWarningDuration;
        slot.flashLeft = CurrentFlashInterval(slot);
        slot.phase = BlastPhase.Warning;
        RefreshWarningSound();
    }

    bool TrySampleBlastPoint(out Vector3 point)
    {
        point = Vector3.zero;
        float minSepSq = multiBlastSeparation * multiBlastSeparation;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            if (!piles.TrySamplePlayAreaPoint(out point, edgeInset))
            {
                return false;
            }

            bool clear = true;
            for (int i = 0; i < slots.Length; i++)
            {
                BlastSlot other = slots[i];
                if (other == null || other.phase == BlastPhase.Waiting)
                {
                    continue;
                }

                Vector3 a = point;
                a.y = 0f;
                Vector3 b = other.blastPos;
                b.y = 0f;
                if ((a - b).sqrMagnitude < minSepSq)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                return true;
            }
        }

        return piles.TrySamplePlayAreaPoint(out point, edgeInset);
    }

    float CurrentWarningSeconds()
    {
        if (eventsHandler == null)
        {
            eventsHandler = EventsHandler.Instance;
        }

        return eventsHandler != null ? eventsHandler.ExplosionWarningSeconds() : warningDuration;
    }

    void StepWarning(BlastSlot slot, float dt)
    {
        slot.warningLeft -= dt;
        slot.flashLeft -= dt;
        if (slot.flashLeft <= 0f)
        {
            slot.flashOn = !slot.flashOn;
            SetWarningVisible(slot, slot.flashOn);
            slot.flashLeft = CurrentFlashInterval(slot);
        }

        if (slot.warningLeft <= 0f)
        {
            Detonate(slot);
        }
    }

    float CurrentFlashInterval(BlastSlot slot)
    {
        float duration = Mathf.Max(0.01f, slot.activeWarningDuration);
        float t = 1f - Mathf.Clamp01(slot.warningLeft / duration);
        return Mathf.Lerp(flashIntervalStart, flashIntervalEnd, t * t);
    }

    void Detonate(BlastSlot slot)
    {
        ClearWarning(slot);
        SpawnDebris(slot.blastPos);
        SoundManager.PlayExplosion();

        if (truck != null && TruckInBlast(slot.blastPos))
        {
            truck.Stun(stunDuration);
        }

        slot.explodeLeft = debrisLifetime;
        slot.phase = BlastPhase.Exploding;
        RefreshWarningSound();
    }

    void RefreshWarningSound()
    {
        bool warning = false;
        if (slots != null && ExperienceRestart.IsActive)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].phase == BlastPhase.Warning)
                {
                    warning = true;
                    break;
                }
            }
        }

        SoundManager.SetExplosionWarning(warning);
    }

    bool TruckInBlast(Vector3 blastPos)
    {
        Vector3 flatBlast = blastPos;
        flatBlast.y = 0f;

        Collider[] colliders = truck.GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            float radiusSq = blastRadius * blastRadius;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null || !colliders[i].enabled)
                {
                    continue;
                }

                Vector3 closest = colliders[i].ClosestPoint(blastPos);
                closest.y = 0f;
                if ((closest - flatBlast).sqrMagnitude <= radiusSq)
                {
                    return true;
                }
            }

            return false;
        }

        Vector3 truckPos = truck.transform.position;
        truckPos.y = 0f;
        return (truckPos - flatBlast).sqrMagnitude <= blastRadius * blastRadius;
    }

    void ClearWarning(BlastSlot slot)
    {
        SetWarningVisible(slot, false);
        if (slot.warningRoot != null)
        {
            slot.warningRoot.gameObject.SetActive(false);
        }
    }

    void SetWarningVisible(BlastSlot slot, bool visible)
    {
        if (slot.warningRenderer != null)
        {
            slot.warningRenderer.enabled = visible;
        }

        if (slot.warningMaterial != null)
        {
            Color c = warningColor;
            c.a = visible ? warningColor.a : 0f;
            ApplyColor(slot.warningMaterial, c);
        }
    }

    void BuildWarningDisc(BlastSlot slot, int index)
    {
        GameObject disc = new GameObject("ExplosionWarning " + (index + 1));
        disc.transform.SetParent(null, false);

        MeshFilter filter = disc.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildDiscMesh(48);
        slot.warningRenderer = disc.AddComponent<MeshRenderer>();
        slot.warningMaterial = CreateWarningMaterial(warningColor);
        slot.warningRenderer.sharedMaterial = slot.warningMaterial;
        slot.warningRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        slot.warningRenderer.receiveShadows = false;

        slot.warningRoot = disc.transform;
        slot.warningRoot.gameObject.SetActive(false);
    }

    static Mesh BuildDiscMesh(int segments)
    {
        segments = Mathf.Max(12, segments);
        Vector3[] verts = new Vector3[segments + 1];
        Vector3[] normals = new Vector3[segments + 1];
        Vector2[] uvs = new Vector2[segments + 1];
        int[] tris = new int[segments * 3];

        verts[0] = Vector3.zero;
        normals[0] = Vector3.up;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * 0.5f;
            float z = Mathf.Sin(angle) * 0.5f;
            verts[i + 1] = new Vector3(x, 0f, z);
            normals[i + 1] = Vector3.up;
            uvs[i + 1] = new Vector2(x + 0.5f, z + 0.5f);

            int tri = i * 3;
            tris[tri] = 0;
            tris[tri + 1] = i + 1;
            tris[tri + 2] = i + 2 <= segments ? i + 2 : 1;
        }

        Mesh mesh = new Mesh { name = "ExplosionWarningDisc" };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    void SpawnDebris(Vector3 origin)
    {
        if (debrisMaterial == null)
        {
            debrisMaterial = CreateOpaqueMaterial(new Color(1f, 0.35f, 0.05f, 1f));
        }

        for (int i = 0; i < debrisCount; i++)
        {
            GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "ExplosionDebris";
            chunk.transform.SetParent(null, true);
            chunk.transform.position = origin + Random.insideUnitSphere * 0.35f;
            chunk.transform.rotation = Random.rotation;
            float size = Random.Range(0.08f, 0.22f);
            chunk.transform.localScale = Vector3.one * size;

            Collider col = chunk.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            MeshRenderer renderer = chunk.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Color fire = FieryColor();
                Material mat = new Material(debrisMaterial);
                ApplyColor(mat, fire);
                renderer.sharedMaterial = mat;
                Destroy(mat, debrisLifetime);
            }

            Rigidbody body = chunk.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.linearDamping = 0.4f;
            body.angularDamping = 0.3f;
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) + Random.Range(0.35f, 0.9f);
            body.AddForce(dir.normalized * Random.Range(5f, 11f), ForceMode.Impulse);
            body.AddTorque(Random.insideUnitSphere * 8f, ForceMode.Impulse);

            Destroy(chunk, debrisLifetime);
        }
    }

    static Color FieryColor()
    {
        float pick = Random.value;
        if (pick < 0.34f)
        {
            return new Color(1f, Random.Range(0.05f, 0.2f), 0.02f, 1f);
        }

        if (pick < 0.68f)
        {
            return new Color(1f, Random.Range(0.35f, 0.55f), 0.05f, 1f);
        }

        return new Color(1f, Random.Range(0.75f, 0.95f), Random.Range(0.15f, 0.35f), 1f);
    }

    static Material CreateWarningMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        ApplyColor(material, color);
        material.renderQueue = 3000;

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            material.SetOverrideTag("RenderType", "Transparent");
        }
        else if (shader != null && shader.name.IndexOf("Sprites", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
        }
        else if (shader != null && shader.name.Contains("Standard"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        return material;
    }

    static Material CreateOpaqueMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        ApplyColor(material, color);
        return material;
    }

    static void ApplyColor(Material material, Color color)
    {
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
