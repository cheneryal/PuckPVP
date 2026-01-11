using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

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
            }
        }

        [ClientRpc]
        public void UpdateHealth_ClientRpc(ulong clientId, float newHP)
        {
            var playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager != null)
            {
                var player = playerManager.GetPlayerByClientId(clientId);
                if (player != null)
                {
                    var hp = player.GetComponent<CTP_PlayerHealth>();
                    if (hp != null)
                    {
                        hp.SetHPClientSide(newHP);
                    }
                }
            }
        }
    }
}
