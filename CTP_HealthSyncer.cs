using UnityEngine;
using Unity.Netcode;
using System;

namespace CTP
{
    public class CTP_HealthSyncer : MonoBehaviour
    {
        public static CTP_HealthSyncer Instance { get; private set; }

        public static bool ServerHasMod { get; private set; } = false;
        public static event Action OnModDetected;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                // We do NOT use DontDestroyOnLoad here. 
                // We want this to be destroyed/recreated on every server join 
                // to reset the state cleanly.
            }
        }

        void Start()
        {
            ServerHasMod = false;

            if (NetworkManager.Singleton == null) return;

            // 1. If we are the Server (Host), we definitely have the mod.
            if (NetworkManager.Singleton.IsServer)
            {
                ServerHasMod = true;
                OnModDetected?.Invoke();
                
                // Listen for Pings from other clients
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("CTP_Ping", Server_OnPingReceived);
            }
            // 2. If we are a Client, we need to verify the server.
            else if (NetworkManager.Singleton.IsClient)
            {
                // Listen for the Pong response
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("CTP_Pong", Client_OnPongReceived);
                
                // Send the Ping
                Client_SendPing();
            }
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("CTP_Ping");
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("CTP_Pong");
            }
        }

        // --- CLIENT LOGIC ---

        private void Client_SendPing()
        {
            if (!NetworkManager.Singleton.IsClient) return;
            
            var writer = new FastBufferWriter(0, Unity.Collections.Allocator.Temp);
            
            // FIX: Access ServerClientId via the Type, not the Singleton instance.
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("CTP_Ping", NetworkManager.ServerClientId, writer);
            
            Debug.Log("[CTP] Verification Ping sent to Server...");
        }

        private void Client_OnPongReceived(ulong senderId, FastBufferReader reader)
        {
            if (!ServerHasMod)
            {
                ServerHasMod = true;
                Debug.Log("[CTP] Server verified via Pong! Enabling CTP features.");
                OnModDetected?.Invoke();
            }
        }

        // --- SERVER LOGIC ---

        private void Server_OnPingReceived(ulong senderId, FastBufferReader reader)
        {
            // Reply to the sender with a Pong
            var writer = new FastBufferWriter(0, Unity.Collections.Allocator.Temp);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("CTP_Pong", senderId, writer);
        }
    }
}