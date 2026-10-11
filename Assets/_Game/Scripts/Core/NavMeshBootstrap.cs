using Unity.AI.Navigation;
using UnityEngine;

// Fallback: if the baked NavMesh asset is missing, bake at load before any agent wakes up.
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(NavMeshSurface))]
public class NavMeshBootstrap : MonoBehaviour
{
    void Awake()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface.navMeshData == null) surface.BuildNavMesh();
    }
}
