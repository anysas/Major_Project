using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [SerializeField] float maxSpeed = 14f;
    [SerializeField] float acceleration = 70f;
    [SerializeField] float deceleration = 80f;
    [SerializeField] float turnSpeed = 240f;

    Rigidbody rb;
    Transform viewCamera;
    float stunLeft;

    public Vector3 DriveVelocity { get; private set; }

    public bool IsStunned
    {
        get { return stunLeft > 0f; }
    }

    public void Stun(float seconds)
    {
        stunLeft = Mathf.Max(stunLeft, Mathf.Max(0f, seconds));
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = 1500f;
        rb.centerOfMass = new Vector3(0f, -0.5f, 0f);
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        viewCamera = Camera.main != null ? Camera.main.transform : null;
    }

    void Update()
    {
        if (stunLeft > 0f)
        {
            stunLeft -= Time.deltaTime;
        }
    }

    void FixedUpdate()
    {
        if (ExperienceRestart.IsEnded || stunLeft > 0f)
        {
            DriveVelocity = Vector3.zero;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        Vector3 moveDir = CameraRelativeDirection(ReadMoveInput());
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

        if (moveDir.sqrMagnitude > 0.01f)
        {
            float speed = Mathf.MoveTowards(horizontal.magnitude, maxSpeed, acceleration * Time.fixedDeltaTime);
            horizontal = moveDir * speed;

            Quaternion targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
        else
        {
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, deceleration * Time.fixedDeltaTime);
        }

        DriveVelocity = horizontal;
        rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    static Vector3 ReadMoveInput()
    {
        float x = 0f;
        float z = 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed)
            {
                x -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                x += 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                z += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                z -= 1f;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            x += stick.x;
            z += stick.y;
        }

        Vector3 input = new Vector3(x, 0f, z);
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
