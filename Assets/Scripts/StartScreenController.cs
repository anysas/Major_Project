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

    CanvasGroup canvasGroup;
    Volume blurVolume;
    bool starting;

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
    }

    void Update()
    {
        if (starting)
        {
            return;
        }

        if (EnterPressedThisFrame() || ScreenPressedThisFrame())
        {
            OnStartPressed();
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

    void OnStartPressed()
    {
        if (starting)
        {
            return;
        }

        starting = true;
        if (startButton != null)
        {
            startButton.interactable = false;
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
