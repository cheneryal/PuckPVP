using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    public class CTP_HealthSyncer : NetworkBehaviour
    {
        public static CTP_HealthSyncer Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

        [ClientRpc]
        public void UpdateHealth_ClientRpc(ulong clientId, float newHP)
        {
            // This runs on the Client (and Host).
            // Find the player object associated with the clientId
            var playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager != null)
            {
                var player = playerManager.GetPlayerByClientId(clientId);
                if (player != null)
                {
                    var hp = player.GetComponent<CTP_PlayerHealth>();
                    if (hp != null)
                    {
                        // Update the local data and fire the HUD event
                        hp.SetHPClientSide(newHP);
                    }
                }
            }
        }
    }
}