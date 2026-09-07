using System.Collections.Generic;
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
    [SerializeField] float wheelRadius = 0.75f;
    [SerializeField] float armRestX = -100f;
    [SerializeField] float armRaisedX = -145f;
    [SerializeField] float armRotateSpeed = 120f;

    Rigidbody rb;
    Transform viewCamera;
    Transform[] wheels;
    float[] wheelRadii;
    Transform arm;
    float armX;
    float armBaseY;
    float armBaseZ;
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
        CollectWheels();
        CollectArm();
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

        Vector3 euler = arm.localEulerAngles;
        armBaseY = euler.y;
        armBaseZ = euler.z;
        armX = armRestX;
        ApplyArmRotation();
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
        if (stunLeft > 0f)
        {
            stunLeft -= Time.deltaTime;
        }

        UpdateArm();
        SpinWheels();
    }

    void UpdateArm()
    {
        if (arm == null)
        {
            return;
        }

        bool raise = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.upArrowKey.isPressed)
        {
            raise = true;
        }

        float target = raise ? armRaisedX : armRestX;
        armX = Mathf.MoveTowards(armX, target, armRotateSpeed * Time.deltaTime);
        ApplyArmRotation();
    }

    void ApplyArmRotation()
    {
        arm.localRotation = Quaternion.Euler(armX, armBaseY, armBaseZ);
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
