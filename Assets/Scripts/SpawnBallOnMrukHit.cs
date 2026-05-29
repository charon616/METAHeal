using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns the next beach ball when this one hits an MRUK wall/floor surface, chaining active spawner focus.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SpawnBallOnMrukHit : MonoBehaviour
{
    static readonly Dictionary<Transform, Queue<GameObject>> s_spawnedByParent = new();
    static float s_nextGlobalSpawnTime;
    static SpawnBallOnMrukHit s_activeSpawner;

    [Header("Spawn")]
    [SerializeField] GameObject ballPrefab;
    [SerializeField] Transform spawnParent;
    [SerializeField] float spawnOffsetFromSurface = 0.03f;
    [Tooltip("Max children under spawnParent; oldest removed when exceeded. 0 or less = unlimited.")]
    [SerializeField] int maxChildrenUnderSpawnParent = 5;

    [Header("Velocity")]
    [SerializeField] bool inheritReflectedVelocity = true;
    [SerializeField] float reflectedSpeedMultiplier = 1f;
    [SerializeField] float minReflectedSpeed = 0.5f;
    [SerializeField] float maxReflectedSpeed = 10f;

    [Header("Cooldown")]
    [SerializeField] float localSpawnCooldownSeconds = 0.08f;
    [SerializeField] float globalSpawnCooldownSeconds = 5f;
    [SerializeField] bool onlyActiveInstanceCanSpawn = true;

    Rigidbody _rb;
    float _nextAllowedLocalSpawnTime;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        if (s_activeSpawner == null)
        {
            s_activeSpawner = this;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!ballPrefab)
        {
            return;
        }
        if (onlyActiveInstanceCanSpawn && s_activeSpawner != null && s_activeSpawner != this)
        {
            return;
        }
        if (Time.time < _nextAllowedLocalSpawnTime || Time.time < s_nextGlobalSpawnTime)
        {
            return;
        }
        if (!TryGetMrukSurfaceAnchor(collision, out var anchor) || !IsFloorOrWall(anchor))
        {
            return;
        }

        ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 normal = contact.normal.sqrMagnitude > 1e-6f ? contact.normal.normalized : Vector3.up;
        Vector3 point = collision.contactCount > 0 ? contact.point : transform.position;
        Vector3 spawnPos = point + normal * spawnOffsetFromSurface;

        Transform parent = GetEffectiveSpawnParent();
        GameObject spawned = Instantiate(ballPrefab, spawnPos, Quaternion.identity, parent);
        RegisterSpawned(parent, spawned);
        ApplyVelocity(spawned, normal, collision);

        _nextAllowedLocalSpawnTime = Time.time + Mathf.Max(0f, localSpawnCooldownSeconds);
        s_nextGlobalSpawnTime = Time.time + Mathf.Max(0f, globalSpawnCooldownSeconds);

        if (onlyActiveInstanceCanSpawn &&
            spawned != null &&
            spawned.TryGetComponent<SpawnBallOnMrukHit>(out var nextSpawner))
        {
            s_activeSpawner = nextSpawner;
        }
    }

    void ApplyVelocity(GameObject spawned, Vector3 normal, Collision collision)
    {
        if (!inheritReflectedVelocity || spawned == null || !spawned.TryGetComponent<Rigidbody>(out var newRb))
        {
            return;
        }

        Vector3 sourceVelocity = GetCurrentVelocity(collision);
        Vector3 reflected = Vector3.Reflect(sourceVelocity, normal);
        float speed = Mathf.Clamp(reflected.magnitude * reflectedSpeedMultiplier, minReflectedSpeed, maxReflectedSpeed);
        Vector3 dir = reflected.sqrMagnitude > 1e-6f ? reflected.normalized : normal;
#if UNITY_6000_0_OR_NEWER
        newRb.linearVelocity = dir * speed;
#else
        newRb.velocity = dir * speed;
#endif
    }

    Vector3 GetCurrentVelocity(Collision collision)
    {
        if (_rb)
        {
#if UNITY_6000_0_OR_NEWER
            return _rb.linearVelocity;
#else
            return _rb.velocity;
#endif
        }
        return collision.relativeVelocity;
    }

    Transform GetEffectiveSpawnParent() => spawnParent ? spawnParent : transform.parent;

    void RegisterSpawned(Transform parent, GameObject spawned)
    {
        if (parent == null || spawned == null)
        {
            return;
        }
        if (!s_spawnedByParent.TryGetValue(parent, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            s_spawnedByParent[parent] = queue;
        }
        queue.Enqueue(spawned);
        PruneParentChildrenIfNeeded(queue);
    }

    void PruneParentChildrenIfNeeded(Queue<GameObject> queue)
    {
        if (queue == null || maxChildrenUnderSpawnParent <= 0)
        {
            return;
        }
        while (queue.Count > 0 && queue.Peek() == null)
        {
            queue.Dequeue();
        }
        while (queue.Count > maxChildrenUnderSpawnParent)
        {
            GameObject oldest = queue.Dequeue();
            if (oldest != null)
            {
                Destroy(oldest);
            }
        }
    }

    static bool TryGetMrukSurfaceAnchor(Collision collision, out MRUKAnchor anchor)
    {
        anchor = null;
        if (collision.collider && collision.collider.TryGetComponent<MRUKAnchor>(out var direct))
        {
            anchor = direct;
            return true;
        }
        if (collision.collider)
        {
            anchor = collision.collider.GetComponentInParent<MRUKAnchor>();
            return anchor;
        }
        return false;
    }

    static bool IsFloorOrWall(MRUKAnchor anchor)
    {
        if (!anchor)
        {
            return false;
        }
        return anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR) ||
               anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE) ||
               anchor.HasAnyLabel(MRUKAnchor.SceneLabels.INNER_WALL_FACE) ||
               anchor.HasAnyLabel(MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE);
    }
}
