using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vagonların izleyeceği sıralı rota noktaları (yükleme istasyonu -> ticaret adası).
/// Şimdilik sadece veri + gizmo; vagon hareket sistemi ileride bunu kullanacak.
/// Rota noktaları bu objenin çocuklarıdır, sıraları hiyerarşi sırasıdır.
/// </summary>
public class WagonRoute : MonoBehaviour
{
    [Tooltip("Bu rota şu an kullanılabilir mi? (ör. ray köprüsü tamir edilince true)")]
    public bool routeOpen;
    [Tooltip("Rota açıldığında aktif edilecek obje (ör. RailBridge_ToTradeIsland)")]
    public GameObject requiredConnection;

    public int Count => transform.childCount;

    public Transform GetWaypoint(int index) => transform.GetChild(index);

    public void GetPoints(List<Vector3> result)
    {
        result.Clear();
        for (int i = 0; i < transform.childCount; i++)
            result.Add(transform.GetChild(i).position);
    }

    public float Length
    {
        get
        {
            float len = 0f;
            for (int i = 1; i < transform.childCount; i++)
                len += Vector3.Distance(transform.GetChild(i - 1).position, transform.GetChild(i).position);
            return len;
        }
    }

    /// <summary>Rota üzerinde 0..1 arası normalize konum.</summary>
    public Vector3 Evaluate(float t)
    {
        int n = transform.childCount;
        if (n == 0) return transform.position;
        if (n == 1) return transform.GetChild(0).position;

        float target = Mathf.Clamp01(t) * Length;
        for (int i = 1; i < n; i++)
        {
            Vector3 a = transform.GetChild(i - 1).position;
            Vector3 b = transform.GetChild(i).position;
            float seg = Vector3.Distance(a, b);
            if (target <= seg || i == n - 1)
                return Vector3.Lerp(a, b, seg > 0f ? Mathf.Clamp01(target / seg) : 1f);
            target -= seg;
        }
        return transform.GetChild(n - 1).position;
    }

    public void SetOpen(bool open)
    {
        routeOpen = open;
        if (requiredConnection) requiredConnection.SetActive(open);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = routeOpen ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.8f, 0.2f, 0.35f);
        for (int i = 0; i < transform.childCount; i++)
        {
            Vector3 p = transform.GetChild(i).position;
            Gizmos.DrawSphere(p, 0.25f);
            if (i > 0) Gizmos.DrawLine(transform.GetChild(i - 1).position, p);
        }
    }
}
