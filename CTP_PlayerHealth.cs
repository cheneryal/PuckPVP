using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Collections;

namespace CTP
{
    public class CTP_PlayerHealth : MonoBehaviour
    {
        public float MaxHP = 100f;
        public float CurrentHP;
        public bool IsDead = false;
        
        // CHANGED: Reduced respawn time from 30f to 10f
        public float RespawnTime = 10f;

        private Player player;
        private GameObject canvasRoot;
        private Image fillImage;

        private bool isUnderwater = false;
        private bool defaultsCaptured = false;
        private bool originalFogEnabled;
        private FogMode originalFogMode;
        private float originalFogDensity;
        private Color originalFogColor;

        private static bool isNetworkHandlerRegistered = false;
        private const string SYNC_MSG_NAME = "CTP_HealthSync";

        void Awake()
        {
            player = GetComponent<Player>();
            CurrentHP = MaxHP;
        }

        void Start()
        {
            if (!isNetworkHandlerRegistered && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(SYNC_MSG_NAME, OnHealthSyncReceived);
                isNetworkHandlerRegistered = true;
                Debug.Log("[CTP] Health Sync Message Handler Registered.");
            }

            if (player.IsOwner)
            {
                CaptureDefaultVisuals();
            }
        }

        void Update()
        {
            if (IsDead)
            {
                if (canvasRoot != null) Destroy(canvasRoot);
                return;
            }

            if (player != null && player.PlayerBody != null)
            {
                CheckWaterPhysics();

                if (player.IsOwner) CheckWaterVisuals();

                if (canvasRoot == null) CreateHealthBar(player.PlayerBody.transform);
                UpdateHealthBar();
            }
            else
            {
                if (canvasRoot != null) Destroy(canvasRoot);
            }
        }

