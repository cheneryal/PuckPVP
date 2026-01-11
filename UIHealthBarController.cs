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

        void Awake()
        {
            uiHealthBar = gameObject.AddComponent<UIHealthBar>();
        }

        void Start()
        {
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_OnPlayerBodySpawned", new Action<Dictionary<string, object>>(this.Event_OnPlayerBodySpawned));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(this.Event_Client_OnHealthChanged));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnPlayerCameraEnabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraEnabled));
            MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnPlayerCameraDisabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraDisabled));
        }

        void OnDestroy()
        {
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_OnPlayerBodySpawned", new Action<Dictionary<string, object>>(this.Event_OnPlayerBodySpawned));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(this.Event_Client_OnHealthChanged));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnPlayerCameraEnabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraEnabled));
            MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnPlayerCameraDisabled", new Action<Dictionary<string, object>>(this.Event_Client_OnPlayerCameraDisabled));
        }

        private void Event_OnPlayerBodySpawned(Dictionary<string, object> message)
        {
            PlayerBodyV2 playerBodyV = (PlayerBodyV2)message["playerBody"];
            if (!playerBodyV.Player.IsLocalPlayer)
            {
                return;
            }
            this.hasTarget = true;
            this.targetClientId = playerBodyV.OwnerClientId;
            this.uiHealthBar.Show();
            
            var health = playerBodyV.GetComponentInParent<CTP_PlayerHealth>();
            if(health != null)
            {
                this.uiHealthBar.SetHealth(health.CurrentHP, health.MaxHP);
            }
        }

        private void Event_Client_OnHealthChanged(Dictionary<string, object> message)
        {
            ulong clientId = (ulong)message["clientId"];
            float newHP = (float)message["newHP"];
            float maxHP = (float)message["maxHP"];

            if (!this.hasTarget || clientId != this.targetClientId)
            {
                return;
            }
            this.uiHealthBar.SetHealth(newHP, maxHP);
        }

        private void Event_Client_OnPlayerCameraEnabled(Dictionary<string, object> message)
        {
            PlayerCamera playerCamera = (PlayerCamera)message["playerCamera"];
            if (!this.hasTarget || playerCamera.OwnerClientId != this.targetClientId)
            {
                return;
            }
            this.uiHealthBar.Show();
        }

        private void Event_Client_OnPlayerCameraDisabled(Dictionary<string, object> message)
        {
            PlayerCamera playerCamera = (PlayerCamera)message["playerCamera"];
            if (!this.hasTarget || playerCamera.OwnerClientId != this.targetClientId)
            {
                return;
            }
            this.uiHealthBar.Hide();
        }
    }
}
