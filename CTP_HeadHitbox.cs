using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    public class CTP_HeadHitbox : MonoBehaviour
    {
        public CTP_PlayerHealth parentHealth;
        private const float FATAL_VELOCITY_THRESHOLD = 3.0f;

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (parentHealth == null || parentHealth.IsDead) return;

            Puck puck = other.GetComponentInParent<Puck>();
            if (puck != null)
            {
                Rigidbody puckRb = puck.GetComponent<Rigidbody>();
                if (puckRb != null)
                {
                    if (puckRb.linearVelocity.magnitude > FATAL_VELOCITY_THRESHOLD)
                    {
                        HandleHeadshot(puck);
                    }
                }
            }
        }

        private void HandleHeadshot(Puck puck)
        {
            var player = parentHealth.GetComponent<Player>();
            if (player == null) return;

            // --- TANK LOGIC START ---
            // Goalies are immune to the instant-kill headshot mechanic.
            if (player.Role.Value == PlayerRole.Goalie)
            {
                // Optional: Play a metallic 'dink' sound here to indicate deflection?
                return; 
            }
            // --- TANK LOGIC END ---

            Debug.Log($"[CTP] HEADSHOT! Player eliminated by puck.");

            var uiChat = UnityEngine.Object.FindFirstObjectByType<UIChat>();
            if (uiChat != null)
            {
                string playerName = player.Username.Value.ToString(); 
                uiChat.Server_SendSystemChatMessage($"<color=red><b>HEADSHOT!</b> {playerName} was knocked out by a puck!</color>");
            }

            parentHealth.TakeDamage(9999f);
        }
    }
}