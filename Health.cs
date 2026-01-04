using System.Collections;
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

    void Awake()
    {
        player = GetComponent<Player>();
        CurrentHP = MaxHP;
    }

    void Update()
    {
        // Auto-Reset if body spawns (Revive detection)
        if (IsDead && player.PlayerBody != null)
        {
            IsDead = false;
            CurrentHP = MaxHP;
            if (NetworkManager.Singleton.IsServer) player.Server_DespawnSpectatorCamera();
        }

        // UI Management
        if (IsDead)
        {
            if (canvasRoot != null) Destroy(canvasRoot);
            return;
        }

        if (player.PlayerBody != null)
        {
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

    private Transform GetCamera()
    {
        if (Camera.main != null) return Camera.main.transform;
        if (Camera.current != null) return Camera.current.transform;
        return null;
    }

    // Called by Server Logic
    public void TakeDamage(float amount)
    {
        // Server Authority Logic
        CurrentHP -= amount;

        // SYNC: Send hidden chat message to all clients
        if (NetworkManager.Singleton.IsServer)
        {
            // Format: $$HP|ClientID|NewHP
            string msg = $"$$HP|{player.OwnerClientId}|{CurrentHP}";
            var uiChat = NetworkBehaviourSingleton<UIChat>.Instance;
            if (uiChat != null)
            {
                uiChat.Server_SendSystemChatMessage(msg);
            }
        }

        if (CurrentHP <= 0)
        {
            Die();
        }
    }

    // Called by Client Chat Interceptor
    public void SetHPClientSide(float hp)
    {
        CurrentHP = hp;
        if (CurrentHP <= 0 && !IsDead)
        {
            // Local visual death
            IsDead = true;
            if (canvasRoot != null) Destroy(canvasRoot);
        }
    }

    private void Die()
    {
        IsDead = true;
        CurrentHP = 0;
        
        if (canvasRoot != null) Destroy(canvasRoot);

        if (NetworkManager.Singleton.IsServer)
        {
            PerformServerDeath();
        }
        else if (player.IsOwner)
        {
            // Signal Server
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