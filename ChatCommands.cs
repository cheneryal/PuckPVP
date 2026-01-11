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
                ApplyDamage(__instance, 9999f);
                return false;
            }
            if (cleanedMessage.StartsWith("/revive", StringComparison.OrdinalIgnoreCase))
            {
                TriggerRevive();
                return false;
            }

            // --- New Command: /sethp ---
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
            // If I am the HOST, I can apply damage directly and it will sync.
            if (NetworkManager.Singleton.IsServer)
            {
                var allPlayers = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (var p in allPlayers)
                {
                    if (p.IsOwner) // Host hurting Host
                    {
                        var hp = p.GetComponent<CTP_PlayerHealth>();
                        if(hp) hp.TakeDamage(amount);
                        return;
                    }
                }
            }
            else
            {
                // If I am a CLIENT, I can't force the server to lower my HP easily via this specific command
                // without adding a new message type.
                // For now, /damage on Client is purely visual test.
                SendResponse(chat, "<b>[HP]</b> /damage as Client is local visual test only.");
                
                var allPlayers = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (var p in allPlayers)
                {
                    if (p.IsOwner)
                    {
                        var hp = p.GetComponent<CTP_PlayerHealth>();
                        if(hp) hp.TakeDamage(amount); // Local update
                        return;
                    }
                }
            }
        }

        private static void SendResponse(UIChat chat, string message)
        {
            if (chat != null) chat.AddChatMessage(message);
        }
    }
}