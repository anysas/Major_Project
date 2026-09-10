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
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = RigidbodyConstraints.None;
        rb.detectCollisions = true;
    }

    public void Attach(TrashPiles owner, int index)
    {
        piles = owner;
        cornerIndex = index;
    }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody otherBody = CarBody(other);
        Notify(otherBody, true);
        if (otherBody != null && piles != null)
        {
            piles.PushBlock(cornerIndex, otherBody);
        }
    }

    void OnTriggerStay(Collider other)
    {
        Rigidbody otherBody = CarBody(other);
        if (otherBody == null || piles == null)
        {
            return;
        }

        piles.PushBlock(cornerIndex, otherBody);
    }

    void OnTriggerExit(Collider other)
    {
        Notify(CarBody(other), false);
    }

    void OnCollisionStay(Collision collision)
    {
        Rigidbody otherBody = CarBody(collision);
        if (otherBody == null || piles == null)
        {
            return;
        }

        piles.PushBlock(cornerIndex, otherBody);
    }

    void Notify(Rigidbody otherBody, bool overlapping)
    {
        if (piles == null || cornerIndex < 0 || otherBody == null)
        {
            return;
        }

        piles.SetBlockContact(cornerIndex, overlapping);
    }

    Rigidbody CarBody(Collision collision)
    {
        if (collision == null || collision.collider == null)
        {
            return null;
        }

        return CarBody(collision.collider);
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
