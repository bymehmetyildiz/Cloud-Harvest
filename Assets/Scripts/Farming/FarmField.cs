using UnityEngine;

public enum CropType
{
    Wheat,
    Carrot,
    Cabbage,
    Pumpkin,
    Watermelon,
}

/// <summary>
/// Tarla adasındaki tek bir tarla parseli.
/// Kilitli halde: çimen, yabani ot, kütük (temizlenmesi gereken alan).
/// Açık halde: sürülmüş toprak, ekin sıraları ve çit.
/// İleride yükseltme sistemi sadece SetUnlocked(true) çağıracak.
/// </summary>
public class FarmField : MonoBehaviour
{
    [Tooltip("Kayıt / save sistemi için benzersiz kimlik (örn. Field_02)")]
    public string fieldId;
    public CropType crop;
    [Tooltip("Açılış sırası: 1 = başlangıçta açık, sonrakiler yükseltmeyle açılır")]
    public int unlockOrder = 1;
    public bool unlocked;

    [Header("States")]
    public GameObject lockedState;
    public GameObject unlockedState;

    [Header("Hooks")]
    [Tooltip("Ekinlerin bulunduğu kök. Hasat / büyüme sistemi buradan okuyabilir.")]
    public Transform cropsRoot;
    [Tooltip("Hasat edilen ürünün vagona taşınmak için bırakılacağı nokta")]
    public Transform dropOffPoint;

    void Awake() => Apply();

    /// <summary>Kilitli parselde hâlâ toplanmamış (aktif) odun / taş / mantar sayısı.</summary>
    public int RemainingCollectibles =>
        lockedState ? lockedState.GetComponentsInChildren<Collectible>(false).Length : 0;

    /// <summary>Tüm toplanabilirler toplandıysa parsel ekime açılabilir.</summary>
    public bool IsCleared => RemainingCollectibles == 0;

    public void SetUnlocked(bool value)
    {
        unlocked = value;
        Apply();
    }

    void Apply()
    {
        if (lockedState) lockedState.SetActive(!unlocked);
        if (unlockedState) unlockedState.SetActive(unlocked);
    }

    [ContextMenu("Unlock")]
    void UnlockFromMenu() => SetUnlocked(true);

    [ContextMenu("Lock")]
    void LockFromMenu() => SetUnlocked(false);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (dropOffPoint)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(dropOffPoint.position, 0.4f);
        }
    }
#endif
}
