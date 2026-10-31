using System.Collections;
using UnityEngine;

public class PortalWallHit : MonoBehaviour
{
    [Header("Portal Prefab")]
    public GameObject portalPrefab;

    [Header("Portal Open Size")]
    public float openSize = 2.5f;

    [Header("Portal Open Time")]
    public float openTime = 0.6f;

    [Header("Wall Offset")]
    public float offset = 0.03f;
    [Header("Portal Rotation Offset")]
    public Vector3 rotationOffset;

    private bool hasCreatedPortal = false;
    private BoxCollider wallCollider;

    private void Awake()
    {
        wallCollider = GetComponent<BoxCollider>();
    }

    private void Update()
    {
        if (hasCreatedPortal || wallCollider == null)
            return;

        Vector3 halfExtents = Vector3.Scale(wallCollider.size, transform.lossyScale) * 0.5f;
        halfExtents = Vector3.Max(halfExtents, new Vector3(0.05f, 0.05f, 0.05f));

        Collider[] hits = Physics.OverlapBox(
            wallCollider.bounds.center,
            halfExtents,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (hit.gameObject == gameObject)
                continue;

            if (!IsPortalActivator(hit.gameObject))
                continue;

            Vector3 normal = transform.up;
            Vector3 spawnPos = wallCollider.ClosestPoint(hit.bounds.center) + normal * offset;
            CreatePortal(spawnPos, normal);
            return;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasCreatedPortal)
            return;

        if (!IsPortalActivator(collision.gameObject))
            return;

        ContactPoint contact = collision.contacts[0];
        Vector3 spawnPos = contact.point + contact.normal * offset;
        CreatePortal(spawnPos, contact.normal);
    }

    private bool IsPortalActivator(GameObject other)
    {
        GameObject root = other.transform.root.gameObject;
        return other.CompareTag("Player")
            || root.CompareTag("Player")
            || other.name.Contains("whaelblend")
            || root.name.Contains("whaelblend");
    }

    private void CreatePortal(Vector3 spawnPos, Vector3 wallNormal)
    {
        GameObject portal = Instantiate(portalPrefab, spawnPos, Quaternion.identity);
        Quaternion lookRotation = Quaternion.LookRotation(-wallNormal);
        portal.transform.rotation = lookRotation * Quaternion.Euler(rotationOffset);
        StartCoroutine(OpenPortal(portal.transform));
        hasCreatedPortal = true;
    }

    private IEnumerator OpenPortal(Transform portal)
    {
        Vector3 startScale = new Vector3(0f, portal.localScale.y, 0f);
        Vector3 targetScale = new Vector3(openSize, portal.localScale.y, openSize);

        portal.localScale = startScale;

        float timer = 0f;

        while (timer < openTime)
        {
            timer += Time.deltaTime;
            float t = timer / openTime;
            t = Mathf.SmoothStep(0f, 1f, t);

            portal.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }

        portal.localScale = targetScale;
    }
}