        private void CheckWaterPhysics()
        {
            if (player.PlayerBody.transform.position.y < -1.5f)
            {
                var rb = player.PlayerBody.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 v = rb.linearVelocity;
                    v.x = Mathf.Lerp(v.x, 0f, Time.deltaTime * 5f);
                    v.z = Mathf.Lerp(v.z, 0f, Time.deltaTime * 5f);
                    if (v.y < -1.5f) v.y = Mathf.Lerp(v.y, -1.5f, Time.deltaTime * 15f);
                    rb.linearVelocity = v;
                }

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    TakeDamage(40f * Time.deltaTime, true);
                }
            }
        }

        public void TakeDamage(float amount, bool isDrowning = false)
        {
            if (IsDead) return;
            // --- TANK LOGIC START ---
            // If the player is a Goalie and not drowning, reduce damage significantly.
            if (!isDrowning && player.Role.Value == PlayerRole.Goalie)
            {
                amount *= 0.25f; // Goalies take only 25% of incoming damage (75% reduction)
            }
            CurrentHP -= amount;

            // Sync to clients
            SyncHealthToClients(CurrentHP);

            if (CurrentHP <= 0)
            {
                Die(isDrowning);
            }
        }

        public void ResetHealth()
        {
            CurrentHP = MaxHP;
            IsDead = false;
            
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                SyncHealthToClients(MaxHP);
            }
        }

        private void SyncHealthToClients(float newHP)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(player.OwnerClientId);
            writer.WriteValueSafe(newHP);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(SYNC_MSG_NAME, writer);
        }

        private static void OnHealthSyncReceived(ulong senderId, FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out ulong targetClientId);
                reader.ReadValueSafe(out float receivedHP);

                if (NetworkBehaviourSingleton<PlayerManager>.Instance == null) return;
                var targetPlayer = NetworkBehaviourSingleton<PlayerManager>.Instance.GetPlayerByClientId(targetClientId);
                
                if (targetPlayer != null)
                {
                    var hpComp = targetPlayer.GetComponent<CTP_PlayerHealth>();
                    if (hpComp != null)
                    {
                        hpComp.SetHPClientSide(receivedHP);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CTP] Error parsing health sync: {ex}");
            }
        }

        public void SetHPClientSide(float hp)
        {
            CurrentHP = hp;
            
            var message = new Dictionary<string, object>
            {
                { "clientId", player.OwnerClientId },
                { "newHP", CurrentHP },
                { "maxHP", MaxHP }
            };
            MonoBehaviourSingleton<EventManager>.Instance.TriggerEvent("Event_Client_OnHealthChanged", message);

            if (CurrentHP <= 0 && !IsDead)
            {
                IsDead = true; 
                if (canvasRoot != null) Destroy(canvasRoot);
            }
            else if (CurrentHP > 0 && IsDead)
            {
                IsDead = false;
            }
        }

        private void Die(bool isDrowning)
        {
            if (IsDead) return;
            IsDead = true;
            CurrentHP = 0;

            if (canvasRoot != null) Destroy(canvasRoot);
            if (player.IsOwner) SetUnderwaterVisuals(false);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                string reason = isDrowning ? "drowned" : "was eliminated";
                string msg = $"<color=red><b>{player.Username.Value} {reason}!</b> (Respawn in {RespawnTime}s)</color>";
                
                var uiChat = UnityEngine.Object.FindFirstObjectByType<UIChat>();
                if (uiChat != null)
                {
                    uiChat.Server_SendSystemChatMessage(msg);
                }

                if (CTP_ScoringManager.Instance != null)
                {
                    CTP_ScoringManager.Instance.RegisterKill(player.Team.Value);
                }

                if (isDrowning)
                {
                    player.Server_DespawnCharacter();
                    CTPEnforcer.Instance.DrowningRespawn(player, RespawnTime);
                }
                else
                {
                    StartCoroutine(RespawnRoutine());
                }
            }
            else if (player.IsOwner && isDrowning)
            {
                player.Client_SetPlayerStateRpc(PlayerState.Spectate, 0f);
            }
        }

        private IEnumerator RespawnRoutine()
        {
            CTP_KnockdownManager.KnockdownPlayer(player, FallReason.Eliminated);
            
            yield return new WaitForSeconds(RespawnTime);

            ResetHealth();
            CTP_KnockdownManager.RevivePlayer(player.OwnerClientId);

            Vector3 spawnPos = Vector3.zero;
            if (player.Team.Value == PlayerTeam.Blue)
                spawnPos = new Vector3(-36.4674f, 0.5f, 36.6389f) + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
            else
                spawnPos = new Vector3(38.3345f, 0.5f, -35.8137f) + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));

            if(player.PlayerBody != null)
            {
                player.PlayerBody.Server_Teleport(spawnPos, Quaternion.identity);
            }

            var uiChat = UnityEngine.Object.FindFirstObjectByType<UIChat>();
            if (uiChat != null)
            {
                uiChat.Server_SendSystemChatMessage($"<color=yellow>{player.Username.Value} has respawned.</color>");
            }
        }

        private void CheckWaterVisuals()
        {
            Transform cam = GetCamera();
            if (cam == null) return;
            bool camBelowWater = cam.position.y < -1.6f;
            if (camBelowWater && !isUnderwater) SetUnderwaterVisuals(true);
            else if (!camBelowWater && isUnderwater) SetUnderwaterVisuals(false);
        }

        private void SetUnderwaterVisuals(bool active)
        {
            if (!defaultsCaptured) CaptureDefaultVisuals();
            isUnderwater = active;
            if (active) {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = 0.15f; 
                RenderSettings.fogColor = new Color(0.0f, 0.1f, 0.25f, 1.0f); 
            } else {
                RenderSettings.fog = originalFogEnabled;
                RenderSettings.fogMode = originalFogMode;
                RenderSettings.fogDensity = originalFogDensity;
                RenderSettings.fogColor = originalFogColor;
            }
        }

        private void CaptureDefaultVisuals()
        {
            originalFogEnabled = RenderSettings.fog;
            originalFogMode = RenderSettings.fogMode;
            originalFogDensity = RenderSettings.fogDensity;
            originalFogColor = RenderSettings.fogColor;
            defaultsCaptured = true;
        }

        private Transform GetCamera()
        {
            if (Camera.main != null) return Camera.main.transform;
            if (Camera.current != null) return Camera.current.transform;
            return null;
        }

        private void CreateHealthBar(Transform parent)
        {
            canvasRoot = new GameObject("HealthBar_Canvas");
            canvasRoot.transform.SetParent(parent, false);
            canvasRoot.transform.localPosition = new Vector3(0, 2.3f, 0); 
            canvasRoot.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f); 
            Canvas c = canvasRoot.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = 999; 
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasRoot.transform, false);
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.sprite = CTP_AssetLoader.TextureToSprite(CTP_AssetLoader.LoadTexture("hp_bg.png", Color.gray));
            bgImg.rectTransform.sizeDelta = new Vector2(200, 35);
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(canvasRoot.transform, false);
            fillImage = fillObj.AddComponent<Image>();
            fillImage.sprite = CTP_AssetLoader.TextureToSprite(CTP_AssetLoader.LoadTexture("hp_fill.png", Color.green));
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.rectTransform.sizeDelta = new Vector2(200, 35);
            fillImage.color = Color.white; 
        }

        private void UpdateHealthBar()
        {
             if (canvasRoot != null && fillImage != null)
            {
                Transform cam = GetCamera();
                if (cam != null)
                    canvasRoot.transform.LookAt(canvasRoot.transform.position + cam.rotation * Vector3.forward, cam.rotation * Vector3.up);

                float rawPercent = Mathf.Clamp01(CurrentHP / MaxHP);
                float steps = 7.0f;
                float discreteFill = Mathf.Ceil(rawPercent * steps) / steps;
                fillImage.fillAmount = discreteFill;
            }
        }

        private void OnDestroy()
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            if (player != null && player.IsOwner) SetUnderwaterVisuals(false);
        }
    }
}