using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CTP
{
    public enum FallReason
    {
        None,
        Eliminated,
        Drowned
    }

    public static class CTP_KnockdownManager
    {
        public static readonly Dictionary<ulong, FallReason> fallenPlayers = new Dictionary<ulong, FallReason>();
        public static readonly HashSet<ulong> SuppressedStandUp = new HashSet<ulong>();

        public static void KnockdownPlayer(Player player, FallReason reason)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (player == null || player.PlayerBody == null) return;

            ulong clientId = player.OwnerClientId;

            if (fallenPlayers.ContainsKey(clientId) && fallenPlayers[clientId] != FallReason.None) return;

            fallenPlayers[clientId] = reason;
            SuppressedStandUp.Add(clientId);

            player.PlayerBody.OnSlip();
            
            if (CTPEnforcer.Instance != null)
            {
                CTPEnforcer.Instance.StartDelayedFreeze(player.PlayerBody, 1.5f);
            }

            Debug.Log($"[CTP] Player {player.Username.Value} knocked down - Reason: {reason}");
        }

        public static void RevivePlayer(ulong clientId)
        {
             if (!NetworkManager.Singleton.IsServer) return;

            var playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager == null) return;

            Player player = playerManager.GetPlayerByClientId(clientId);
            if (player == null) return;
            
            var hp = player.GetComponent<CTP_PlayerHealth>();
            if (hp != null)
            {
                hp.ResetHealth();
            }

            if(player.PlayerBody != null)
            {
                player.PlayerBody.Server_Unfreeze();
                fallenPlayers[clientId] = FallReason.None;
                SuppressedStandUp.Remove(clientId);

                player.PlayerBody.OnStandUp();
            }
           
            Debug.Log($"[CTP] Revived player {player.Username.Value}");
        }

        public static bool IsPlayerFallen(ulong clientId)
        {
            return fallenPlayers.ContainsKey(clientId) && fallenPlayers[clientId] != FallReason.None;
        }
    }
}
