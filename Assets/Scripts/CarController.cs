using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [SerializeField] float maxSpeed = 14f;
    [SerializeField] float acceleration = 70f;
    [SerializeField] float deceleration = 80f;
    [SerializeField] float turnSpeed = 240f;
    [SerializeField] float wheelRadius = 0.75f;
    [SerializeField] float armRestX = -100f;
    [SerializeField] float armRaisedX = -55f;
    [SerializeField] float armRotateSpeed = 120f;
    [SerializeField] float loweredSpeedFactor = 0.7f;
    [SerializeField] float raisedSpeedFactor = 1.5f;
    [SerializeField] float headlightRange = 48f;
    [SerializeField] float headlightAngle = 64f;
    [SerializeField] float headlightIntensity = 2800f;
    [SerializeField] float stunCloudHeight = 0.2f;
    [SerializeField] Color stunCloudColor = new Color(0.96f, 0.98f, 1f, 0.72f);

    Rigidbody rb;
    Transform viewCamera;
    Transform[] wheels;
    float[] wheelRadii;
    Transform arm;
    float armX;
    Quaternion armRestLocalRotation;
    float stunLeft;
    Light headlightBeam;
    Transform headlightLamp;
    Renderer[] headlightRenderers;
    MaterialPropertyBlock headlightBlock;
    ParticleSystem[] stunClouds;
    Material stunCloudMaterial;
    Texture2D stunCloudTexture;

    enum ArmState
    {
        Mounted,
        Flying,
        Fading
    }

    ArmState armState = ArmState.Mounted;
    Transform armMountParent;
    Vector3 armMountLocalPosition;
    Vector3 armMountLocalScale;
    Collider[] armColliders;
    bool[] armColliderWasEnabled;
    ArmFadeSlot[] armFades;
    Vector3 flyVelocity;
    Vector3 flySpin;
    float flyElapsed;
    float fadeElapsed;

    [SerializeField] float armFlySpeed = 8f;
    [SerializeField] float armFlyUp = 8f;
    [SerializeField] float armFlyGravity = 9f;
    [SerializeField] float armReturnDelay = 1.6f;
    [SerializeField] float armFadeSeconds = 1.05f;

    public Vector3 DriveVelocity { get; private set; }

    public bool IsArmRaised { get; private set; }

    public bool IsStunned
    {
        get { return stunLeft > 0f; }
    }

    public void Stun(float seconds)
    {
        float duration = Mathf.Max(0f, seconds);
        stunLeft = Mathf.Max(stunLeft, duration);
        if (duration > 0f)
        {
            PlayStunClouds();
        }
    }

    public void BlastOffArm(Vector3 blastOrigin)
    {
        if (arm == null || armState == ArmState.Flying || !ExperienceRestart.IsActive)
        {
            return;
        }

        if (armState == ArmState.Fading)
        {
            RestoreArmMaterials();
        }

        Vector3 away = arm.position - blastOrigin;
        away.y = 0f;
        if (away.sqrMagnitude < 0.25f)
        {
            away = transform.position - blastOrigin;
            away.y = 0f;
        }

        if (away.sqrMagnitude < 0.01f)
        {
            away = transform.right;
        }

        away.Normalize();
        away = Quaternion.AngleAxis(Random.Range(-28f, 28f), Vector3.up) * away;
        flyVelocity = away * Mathf.Max(0.5f, armFlySpeed) + Vector3.up * Mathf.Max(0.5f, armFlyUp);
        flySpin = new Vector3(
            Random.Range(-260f, 260f),
            Random.Range(-180f, 180f),
            Random.Range(-260f, 260f));
        flyElapsed = 0f;
        SetArmCollidersEnabled(false);
        arm.SetParent(null, true);
        armState = ArmState.Flying;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = 1500f;
        rb.centerOfMass = new Vector3(0f, -0.5f, 0f);
        // Keep the truck on the floor. Closing pile walls and convex trash
        // otherwise depenetrate along Y and leave it floating.
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        viewCamera = Camera.main != null ? Camera.main.transform : null;
        CollectWheels();
        CollectArm();
        CollectHeadlight();
        CollectStunClouds();
    }

    void OnDestroy()
    {
        if (stunCloudMaterial != null)
        {
            Destroy(stunCloudMaterial);
        }

        if (stunCloudTexture != null)
        {
            Destroy(stunCloudTexture);
        }

        DestroyArmFadeMaterials();
    }

    void CollectArm()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != transform && child.name.Equals("Arm", System.StringComparison.OrdinalIgnoreCase))
            {
                arm = child;
                break;
            }
        }

        if (arm == null)
        {
            return;
        }

        // Local X is flipped on this mesh: more negative pitches into the ground.
        // Rest at -100, raise toward the opposite side of the old -145 target (-55).
        Vector3 euler = arm.localEulerAngles;
        arm.localRotation = Quaternion.Euler(armRestX, euler.y, euler.z);
        armRestLocalRotation = arm.localRotation;
        armX = armRestX;
        armMountParent = arm.parent;
        armMountLocalPosition = arm.localPosition;
        armMountLocalScale = arm.localScale;
        armColliders = arm.GetComponentsInChildren<Collider>(true);
        armColliderWasEnabled = new bool[armColliders.Length];
        for (int i = 0; i < armColliders.Length; i++)
        {
            armColliderWasEnabled[i] = armColliders[i] != null && armColliders[i].enabled;
        }

        SetLayerRecursive(arm, LayerMask.NameToLayer("TruckArm"));
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null || layer < 0)
        {
            return;
        }

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursive(root.GetChild(i), layer);
        }
    }

    void CollectHeadlight()
    {
        Transform lamp = null;
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != transform && child.name.IndexOf("headlight", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lamp = child;
                break;
            }
        }

        if (lamp == null)
        {
            lamp = transform;
        }

        headlightLamp = lamp;
        GameObject beamObject = new GameObject("HeadlightCone");
        beamObject.transform.SetParent(transform, false);
        beamObject.transform.localScale = Vector3.one;

        headlightBeam = beamObject.AddComponent<Light>();
        headlightBeam.type = LightType.Spot;
        headlightBeam.color = new Color(1f, 0.95f, 0.8f, 1f);
        headlightBeam.range = Mathf.Max(4f, headlightRange);
        headlightBeam.spotAngle = Mathf.Clamp(headlightAngle, 10f, 160f);
        headlightBeam.innerSpotAngle = Mathf.Clamp(headlightAngle * 0.82f, 5f, headlightAngle);
        headlightBeam.intensity = Mathf.Max(1f, headlightIntensity);
        headlightBeam.shadows = LightShadows.None;
        int armLayer = LayerMask.NameToLayer("TruckArm");
        if (armLayer >= 0)
        {
            headlightBeam.cullingMask &= ~(1 << armLayer);
        }
        headlightBeam.enabled = false;
        beamObject.AddComponent<UniversalAdditionalLightData>();

        headlightRenderers = lamp != transform ? lamp.GetComponentsInChildren<Renderer>() : null;
        headlightBlock = new MaterialPropertyBlock();
        SetHeadlightOn(false);
    }

    void UpdateHeadlight()
    {
        bool on = GameInput.HeadlightHeld;

        SetHeadlightOn(on);
        if (!on || headlightBeam == null)
        {
            return;
        }

        Vector3 origin = headlightLamp != null ? headlightLamp.position : transform.position + transform.forward * 1.2f + Vector3.up * 1.1f;
        Vector3 aim = transform.forward;
        aim.y = -0.06f;
        if (aim.sqrMagnitude < 0.0001f)
        {
            aim = Vector3.forward;
        }

        headlightBeam.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(aim.normalized, Vector3.up));
        headlightBeam.range = Mathf.Max(4f, headlightRange);
        headlightBeam.spotAngle = Mathf.Clamp(headlightAngle, 10f, 160f);
        headlightBeam.innerSpotAngle = Mathf.Clamp(headlightAngle * 0.82f, 5f, headlightAngle);
        headlightBeam.intensity = Mathf.Max(1f, headlightIntensity);
    }

    void SetHeadlightOn(bool on)
    {
        if (headlightBeam != null)
        {
            headlightBeam.enabled = on;
        }

        if (headlightRenderers == null || headlightBlock == null)
        {
            return;
        }

        Color emission = on ? new Color(8f, 7.4f, 5.6f, 1f) : Color.black;
        for (int i = 0; i < headlightRenderers.Length; i++)
        {
            Renderer renderer = headlightRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(headlightBlock);
            headlightBlock.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(headlightBlock);
        }
    }

    void CollectStunClouds()
    {
        if (stunClouds != null && stunClouds.Length > 0)
        {
            return;
        }

        stunCloudTexture = CreateSoftCloudTexture(96);
        stunCloudMaterial = CreateStunCloudMaterial(stunCloudTexture);

        List<Transform> pipes = CollectPipes();
        if (pipes.Count == 0)
        {
            stunClouds = new[] { BuildStunCloud(transform, StunCloudFallbackLocalPosition(), false) };
            return;
        }

        stunClouds = new ParticleSystem[pipes.Count];
        for (int i = 0; i < pipes.Count; i++)
        {
            stunClouds[i] = BuildStunCloud(pipes[i], PipeMouthLocalPosition(pipes[i]), true);
        }
    }

    List<Transform> CollectPipes()
    {
        List<Transform> pipes = new List<Transform>();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != transform && child.name.IndexOf("pipe", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                pipes.Add(child);
            }
        }

        return pipes;
    }

    ParticleSystem BuildStunCloud(Transform parent, Vector3 localPosition, bool compensateParentScale)
    {
        GameObject cloudObject = new GameObject("StunClouds");
        cloudObject.layer = gameObject.layer;
        cloudObject.transform.SetParent(parent, false);
        cloudObject.transform.localPosition = localPosition;
        cloudObject.transform.rotation = Quaternion.identity;
        if (compensateParentScale)
        {
            Vector3 parentScale = parent.lossyScale;
            cloudObject.transform.localScale = new Vector3(
                InverseScale(parentScale.x),
                InverseScale(parentScale.y),
                InverseScale(parentScale.z));
        }
        else
        {
            cloudObject.transform.localRotation = Quaternion.identity;
        }

        ParticleSystem particles = cloudObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ConfigureStunCloud(particles, cloudObject.GetComponent<ParticleSystemRenderer>());
        return particles;
    }

    void ConfigureStunCloud(ParticleSystem particles, ParticleSystemRenderer renderer)
    {
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.15f, 1.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.35f);
        main.startColor = stunCloudColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.gravityModifier = -0.22f;
        main.maxParticles = 28;
        main.prewarm = false;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 7f;
        emission.burstCount = 0;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.08f;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.alignToDirection = false;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.93f, 0.96f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.16f),
                new GradientAlphaKey(0.45f, 0.58f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fade;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve grow = new AnimationCurve(
            new Keyframe(0f, 0.4f),
            new Keyframe(0.3f, 1.1f),
            new Keyframe(1f, 1.75f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, grow);

        ParticleSystem.RotationOverLifetimeModule spin = particles.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-18f, 18f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.22f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.18f;
        noise.damping = true;
        noise.octaveCount = 2;

        ParticleSystem.VelocityOverLifetimeModule drift = particles.velocityOverLifetime;
        drift.enabled = true;
        drift.space = ParticleSystemSimulationSpace.World;
        drift.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        drift.y = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        drift.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = stunCloudMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lengthScale = 1f;
        renderer.minParticleSize = 0.02f;
        renderer.maxParticleSize = 0.75f;
    }

    Vector3 PipeMouthLocalPosition(Transform pipe)
    {
        Renderer renderer = pipe.GetComponentInChildren<Renderer>();
        if (renderer == null)
        {
            return Vector3.up * Mathf.Max(0.05f, stunCloudHeight);
        }

        Bounds local = renderer.localBounds;
        Vector3 extents = local.extents;
        Vector3 axis = Vector3.up;
        if (extents.z >= extents.x && extents.z >= extents.y)
        {
            axis = Vector3.forward;
        }
        else if (extents.x >= extents.y && extents.x >= extents.z)
        {
            axis = Vector3.right;
        }

        Vector3 endA = renderer.transform.TransformPoint(local.center + Vector3.Scale(axis, extents));
        Vector3 endB = renderer.transform.TransformPoint(local.center - Vector3.Scale(axis, extents));
        Vector3 mouth = endA.y >= endB.y ? endA : endB;
        mouth += Vector3.up * Mathf.Max(0f, stunCloudHeight);
        return pipe.InverseTransformPoint(mouth);
    }

    Vector3 StunCloudFallbackLocalPosition()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 local = box.center;
            local.y += box.size.y * 0.5f + Mathf.Max(0f, stunCloudHeight);
            return local;
        }

        return new Vector3(0f, 2.4f + stunCloudHeight, 0.4f);
    }

    static float InverseScale(float value)
    {
        return Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;
    }

    void PlayStunClouds()
    {
        if (stunClouds == null || stunClouds.Length == 0)
        {
            CollectStunClouds();
        }

        if (stunClouds == null)
        {
            return;
        }

        for (int i = 0; i < stunClouds.Length; i++)
        {
            ParticleSystem particles = stunClouds[i];
            if (particles == null)
            {
                continue;
            }

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            if (!particles.isPlaying)
            {
                particles.Play(true);
            }

            particles.Emit(Random.Range(4, 8));
        }
    }

    void StopStunClouds()
    {
        if (stunClouds == null)
        {
            return;
        }

        for (int i = 0; i < stunClouds.Length; i++)
        {
            ParticleSystem particles = stunClouds[i];
            if (particles == null || !particles.isPlaying)
            {
                continue;
            }

            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    static Material CreateStunCloudMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader);
        material.color = Color.white;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        if (texture != null)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }

            material.mainTexture = texture;
        }

        material.renderQueue = 3000;
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

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }

    static Texture2D CreateSoftCloudTexture(int size)
    {
        size = Mathf.Max(16, size);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "StunCloudPuff";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float lump = 0.14f * Mathf.Sin(dx * 6.1f + dy * 3.4f)
                    + 0.09f * Mathf.Sin(dx * 9.7f - dy * 7.2f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - lump;
                float t = Mathf.Clamp01(1f - distance);
                t = t * t * (3f - 2f * t);
                float alpha = Mathf.Pow(t, 1.55f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    void CollectWheels()
    {
        List<Transform> found = new List<Transform>();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != transform && child.name.StartsWith("wheel", System.StringComparison.OrdinalIgnoreCase))
            {
                found.Add(child);
            }
        }

        wheels = found.ToArray();
        wheelRadii = new float[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            wheelRadii[i] = EstimateWheelRadius(wheels[i]);
        }
    }

    float EstimateWheelRadius(Transform wheel)
    {
        Renderer renderer = wheel.GetComponentInChildren<Renderer>();
        if (renderer == null)
        {
            return Mathf.Max(0.05f, wheelRadius);
        }

        Vector3 size = renderer.bounds.size;
        // Rolling radius is half the wheel height (or the smaller vertical/rolling dimension).
        float radius = 0.5f * Mathf.Min(size.y, Mathf.Max(size.x, size.z));
        if (radius < 0.05f)
        {
            radius = Mathf.Max(0.05f, wheelRadius);
        }

        return radius;
    }

    void Update()
    {
        if (!ExperienceRestart.IsActive)
        {
            SetHeadlightOn(false);
            StopStunClouds();
            SoundManager.UpdateMotor(0f);
            StepDetachedArm();
            return;
        }

        if (stunLeft > 0f)
        {
            stunLeft -= Time.deltaTime;
            if (stunLeft <= 0f)
            {
                stunLeft = 0f;
                StopStunClouds();
            }
        }

        UpdateArm();
        UpdateHeadlight();
        SpinWheels();
        SoundManager.UpdateMotor(DriveVelocity.magnitude);
    }

    void UpdateArm()
    {
        bool raise = GameInput.ArmUpHeld;

        if (raise && !IsArmRaised)
        {
            SoundManager.PlayArmUp();
        }

        IsArmRaised = raise;

        if (arm == null)
        {
            return;
        }

        if (armState == ArmState.Flying)
        {
            StepFlyingArm();
            return;
        }

        float target = raise ? armRaisedX : armRestX;
        armX = Mathf.MoveTowards(armX, target, armRotateSpeed * Time.deltaTime);
        ApplyArmRotation();

        if (armState == ArmState.Fading)
        {
            StepArmFade();
        }
    }

    void ApplyArmRotation()
    {
        float pitchFromRest = armX - armRestX;
        arm.localRotation = armRestLocalRotation * Quaternion.Euler(pitchFromRest, 0f, 0f);
    }

    void StepDetachedArm()
    {
        if (arm == null)
        {
            return;
        }

        if (armState == ArmState.Flying)
        {
            StepFlyingArm();
        }
        else if (armState == ArmState.Fading)
        {
            StepArmFade();
        }
    }

    void StepFlyingArm()
    {
        float dt = Time.deltaTime;
        flyElapsed += dt;
        if (flyElapsed < 4f)
        {
            flyVelocity += Vector3.down * Mathf.Max(0f, armFlyGravity) * dt;
            arm.position += flyVelocity * dt;
            arm.Rotate(flySpin * dt, Space.World);
        }

        if (flyElapsed >= Mathf.Max(0.2f, armReturnDelay) && ExperienceRestart.IsActive && HealthHearts.Lives > 0)
        {
            BeginArmReturn();
        }
    }

    void BeginArmReturn()
    {
        Transform parent = armMountParent != null ? armMountParent : transform;
        arm.SetParent(parent, false);
        arm.localPosition = armMountLocalPosition;
        arm.localScale = armMountLocalScale;
        ApplyArmRotation();
        EnsureArmFadeMaterials();
        AssignArmFadeMaterials();
        SetArmAlpha(0f);
        SetArmCollidersEnabled(true);
        fadeElapsed = 0f;
        armState = ArmState.Fading;
    }

    void StepArmFade()
    {
        float duration = Mathf.Max(0.05f, armFadeSeconds);
        fadeElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(fadeElapsed / duration);
        SetArmAlpha(Mathf.SmoothStep(0f, 1f, t));
        if (t >= 1f)
        {
            RestoreArmMaterials();
            armState = ArmState.Mounted;
        }
    }

    void SetArmCollidersEnabled(bool enabled)
    {
        if (armColliders == null)
        {
            return;
        }

        for (int i = 0; i < armColliders.Length; i++)
        {
            Collider collider = armColliders[i];
            if (collider == null)
            {
                continue;
            }

            if (enabled)
            {
                collider.enabled = armColliderWasEnabled != null && i < armColliderWasEnabled.Length
                    ? armColliderWasEnabled[i]
                    : true;
            }
            else
            {
                collider.enabled = false;
            }
        }
    }

    void EnsureArmFadeMaterials()
    {
        if (armFades != null || arm == null)
        {
            return;
        }

        Renderer[] renderers = arm.GetComponentsInChildren<Renderer>(true);
        armFades = new ArmFadeSlot[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            ArmFadeSlot slot = new ArmFadeSlot();
            slot.renderer = renderer;
            if (renderer == null)
            {
                armFades[i] = slot;
                continue;
            }

            Material[] shared = renderer.sharedMaterials;
            slot.shared = shared;
            slot.runtime = new Material[shared.Length];
            slot.colors = new Color[shared.Length];
            for (int m = 0; m < shared.Length; m++)
            {
                if (shared[m] == null)
                {
                    continue;
                }

                Material copy = new Material(shared[m]);
                copy.name = shared[m].name + " ArmFade";
                MakeArmTransparent(copy);
                slot.colors[m] = ReadMaterialColor(copy);
                slot.runtime[m] = copy;
            }

            armFades[i] = slot;
        }
    }

    void AssignArmFadeMaterials()
    {
        if (armFades == null)
        {
            return;
        }

        for (int i = 0; i < armFades.Length; i++)
        {
            ArmFadeSlot slot = armFades[i];
            if (slot == null || slot.renderer == null || slot.runtime == null)
            {
                continue;
            }

            slot.renderer.sharedMaterials = slot.runtime;
        }
    }

    void SetArmAlpha(float alpha)
    {
        if (armFades == null)
        {
            return;
        }

        float clamped = Mathf.Clamp01(alpha);
        for (int i = 0; i < armFades.Length; i++)
        {
            ArmFadeSlot slot = armFades[i];
            if (slot == null || slot.runtime == null || slot.colors == null)
            {
                continue;
            }

            int count = Mathf.Min(slot.runtime.Length, slot.colors.Length);
            for (int m = 0; m < count; m++)
            {
                Material material = slot.runtime[m];
                if (material == null)
                {
                    continue;
                }

                Color color = slot.colors[m];
                color.a *= clamped;
                WriteMaterialColor(material, color);
            }
        }
    }

    void RestoreArmMaterials()
    {
        if (armFades == null)
        {
            return;
        }

        for (int i = 0; i < armFades.Length; i++)
        {
            ArmFadeSlot slot = armFades[i];
            if (slot == null || slot.renderer == null || slot.shared == null)
            {
                continue;
            }

            slot.renderer.sharedMaterials = slot.shared;
        }
    }

    void DestroyArmFadeMaterials()
    {
        if (armFades == null)
        {
            return;
        }

        for (int i = 0; i < armFades.Length; i++)
        {
            ArmFadeSlot slot = armFades[i];
            if (slot == null || slot.runtime == null)
            {
                continue;
            }

            for (int m = 0; m < slot.runtime.Length; m++)
            {
                if (slot.runtime[m] != null)
                {
                    Destroy(slot.runtime[m]);
                }
            }
        }

        armFades = null;
    }

    static void MakeArmTransparent(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static Color ReadMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        return material.color;
    }

    static void WriteMaterialColor(Material material, Color color)
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

    class ArmFadeSlot
    {
        public Renderer renderer;
        public Material[] shared;
        public Material[] runtime;
        public Color[] colors;
    }

    void SpinWheels()
    {
        if (wheels == null || wheels.Length == 0)
        {
            return;
        }

        float signedSpeed = Vector3.Dot(DriveVelocity, transform.forward);
        if (Mathf.Abs(signedSpeed) < 0.01f)
        {
            return;
        }

        // Roll around the truck axle (left-right), independent of mesh import orientation.
        Vector3 axle = transform.right;
        for (int i = 0; i < wheels.Length; i++)
        {
            Transform wheel = wheels[i];
            if (wheel == null)
            {
                continue;
            }

            float radius = wheelRadii != null && i < wheelRadii.Length
                ? wheelRadii[i]
                : Mathf.Max(0.05f, wheelRadius);
            float degrees = -signedSpeed / radius * Mathf.Rad2Deg * Time.deltaTime;
            wheel.Rotate(axle, degrees, Space.World);
        }
    }

    void FixedUpdate()
    {
        if (!ExperienceRestart.IsActive || stunLeft > 0f)
        {
            DriveVelocity = Vector3.zero;
            rb.linearVelocity = new Vector3(0f, 0f, 0f);
            return;
        }

        Vector3 moveDir = CameraRelativeDirection(ReadMoveInput());
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float raiseT = Mathf.Abs(armRaisedX - armRestX) > 0.001f
            ? Mathf.InverseLerp(armRestX, armRaisedX, armX)
            : 1f;
        if (DayNightCycle.IsNight && !GameInput.HeadlightHeld)
        {
            raiseT = 0f;
        }

        float speedCap = maxSpeed * Mathf.Lerp(loweredSpeedFactor, raisedSpeedFactor, raiseT);

        if (moveDir.sqrMagnitude > 0.01f)
        {
            float speed = Mathf.MoveTowards(horizontal.magnitude, speedCap, acceleration * Time.fixedDeltaTime);
            horizontal = moveDir * speed;

            Quaternion targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
        else
        {
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, deceleration * Time.fixedDeltaTime);
        }

        DriveVelocity = horizontal;
        rb.linearVelocity = new Vector3(horizontal.x, 0f, horizontal.z);
    }

    static Vector3 ReadMoveInput()
    {
        Vector2 stick = GameInput.Move;
        Vector3 input = new Vector3(stick.x, 0f, stick.y);
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        return input;
    }

    Vector3 CameraRelativeDirection(Vector3 input)
    {
        if (input.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (viewCamera != null)
        {
            forward = viewCamera.forward;
            right = viewCamera.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = viewCamera.up;
                forward.y = 0f;
            }

            forward.Normalize();
            right.Normalize();
        }

        Vector3 direction = right * input.x + forward * input.z;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }
}
