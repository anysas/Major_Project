using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TrashCube : MonoBehaviour
{
    Rigidbody rb;
    TrashPiles piles;
    int cornerIndex = -1;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = RigidbodyConstraints.None;

        Collider cubeCollider = GetComponent<Collider>();
        if (cubeCollider != null)
        {
            cubeCollider.isTrigger = true;
        }
    }

    public void Attach(TrashPiles owner, int index)
    {
        piles = owner;
        cornerIndex = index;
    }

    void OnTriggerEnter(Collider other)
    {
        Notify(other, true);
    }

    void OnTriggerStay(Collider other)
    {
        Rigidbody otherBody = CarBody(other);
        if (otherBody == null)
        {
            return;
        }

        piles.PushBlock(cornerIndex, otherBody);
    }

    void OnTriggerExit(Collider other)
    {
        Notify(other, false);
    }

    void Notify(Collider other, bool overlapping)
    {
        if (piles == null || cornerIndex < 0)
        {
            return;
        }

        Rigidbody otherBody = CarBody(other);
        if (otherBody == null)
        {
            return;
        }

        piles.SetBlockContact(cornerIndex, overlapping);
    }

    Rigidbody CarBody(Collider other)
    {
        if (piles == null || cornerIndex < 0 || other == null)
        {
            return null;
        }

        Rigidbody otherBody = other.attachedRigidbody;
        if (otherBody == null || otherBody.GetComponent<CarController>() == null)
        {
            return null;
        }

        return otherBody;
    }
}
