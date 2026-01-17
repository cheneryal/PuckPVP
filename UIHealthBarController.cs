using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTP
{
    public class UIHealthBarController : MonoBehaviour
    {
        private UIHealthBar uiHealthBar;
        private bool hasTarget;
        private ulong targetClientId;
        private CTP_PlayerHealth targetHealth;

        void Awake()
        {
            uiHealthBar = gameObject.AddComponent<UIHealthBar>();
            // Start Hidden
            uiHealthBar.Hide(); 
        }

        void Start()
        {
            // Game Events
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_OnPlayerBodySpawned", new Action<Dictionary<string, object>>(this.Event_OnPlayerBodySpawned));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(this.Event_Client_OnHealthChanged));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnPlayerCameraEnabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraEnabled));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnPlayerCameraDisabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraDisabled));

            // Listen for Ping Success
            CTP_HealthSyncer.OnModDetected += OnModDetectedHandler;
        }

        void OnDestroy()
        {
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_OnPlayerBodySpawned", new Action<Dictionary<string, object>>(this.Event_OnPlayerBodySpawned));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(this.Event_Client_OnHealthChanged));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnPlayerCameraEnabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraEnabled));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnPlayerCameraDisabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraDisabled));
            
            CTP_HealthSyncer.OnModDetected -= OnModDetectedHandler;
        }

        private void OnModDetectedHandler()
        {
            // The server is valid!
            // If we already have the player loaded, show the UI now.
            if (this.hasTarget && this.targetHealth != null)
            {
                this.uiHealthBar.Show();
                this.uiHealthBar.SetHealth(this.targetHealth.CurrentHP, this.targetHealth.MaxHP);
            }
        }

        private void Event_OnPlayerBodySpawned(Dictionary<string, object> message)
        {
            PlayerBodyV2 playerBodyV = (PlayerBodyV2)message["playerBody"];
            if (!playerBodyV.Player.IsLocalPlayer) return;

            // Always cache the local player references
            this.hasTarget = true;
            this.targetClientId = playerBodyV.OwnerClientId;
            this.targetHealth = playerBodyV.GetComponentInParent<CTP_PlayerHealth>();

            // Only show if verification has already happened
            if (CTP_HealthSyncer.ServerHasMod)
            {
                this.uiHealthBar.Show();
                if (this.targetHealth != null)
                {
                    this.uiHealthBar.SetHealth(this.targetHealth.CurrentHP, this.targetHealth.MaxHP);
                }
            }
        }

        private void Event_Client_OnHealthChanged(Dictionary<string, object> message)
        {
            if (!CTP_HealthSyncer.ServerHasMod) return;

            ulong clientId = (ulong)message["clientId"];
            float newHP = (float)message["newHP"];
            float maxHP = (float)message["maxHP"];

            if (!this.hasTarget || clientId != this.targetClientId) return;

            this.uiHealthBar.SetHealth(newHP, maxHP);
        }

        private void Event_Client_OnPlayerCameraEnabled(Dictionary<string, object> message)
        {
            PlayerCamera playerCamera = (PlayerCamera)message["playerCamera"];
            if (!this.hasTarget || playerCamera.OwnerClientId != this.targetClientId) return;

            if (CTP_HealthSyncer.ServerHasMod)
            {
                this.uiHealthBar.Show();
            }
        }

        private void Event_Client_OnPlayerCameraDisabled(Dictionary<string, object> message)
        {
            PlayerCamera playerCamera = (PlayerCamera)message["playerCamera"];
            if (!this.hasTarget || playerCamera.OwnerClientId != this.targetClientId) return;

            this.uiHealthBar.Hide();
        }
    }
}