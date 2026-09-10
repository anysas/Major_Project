using UnityEngine;
using UnityEngine.InputSystem;

public static class GameInput
{
    static Xboxcontroller instance;

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
        instance?.Dispose();
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Ensure();
    }

    public static void Ensure()
    {
        if (instance != null)
        {
            return;
        }

        instance = new Xboxcontroller();
        instance.Enable();
    }

    public static Vector2 Move
    {
        get
        {
            InputAction action = Actions.Truck.Move;
            return action != null ? action.ReadValue<Vector2>() : Vector2.zero;
        }
    }

    public static bool ArmUpHeld => ActionHeld(Actions.Truck.ArmUp);

    public static bool HeadlightHeld => ActionHeld(Actions.Truck.Headlight);

    public static bool HornPressedThisFrame => ActionPressedThisFrame(Actions.Truck.Horn);

    public static bool ConfirmPressedThisFrame => ActionPressedThisFrame(Actions.Truck.Confirm);

    static bool ActionHeld(InputAction action)
    {
        return action != null && action.IsPressed();
    }

    static bool ActionPressedThisFrame(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }
}
