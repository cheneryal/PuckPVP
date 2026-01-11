using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class CTP_PlayerHealth : MonoBehaviour
{
    public float MaxHP = 100f;
    public float CurrentHP;
    public bool IsDead = false;

    private Player player;
    private GameObject canvasRoot;
    private Image fillImage;
    
    private Vector3 lastDeathPos;
    private Quaternion lastDeathRot;
    private float lastSyncedHP;

    // --- UNDERWATER VISUALS ---
    private bool isUnderwater = false;
    private bool defaultsCaptured = false;
    private Color originalFogColor;
    private float originalFogDensity;
    private FogMode originalFogMode;
    private bool originalFogEnabled;

    void Awake()
    {
        player = GetComponent<Player>();
        CurrentHP = MaxHP;
        lastSyncedHP = MaxHP;
    }

    void Start()
    {
        // Capture initial weather settings to restore them later
        if (player.IsOwner)
        {
            CaptureDefaultVisuals();
        }
    }

    void Update()
    {
        if (IsDead && player.PlayerBody != null)
        {
            IsDead = false;
            CurrentHP = MaxHP;
            lastSyncedHP = MaxHP;
            if (NetworkManager.Singleton.IsServer) player.Server_DespawnSpectatorCamera();
            
            // Restore visuals on respawn
            if (player.IsOwner) SetUnderwaterVisuals(false);
        }

        if (IsDead)
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            return;
        }

        if (player.PlayerBody != null)
        {
            // Physics Logic (Server + Client Prediction)
            CheckWaterPhysics();

            // Visual Logic (Local Owner Only)
            if (player.IsOwner)
            {
                CheckWaterVisuals();
            }

            // UI Logic
            if (canvasRoot == null) CreateHealthBar(player.PlayerBody.transform);
            
            if (canvasRoot != null)
            {
                Transform cam = GetCamera();
                if (cam != null)
                    canvasRoot.transform.LookAt(canvasRoot.transform.position + cam.rotation * Vector3.forward, cam.rotation * Vector3.up);

                if (fillImage != null)
                {
                    float rawPercent = Mathf.Clamp01(CurrentHP / MaxHP);
                    float steps = 7.0f;
                    float discreteFill = Mathf.Ceil(rawPercent * steps) / steps;
                    fillImage.fillAmount = discreteFill;
                }
            }
        }
        else
        {
            if (canvasRoot != null) Destroy(canvasRoot);
        }
    }

    private void CheckWaterPhysics()
    {
        // Water level check (approx -1.5f)
        if (player.PlayerBody.transform.position.y < -1.5f)
        {
            var rb = player.PlayerBody.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;

                // 1. Horizontal Drag (Viscosity)
                v.x = Mathf.Lerp(v.x, 0f, Time.deltaTime * 5f);
                v.z = Mathf.Lerp(v.z, 0f, Time.deltaTime * 5f);

                // 2. Vertical Buoyancy/Drag
                // Clamp falling speed to -1.5m/s to simulate sinking
                if (v.y < -1.5f)
                {
                    v.y = Mathf.Lerp(v.y, -1.5f, Time.deltaTime * 15f);
                }

                rb.linearVelocity = v;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                TakeDamage(40f * Time.deltaTime);
            }
        }
    }

    private void CheckWaterVisuals()
    {
        Transform cam = GetCamera();
        if (cam == null) return;

        // Check if Camera (eyes) is underwater, not just the feet
        bool camBelowWater = cam.position.y < -1.6f;

        if (camBelowWater && !isUnderwater)
        {
            SetUnderwaterVisuals(true);
        }
        else if (!camBelowWater && isUnderwater)
        {
            SetUnderwaterVisuals(false);
        }
    }

    private void SetUnderwaterVisuals(bool active)
    {
        if (!defaultsCaptured) CaptureDefaultVisuals();

        isUnderwater = active;

        if (active)
        {
            // Set thick blue fog
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.15f; // Thick fog
            RenderSettings.fogColor = new Color(0.0f, 0.1f, 0.25f, 1.0f); // Deep Blue
        }
        else
        {
            // Restore original settings
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

    public void TakeDamage(float amount)
    {
        CurrentHP -= amount;

        if (NetworkManager.Singleton.IsServer)
        {
            if (Mathf.Abs(CurrentHP - lastSyncedHP) > 5f || CurrentHP <= 0f)
            {
                if(CTP_HealthSyncer.Instance != null)
                {
                    CTP_HealthSyncer.Instance.UpdateHealth_ClientRpc(player.OwnerClientId, CurrentHP);
                }
                lastSyncedHP = CurrentHP;
            }
        }

        if (CurrentHP <= 0)
        {
            Die();
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
    }

    private void Die()
    {
        IsDead = true;
        CurrentHP = 0;
        
        // Reset effects if I died locally
        if (player.IsOwner) SetUnderwaterVisuals(false);

        if (canvasRoot != null) Destroy(canvasRoot);

        if (NetworkManager.Singleton.IsServer)
        {
            PerformServerDeath();
        }
        else if (player.IsOwner)
        {
            player.Client_SetPlayerStateRpc(PlayerState.Spectate, 0f);
        }
    }

    public void PerformServerDeath()
    {
        if (player.PlayerBody != null)
        {
            lastDeathPos = player.PlayerBody.transform.position;
            lastDeathRot = player.PlayerBody.transform.rotation;
        }
        else
        {
            lastDeathPos = transform.position;
            lastDeathRot = Quaternion.identity;
        }

        if (player.PlayerBody != null)
        {
            player.Server_DespawnCharacter();
            player.Server_SpawnSpectatorCamera(lastDeathPos + Vector3.up * 5f, Quaternion.LookRotation(Vector3.down));
        }
        else if (!player.IsSpectatorCameraSpawned)
        {
             player.Server_SpawnSpectatorCamera(lastDeathPos + Vector3.up * 5f, Quaternion.LookRotation(Vector3.down));
        }
        
        Debug.Log($"[CTP] {player.Username.Value} Died (HP Sync).");
    }

    void CreateHealthBar(Transform target)
    {
        canvasRoot = new GameObject("HealthBar_Canvas");
        canvasRoot.transform.SetParent(target, false);
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
}