using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

public static class GameInput
{
    static Xboxcontroller instance;

    // Xbox holds the right trigger for the arm and toggles headlights with B.
    // Keyboard holds the arm and toggles headlights. Arduino switches stay on only while held.
    static bool headlightLatched;

    public static Xboxcontroller Actions
    {
        get
        {
            Ensure();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unhook();
        instance?.Dispose();
        instance = null;
        headlightLatched = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    public static void Ensure()
    {
        if (instance != null)
        {
            return;
        }

        instance = new Xboxcontroller();
        Hook();
        instance.Enable();
    }

    public static Vector2 Move
    {
        get
        {
            InputAction action = Actions.Truck.Move;
            Vector2 pad = action != null ? action.ReadValue<Vector2>() : Vector2.zero;
            Vector2 combined = pad + ArduinoController.Move;
            if (combined.sqrMagnitude > 1f)
            {
                combined.Normalize();
            }

            return combined;
        }
    }

    public static bool ArmUpHeld =>
        ArduinoController.ArmUpHeld || ActionHeld(Actions.Truck.ArmUp);

    public static bool HeadlightHeld => ArduinoController.HeadlightHeld || headlightLatched;

    public static bool HornPressedThisFrame =>
        ActionPressedThisFrame(Actions.Truck.Horn) || ArduinoController.HornPressedThisFrame;

    public static bool ConfirmPressedThisFrame =>
        ActionPressedThisFrame(Actions.Truck.Confirm) ||
        ArduinoController.ConfirmPressedThisFrame ||
        GamepadPressedThisFrame();

    static void Hook()
    {
        InputAction headlight = instance.Truck.Headlight;
        if (headlight != null)
        {
            headlight.performed += OnHeadlightPressed;
        }
    }

    static void Unhook()
    {
        if (instance == null)
        {
            return;
        }

        InputAction headlight = instance.Truck.Headlight;
        if (headlight != null)
        {
            headlight.performed -= OnHeadlightPressed;
        }
    }

    static void OnHeadlightPressed(InputAction.CallbackContext context)
    {
        if (!ExperienceRestart.IsActive)
        {
            return;
        }

        if (IsKeyboardOrGamepad(context))
        {
            headlightLatched = !headlightLatched;
        }
    }

    static bool IsKeyboardOrGamepad(InputAction.CallbackContext context)
    {
        InputDevice device = context.control != null ? context.control.device : null;
        return device is Keyboard || device is Gamepad;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        headlightLatched = false;
    }

    static bool ActionHeld(InputAction action)
    {
        return action != null && action.IsPressed();
    }

    static bool GamepadPressedThisFrame()
    {
        foreach (Gamepad gamepad in Gamepad.all)
        {
            if (gamepad == null)
            {
                continue;
            }

            foreach (InputControl control in gamepad.allControls)
            {
                if (control is ButtonControl button && button.wasPressedThisFrame)
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool ActionPressedThisFrame(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }
}
