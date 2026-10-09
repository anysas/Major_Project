using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class GameInput
{
    static Xboxcontroller instance;

    // Xbox latches the arm and headlights. Keyboard latches headlights and holds the arm.
    // Arduino switches stay on only while held.
    static bool armLatched;
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
        armLatched = false;
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
        ArduinoController.ArmUpHeld || KeyboardHeld(Actions.Truck.ArmUp) || armLatched;

    public static bool HeadlightHeld => ArduinoController.HeadlightHeld || headlightLatched;

    public static bool HornPressedThisFrame =>
        ActionPressedThisFrame(Actions.Truck.Horn) || ArduinoController.HornPressedThisFrame;

    public static bool ConfirmPressedThisFrame =>
        ActionPressedThisFrame(Actions.Truck.Confirm) || ArduinoController.ConfirmPressedThisFrame;

    static void Hook()
    {
        InputAction arm = instance.Truck.ArmUp;
        InputAction headlight = instance.Truck.Headlight;
        if (arm != null)
        {
            arm.performed += OnArmPressed;
        }

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

        InputAction arm = instance.Truck.ArmUp;
        InputAction headlight = instance.Truck.Headlight;
        if (arm != null)
        {
            arm.performed -= OnArmPressed;
        }

        if (headlight != null)
        {
            headlight.performed -= OnHeadlightPressed;
        }
    }

    static void OnArmPressed(InputAction.CallbackContext context)
    {
        if (context.control != null && context.control.device is Gamepad)
        {
            armLatched = !armLatched;
        }
    }

    static void OnHeadlightPressed(InputAction.CallbackContext context)
    {
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
        armLatched = false;
        headlightLatched = false;
    }

    static bool KeyboardHeld(InputAction action)
    {
        if (action == null)
        {
            return false;
        }

        var controls = action.controls;
        for (int i = 0; i < controls.Count; i++)
        {
            InputControl control = controls[i];
            if (control != null && control.device is Keyboard && control.IsPressed())
            {
                return true;
            }
        }

        return false;
    }

    static bool ActionPressedThisFrame(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }
}
