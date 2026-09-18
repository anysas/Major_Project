using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DayNightCycle : MonoBehaviour
{
    [SerializeField] Light sun;
    [SerializeField, Min(1f)] float daySeconds = 30f;
    [SerializeField, Min(1f)] float nightSeconds = 10f;
    [SerializeField, Min(0.05f)] float fadeSeconds = 3.5f;
    [SerializeField] Color nightSunColor = new Color(0.62f, 0.32f, 0.92f, 1f);
    [SerializeField] float nightSunIntensity = 0.46f;
    [SerializeField] float nightAmbient = 0.55f;
    [SerializeField] float nightSkyExposure = 0.7f;
    [SerializeField] Color nightSkyTint = new Color(0.48f, 0.26f, 0.65f, 1f);
    [SerializeField] float nightPostExposure = -0.1f;

    Color daySunColor;
    float daySunIntensity;
    float dayAmbient;
    Material originalSkybox;
    Material skyboxInstance;
    float daySkyExposure = 1.3f;
    Color daySkyTint = Color.white;
    bool hasSkyExposure;
    bool hasSkyTint;
    Volume nightVolume;
    ColorAdjustments colorAdjust;
    float elapsed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BindIfPresent()
    {
        if (FindFirstObjectByType<DayNightCycle>() != null)
        {
            return;
        }

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                lights[i].gameObject.AddComponent<DayNightCycle>();
                return;
            }
        }
    }

    void Awake()
    {
        if (sun == null)
        {
            sun = GetComponent<Light>();
        }

        if (sun == null)
        {
            sun = RenderSettings.sun;
        }

        if (sun != null)
        {
            daySunColor = sun.color;
            daySunIntensity = sun.intensity;
        }

        dayAmbient = RenderSettings.ambientIntensity;
        CacheSkybox();
        SetupNightVolume();
        ApplyNight(0f);
    }

    void OnDestroy()
    {
        if (originalSkybox != null)
        {
            RenderSettings.skybox = originalSkybox;
        }

        if (skyboxInstance != null)
        {
            Destroy(skyboxInstance);
        }

        if (nightVolume != null && nightVolume.profile != null)
        {
            Destroy(nightVolume.profile);
        }
    }

    void Update()
    {
        if (!ExperienceRestart.IsActive)
        {
            return;
        }

        elapsed += Time.deltaTime;
        ApplyNight(CurrentNightBlend());
    }

    void CacheSkybox()
    {
        originalSkybox = RenderSettings.skybox;
        if (originalSkybox == null)
        {
            return;
        }

        skyboxInstance = new Material(originalSkybox);
        RenderSettings.skybox = skyboxInstance;
        hasSkyExposure = skyboxInstance.HasProperty("_Exposure");
        if (hasSkyExposure)
        {
            daySkyExposure = skyboxInstance.GetFloat("_Exposure");
        }

        hasSkyTint = skyboxInstance.HasProperty("_SkyTint");
        if (hasSkyTint)
        {
            daySkyTint = skyboxInstance.GetColor("_SkyTint");
        }
    }

    void SetupNightVolume()
    {
        nightVolume = GetComponent<Volume>();
        if (nightVolume == null)
        {
            nightVolume = gameObject.AddComponent<Volume>();
        }

        nightVolume.isGlobal = true;
        nightVolume.priority = 40f;
        nightVolume.weight = 1f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        colorAdjust = profile.Add<ColorAdjustments>(true);
        colorAdjust.active = true;
        colorAdjust.postExposure.Override(0f);
        colorAdjust.colorFilter.Override(Color.white);
        colorAdjust.contrast.Override(0f);
        nightVolume.profile = profile;
    }

    float CurrentNightBlend()
    {
        float day = Mathf.Max(1f, daySeconds);
        float night = Mathf.Max(1f, nightSeconds);
        float fade = Mathf.Min(Mathf.Max(0.05f, fadeSeconds), day * 0.45f, night * 0.85f);
        float cycle = day + night;
        float x = elapsed % cycle;
        float blend;
        if (x < day - fade)
        {
            blend = 0f;
        }
        else if (x < day)
        {
            blend = (x - (day - fade)) / fade;
        }
        else if (x < day + night - fade)
        {
            blend = 1f;
        }
        else
        {
            blend = (day + night - x) / fade;
        }

        blend = Mathf.Clamp01(blend);
        return blend * blend * blend * (blend * (blend * 6f - 15f) + 10f);
    }

    void ApplyNight(float t)
    {
        if (sun != null)
        {
            sun.color = Color.Lerp(daySunColor, nightSunColor, t);
            sun.intensity = Mathf.Lerp(daySunIntensity, nightSunIntensity, t);
        }

        RenderSettings.ambientIntensity = Mathf.Lerp(dayAmbient, nightAmbient, t);
        if (skyboxInstance != null)
        {
            if (hasSkyExposure)
            {
                skyboxInstance.SetFloat("_Exposure", Mathf.Lerp(daySkyExposure, nightSkyExposure, t));
            }

            if (hasSkyTint)
            {
                skyboxInstance.SetColor("_SkyTint", Color.Lerp(daySkyTint, nightSkyTint, t));
            }
        }

        if (colorAdjust != null)
        {
            colorAdjust.postExposure.Override(Mathf.Lerp(0f, nightPostExposure, t));
            colorAdjust.colorFilter.Override(Color.Lerp(Color.white, new Color(0.92f, 0.72f, 1f, 1f), t));
            colorAdjust.contrast.Override(Mathf.Lerp(0f, 6f, t));
        }
    }
}
