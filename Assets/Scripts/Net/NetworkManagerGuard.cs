using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Net
{
    // The NetworkManager lives in the house scene and survives reloads (DontDestroyOnLoad).
    // When the scene reloads (back to the menu), the scene's fresh copy removes itself
    // before it can register as a second NetworkManager.
    [DefaultExecutionOrder(-10000)]
    public class NetworkManagerGuard : MonoBehaviour
    {
        void Awake()
        {
            var nm = GetComponent<NetworkManager>();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton != nm)
                DestroyImmediate(gameObject);
        }
    }
}
