using UnityEngine;

public class Collectible : MonoBehaviour
{
    public CollectibleType collectibleType;

    void Start()
    {
        
    }

    
    void Update()
    {
        
    }
}

public enum CollectibleType
{
    Stone,
    Wood,
    Food,
    Gold,
    Diamond,
}
