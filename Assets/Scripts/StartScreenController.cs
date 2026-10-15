using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DefaultExecutionOrder(-200)]
public class StartScreenController : MonoBehaviour
{
    [SerializeField] Button startButton;
    [SerializeField, Min(0.05f)] float unblurSeconds = 0.65f;
    [SerializeField, Min(0.15f)] float blurDistance = 1.5f;
    [SerializeField, Range(0.5f, 1.5f)] float blurRadius = 1.5f;

    enum ScreenPhase
    {
        Title,
        Controls,
        Starting
    }

    CanvasGroup canvasGroup;
    Volume blurVolume;
    ScreenPhase phase;
    int controlsOpenedFrame = -1;
    GameObject boxControls;
    GameObject xboxControls;
    GameObject activeControls;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BindIfPresent()
    {
        GameObject screen = GameObject.Find("StartScreen");
        if (screen == null)
        {
            return;
        }

        if (screen.GetComponent<StartScreenController>() == null)
        {
            screen.AddComponent<StartScreenController>();
        }
    }

    void Awake()
    {
        ExperienceRestart.HoldUntilStart();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        EnsureStartButton();
        SetupBlur();
        CacheControlScreens();
        HideControlScreens();
    }

    void Update()
    {
        if (phase == ScreenPhase.Starting)
        {
            return;
        }

        if (phase == ScreenPhase.Controls && Time.frameCount == controlsOpenedFrame)
        {
            return;
        }

        if (EnterPressedThisFrame() || ScreenPressedThisFrame())
        {
            if (phase == ScreenPhase.Title)
            {
                ShowControls();
            }
            else
            {
                BeginGame();
            }
        }
    }

    void OnDestroy()
    {
        if (blurVolume != null && blurVolume.profile != null)
        {
            Destroy(blurVolume.profile);
        }
    }

    void EnsureStartButton()
    {
        if (startButton == null)
        {
            Transform buttonTransform = transform.Find("StartButton");
            if (buttonTransform != null)
            {
                startButton = buttonTransform.GetComponent<Button>();
                if (startButton == null)
                {
                    startButton = buttonTransform.gameObject.AddComponent<Button>();
                }
            }
        }

        if (startButton == null)
        {
            return;
        }

        Graphic graphic = startButton.GetComponent<Graphic>();
        if (graphic != null)
        {
            startButton.targetGraphic = graphic;
        }

        startButton.onClick.RemoveListener(OnStartPressed);
        startButton.onClick.AddListener(OnStartPressed);
    }

    void SetupBlur()
    {
        GameObject volumeObject = new GameObject("StartScreenBlur");
        volumeObject.transform.SetParent(transform, false);

        blurVolume = volumeObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 1f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField depthOfField = profile.Add<DepthOfField>(true);
        depthOfField.active = true;
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(0f);
        depthOfField.gaussianEnd.Override(blurDistance);
        depthOfField.gaussianMaxRadius.Override(blurRadius);
        depthOfField.highQualitySampling.Override(true);
        blurVolume.profile = profile;
    }

    static bool EnterPressedThisFrame()
    {
        return GameInput.ConfirmPressedThisFrame;
    }

    static bool ScreenPressedThisFrame()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        Touchscreen touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        Pen pen = Pen.current;
        if (pen != null && pen.tip.wasPressedThisFrame)
        {
            return true;
        }

        return false;
    }

    void CacheControlScreens()
    {
        boxControls = null;
        xboxControls = null;
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.gameObject == gameObject)
            {
                continue;
            }

            string name = canvas.gameObject.name;
            if (IsXboxControlsName(name))
            {
                xboxControls = canvas.gameObject;
            }
            else if (IsBoxControlsName(name))
            {
                boxControls = canvas.gameObject;
            }
        }
    }

    static bool IsXboxControlsName(string objectName)
    {
        string name = NormalizeScreenName(objectName);
        return name.Contains("control") && (name.Contains("xbox") || name.Contains("xobx"));
    }

    static bool IsBoxControlsName(string objectName)
    {
        string name = NormalizeScreenName(objectName);
        return name.Contains("control") && name.Contains("box") && !name.Contains("xbox") && !name.Contains("xobx");
    }

    static string NormalizeScreenName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return string.Empty;
        }

        return objectName.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }

    void HideControlScreens()
    {
        if (boxControls != null)
        {
            boxControls.SetActive(false);
        }

        if (xboxControls != null)
        {
            xboxControls.SetActive(false);
        }
    }

    void OnStartPressed()
    {
        if (phase != ScreenPhase.Title)
        {
            return;
        }

        ShowControls();
    }

    void ShowControls()
    {
        phase = ScreenPhase.Controls;
        controlsOpenedFrame = Time.frameCount;
        if (startButton != null)
        {
            startButton.interactable = false;
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        bool useBox = ArduinoController.IsDeviceConnected;
        activeControls = useBox ? boxControls : xboxControls;
        if (activeControls == null)
        {
            Debug.LogWarning(useBox
                ? "StartScreen: custom controller is connected, but ControlsScreen[box] was not found."
                : "StartScreen: ControlsScreen[xbox] was not found.");
            BeginGame();
            return;
        }

        activeControls.SetActive(true);
        Canvas canvas = activeControls.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 50;
        }
    }

    void BeginGame()
    {
        if (phase == ScreenPhase.Starting)
        {
            return;
        }

        phase = ScreenPhase.Starting;
        if (activeControls != null)
        {
            activeControls.SetActive(false);
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        ExperienceRestart.NotifyStarted();
        StartCoroutine(UnblurAndHide());
    }

    IEnumerator UnblurAndHide()
    {
        float duration = Mathf.Max(0.05f, unblurSeconds);
        float elapsed = 0f;
        float startWeight = blurVolume != null ? blurVolume.weight : 1f;
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            if (blurVolume != null)
            {
                blurVolume.weight = Mathf.Lerp(startWeight, 0f, eased);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            }

            yield return null;
        }

        if (blurVolume != null)
        {
            blurVolume.weight = 0f;
            blurVolume.enabled = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        gameObject.SetActive(false);
    }
}
