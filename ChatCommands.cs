using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    [HarmonyPatch(typeof(UIChat), "Client_SendClientChatMessage")]
    public static class ChatCommands
    {
        [HarmonyPrefix]
        public static bool Prefix(string message, UIChat __instance)
        {
            if (string.IsNullOrEmpty(message)) return true;
            string cleanedMessage = message.Trim();
            
            // Debug Log to confirm interception
            if (cleanedMessage.StartsWith("/")) 
                Debug.Log($"[CTP] Chat Command Intercepted: {cleanedMessage}");

            if (cleanedMessage.StartsWith("/damage", StringComparison.OrdinalIgnoreCase))
            {
                float damageAmount = 25f;
                string[] parts = cleanedMessage.Split(' ');
                if (parts.Length > 1)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedDamage))
                    {
                        damageAmount = parsedDamage;
                    }
                }
                ApplyDamage(__instance, damageAmount);
                return false;
            }
            if (cleanedMessage.StartsWith("/kill", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[CTP] Executing /kill command...");
                ApplyDamage(__instance, 9999f);
                return false;
            }
            if (cleanedMessage.StartsWith("/revive", StringComparison.OrdinalIgnoreCase))
            {
                TriggerRevive();
                return false;
            }

            if (cleanedMessage.StartsWith("/sethp", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = cleanedMessage.Split(' ');
                if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float newHP))
                {
                    SetHealth(__instance, newHP);
                }
                else
                {
                    SendResponse(__instance, "Usage: /sethp <value>");
                }
                return false;
            }
            return true;
        }

        private static void SetHealth(UIChat chat, float newHP)
        {
             var allPlayers = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p.IsOwner)
                {
                    var hp = p.GetComponent<CTP_PlayerHealth>();
                    if(hp) hp.SetHPClientSide(newHP);
                    return;
                }
            }
        }

        private static void TriggerRevive()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                var allPlayers = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (var p in allPlayers)
                {
                    if (p.IsOwner)
                    {
                        CTP_KnockdownManager.RevivePlayer(p.OwnerClientId);
                        return;
                    }
                }
            }
            else
            {
                SendResponse(null, "Revive command can only be run by the host.");
            }
        }

        private static void ApplyDamage(UIChat chat, float amount)
        {
            Debug.Log($"[CTP] ApplyDamage called with amount: {amount}. IsServer: {NetworkManager.Singleton.IsServer}");
            
            // Note: In Host mode, IsServer is True and the Host Player is IsOwner = True.
            
            var allPlayers = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
            bool foundLocal = false;

            foreach (var p in allPlayers)
            {
                if (p.IsOwner) 
                {
                    foundLocal = true;
                    var hp = p.GetComponent<CTP_PlayerHealth>();
                    if(hp) 
                    {
                        Debug.Log($"[CTP] Found local player {p.Username.Value}. Applying damage.");
                        hp.TakeDamage(amount);
                    }
                    else
                    {
                        Debug.LogError($"[CTP] Local player {p.Username.Value} has NO CTP_PlayerHealth component!");
                    }
                    return;
                }
            }

            if (!foundLocal) Debug.LogError("[CTP] ApplyDamage could not find a local player to hurt.");
        }

        private static void SendResponse(UIChat chat, string message)
        {
            if (chat != null) chat.AddChatMessage(message);
        }
    }
}