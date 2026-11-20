using UnityEngine;

/// <summary>
/// Henüz yapılmamış bir adanın ayrılmış yerini sahnede gösterir (sadece editörde görünür).
/// Ada inşa edilince bu objeyi silip yerine adayı koyabilirsin.
/// </summary>
public class PlannedIslandMarker : MonoBehaviour
{
    public string islandName = "Trade Island";
    public Vector2 size = new Vector2(24f, 20f);
    public Color color = new Color(0.2f, 0.8f, 1f, 0.6f);

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(size.x, 1f, size.y));
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, islandName);
    }
#endif
}
