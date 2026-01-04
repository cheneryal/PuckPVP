using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit; 
using HarmonyLib; 
using UnityEngine;
using UnityEngine.SceneManagement;
using UScene = UnityEngine.SceneManagement.SceneManager;
using Unity.Netcode;
using System.Globalization;

[Serializable]
public class CTPConfig
{
    public bool ModEnabled = true;
    public string BundleFileName = "ctp";            
    public string PrefabName = "CaptureThePuck";     
    public string CustomIceName = "Ice"; 
    public bool MoveGoalsToSpawns = true; 
    public string Team0SpawnName = "GoalSpawn_0"; 
    public string Team1SpawnName = "GoalSpawn_1"; 
    public bool LogDebug = true;
}

public class CaptureThePuck_Mod : IPuckMod
{
    GameObject host;
    Harmony harmony;

    public bool OnEnable()
    {
        host = new GameObject("[CaptureThePuck_ModHost]");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<CTPEnforcer>();

        try
        {
            if (harmony == null) harmony = new Harmony("com.capturethepuck.networkfix");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("[CTP] Network bounds patched (200f). Soft Sync & Rotational Smoothing active.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CTP] CRITICAL: Failed to patch network. {ex}");
        }

        return true;
    }

    public bool OnDisable()
    {
        if (host) UnityEngine.Object.Destroy(host);
        if (harmony != null) harmony.UnpatchSelf();
        return true;
    }
}

public static class HP_Mechanic_Patches
{
    // 1. Attach Health Manager to the PLAYER
    [HarmonyPatch(typeof(Player), "OnNetworkSpawn")]
    public static class PlayerSpawnPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            if (__instance.GetComponent<CTP_PlayerHealth>() == null)
            {
                __instance.gameObject.AddComponent<CTP_PlayerHealth>();
            }
        }
    }

    // 2. Detect Impact via Deferred Collision (Matches Grunt Sound Logic)
    // This replaces the old OnCollisionEnter patch. 
    // It hooks into the exact same method that triggers the audio.
    [HarmonyPatch(typeof(PlayerBodyV2), "Server_OnDeferredCollision")]
    public static class DeferredCollisionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerBodyV2 __instance, GameObject gameObject, float force)
        {
            // Ensure we are on Server (Damage Authority)
            if (!NetworkManager.Singleton.IsServer) return;

            // Filter out invalid objects
            if (gameObject == null) return;

            float damageMultiplier = 0f;
            float damageCap = 100f;

            // Use GetComponentInParent to find root objects (Stick, Puck, Player)
            // The 'gameObject' passed here is usually the specific collider object
            Puck puck = gameObject.GetComponentInParent<Puck>();
            Stick stick = gameObject.GetComponentInParent<Stick>();
            PlayerBodyV2 otherPlayer = gameObject.GetComponentInParent<PlayerBodyV2>();

            // --- DAMAGE TUNING ---
            // 'force' is the calculated impact intensity used for audio volume.
            // It generally ranges from 0.0 to 10.0+ depending on impact.
            
            if (puck != null)
            {
                // Puck: Fast, sharp hits.
                damageMultiplier = 3.0f; 
                damageCap = 34f; // Max 3 hard shots to kill
            }
            else if (stick != null)
            {
                // Stick: Slashing.
                damageMultiplier = 5.0f; // High multiplier because sticks are light
                damageCap = 25f; 
            }
            else if (otherPlayer != null)
            {
                // Body Check: Heavy mass hits.
                damageMultiplier = 4.0f; 
                damageCap = 40f; 
            }
            else
            {
                // Hit wall/floor/net - Ignore for HP
                return; 
            }
            
            // Apply Damage
            // The game already filters tiny collisions before calling this method.
            if (force > 0.1f) 
            {
                float finalDamage = force * damageMultiplier;
                finalDamage = Mathf.Min(finalDamage, damageCap);

                if (__instance.Player != null)
                {
                    var hp = __instance.Player.GetComponent<CTP_PlayerHealth>();
                    if (hp != null)
                    {
                        // Debug.Log($"[CTP] Hit by {gameObject.name}. Force: {force:F1}. Dmg: {finalDamage:F1}");
                        hp.TakeDamage(finalDamage);
                    }
                }
            }
        }
    }

    // 3. Intercept Client RPC Death Signal (For Local/Owner Death)
    [HarmonyPatch(typeof(Player), "Client_SetPlayerStateRpc")]
    public static class RpcInterception
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance, PlayerState state)
        {
            if (NetworkManager.Singleton.IsServer && state == PlayerState.Spectate)
            {
                var hp = __instance.GetComponent<CTP_PlayerHealth>();
                if (hp != null && !hp.IsDead)
                {
                    hp.TakeDamage(9999f); // Trigger full death flow
                }
            }
        }
    }

    // 4. SYNC: Intercept Chat Messages to Update HP for everyone
    [HarmonyPatch(typeof(UIChat), "AddChatMessage")]
    public static class ChatSyncPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string message)
        {
            // Protocol Format: "$$HP|ClientId|CurrentHP"
            if (message.StartsWith("$$HP|"))
            {
                try
                {
                    string[] parts = message.Split('|');
                    if (parts.Length == 3)
                    {
                        ulong clientId = ulong.Parse(parts[1]);
                        float newHP = float.Parse(parts[2], CultureInfo.InvariantCulture);

                        var playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
                        if (playerManager != null)
                        {
                            var player = playerManager.GetPlayerByClientId(clientId);
                            if (player != null)
                            {
                                var hp = player.GetComponent<CTP_PlayerHealth>();
                                if (hp != null)
                                {
                                    hp.SetHPClientSide(newHP);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex) { Debug.LogError($"[CTP] Sync Error: {ex}"); }

                // Hide this message from chat
                return false; 
            }
            return true;
        }
    }
}

// --- HARMONY PATCHES ---

[HarmonyPatch(typeof(SynchronizedObjectManager))]
public static class NetworkBoundsPatches
{
    // Precision: 200f (5mm). Max Radius: ~163m. Map Size: 300m.
    // This is the highest precision possible without overflow.
    const float VANILLA_PRECISION = 655f;
    const float MODDED_PRECISION = 200f; 

    [HarmonyPatch("EncodeSynchronizedObject")]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> EncodeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == VANILLA_PRECISION)
                instruction.operand = MODDED_PRECISION;
            yield return instruction;
        }
    }

    [HarmonyPatch("DecodeSynchronizedObjectData")]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> DecodeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == VANILLA_PRECISION)
                instruction.operand = MODDED_PRECISION;
            yield return instruction;
        }
    }
}

[HarmonyPatch(typeof(SynchronizedObject))]
public static class SmoothingPatches
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    static void AwakePostfix(SynchronizedObject __instance)
    {
        var field = typeof(SynchronizedObject).GetField("positionThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            bool isStick = __instance.GetComponent<Stick>() != null;
            bool isPuck = __instance.GetComponent<Puck>() != null;

            if (isStick || isPuck)
            {
                // OPTIMIZATION FIX:
                // Grid is 5mm (1/200). Threshold MUST be > 5mm to prevent quantization noise loops.
                // Set to 6mm (0.006f). This stops "micro-jitter packets".
                field.SetValue(__instance, 0.006f); 
            }
            else
            {
                // Bodies are larger/slower, increase to 3cm to save bandwidth.
                field.SetValue(__instance, 0.03f); 
            }
        }
    }

    // THE BUTTER PATCH (Client Side Smoothing)
    // This runs on the receiving client, so it doesn't affect server bandwidth.
    [HarmonyPatch("OnClientTick")]
    [HarmonyPrefix]
    static bool OnClientTickPrefix(SynchronizedObject __instance, Vector3 position, Quaternion rotation, float serverDeltaTime)
    {
        if (serverDeltaTime <= 0f) return false;

        // 1. Calculate raw network velocity
        Vector3 rawLinearVel = (position - __instance.transform.position) / serverDeltaTime;
        Vector3 rawAngularVel = (rotation * Quaternion.Inverse(__instance.transform.rotation)).eulerAngles / serverDeltaTime;

        // 2. Fix Euler wrap-around
        if (rawAngularVel.x > 180) rawAngularVel.x -= 360; else if (rawAngularVel.x < -180) rawAngularVel.x += 360;
        if (rawAngularVel.y > 180) rawAngularVel.y -= 360; else if (rawAngularVel.y < -180) rawAngularVel.y += 360;
        if (rawAngularVel.z > 180) rawAngularVel.z -= 360; else if (rawAngularVel.z < -180) rawAngularVel.z += 360;

        // 3. Adaptive Smoothing Factors
        float speed = rawLinearVel.magnitude;
        // Aggressive smoothing at low speeds (0.1) to hide the 5mm grid steps
        // Fast reaction at high speeds (0.8) for shots
        float smoothFactor = Mathf.Lerp(0.1f, 0.8f, Mathf.Clamp01(speed / 4.0f));

        // 4. Apply Smoothing to Predicted Velocities
        __instance.PredictedLinearVelocity = Vector3.Lerp(__instance.PredictedLinearVelocity, rawLinearVel, smoothFactor);
        __instance.PredictedAngularVelocity = Vector3.Lerp(__instance.PredictedAngularVelocity, rawAngularVel, smoothFactor);

        // 5. Store last received via reflection
        var type = typeof(SynchronizedObject);
        type.GetField("lastReceivedPosition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, position);
        type.GetField("lastReceivedRotation", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, rotation);

        // 6. SOFT SYNC
        float distError = Vector3.Distance(__instance.transform.position, position);
        
        // If error > 10cm (teleport/lag spike), snap instantly
        if (distError > 0.1f) 
        {
            __instance.transform.position = position;
            __instance.transform.rotation = rotation;
        }
        else
        {
            // Smoothly blend to the new 5mm grid position
            __instance.transform.position = Vector3.Lerp(__instance.transform.position, position, 0.5f);
            __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, rotation, 0.5f);
        }

        return false;
    }
}


// -----------------------

public class CTPEnforcer : MonoBehaviour
{
    CTPConfig cfg;
    static AssetBundle loadedBundle;
    GameObject spawnedMapInstance;
    PhysicsMaterial customBoardMaterial;
    PhysicsMaterial customIceMaterial;

    const int LAYER_ICE = 13;    
    const int LAYER_BOARDS = 12; 
    
    const float MAP_SCALE = 1.0f; 

    void Awake()
    {
        cfg = new CTPConfig();
        UScene.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        UScene.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!cfg.ModEnabled) return;
        StartCoroutine(ProcessMapRoutine());
    }

    IEnumerator ProcessMapRoutine()
    {
        yield return new WaitForSeconds(1.0f);
        CreatePhysicsMaterials();

        if (spawnedMapInstance == null) yield return StartCoroutine(LoadAndInstantiateMap());

        if (spawnedMapInstance != null)
        {
            Log($"Configuring Map: {spawnedMapInstance.name}...");
            try 
            {
                HandleSceneCleanup();
                ConfigureCustomMapPhysics(spawnedMapInstance);
                HandleGoals();
                OverrideVanillaBounds();
            }
            catch (Exception ex) { Debug.LogError($"[CTP] Error: {ex.Message}"); }
        }
    }

    IEnumerator LoadAndInstantiateMap()
    {
        string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string bundlePath = Path.Combine(modDir, cfg.BundleFileName);
        if (!File.Exists(bundlePath)) bundlePath += ".bundle";

        if (loadedBundle == null)
        {
            var bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return bundleRequest;
            loadedBundle = bundleRequest.assetBundle;
        }
        
        if (loadedBundle == null) yield break;

        GameObject prefab = loadedBundle.LoadAsset<GameObject>(cfg.PrefabName);
        if (prefab == null) prefab = loadedBundle.LoadAllAssets<GameObject>().FirstOrDefault();

        spawnedMapInstance = Instantiate(prefab);
        spawnedMapInstance.name = "[CaptureThePuck_Map]";
        spawnedMapInstance.transform.localScale = Vector3.one * MAP_SCALE;
        spawnedMapInstance.transform.position = Vector3.zero;
        spawnedMapInstance.transform.rotation = Quaternion.identity;
    }

    void CreatePhysicsMaterials()
    {
        customBoardMaterial = new PhysicsMaterial("CTP_Barrier");
        customBoardMaterial.bounciness = 0.2f;
        customBoardMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        customIceMaterial = new PhysicsMaterial("CTP_Ice");
        customIceMaterial.dynamicFriction = 0f;
        customIceMaterial.staticFriction = 0f;
    }

    void ConfigureCustomMapPhysics(GameObject root)
    {
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            bool isIce = col.name.Equals(cfg.CustomIceName, StringComparison.OrdinalIgnoreCase);
            col.gameObject.layer = isIce ? LAYER_ICE : LAYER_BOARDS;
            col.sharedMaterial = isIce ? customIceMaterial : customBoardMaterial;
            col.isTrigger = false;
            col.enabled = true;
            if (!isIce && col is MeshCollider mc) mc.convex = false;
            if (!col.GetComponent<Rigidbody>()) 
            {
                var rb = col.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    void HandleSceneCleanup()
    {
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var obj in allObjects)
        {
            if (!obj.scene.IsValid()) continue;
            if (obj.transform.root == spawnedMapInstance.transform) continue;
            if (obj.name == "Ice Bottom" || obj.name == "Ice Top") obj.SetActive(false);
        }

        GameObject levelRoot = GameObject.Find("Level");
        if (levelRoot != null)
        {
            foreach (Transform child in levelRoot.transform)
            {
                if (child.root == spawnedMapInstance.transform) continue;
                if (child.name.Contains("Ice")) continue; 
                if (IsCriticalObject(child.name)) continue;
                child.gameObject.SetActive(false);
            }
        }

        DisableLooseObjects();

        var allColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (var col in allColliders)
        {
            if (col.transform.root == spawnedMapInstance.transform) continue;
            if (col.GetComponentInParent<PlayerBodyV2>() != null) continue;
            if (col.GetComponentInParent<Player>() != null) continue;
            if (col.enabled) col.enabled = false;
        }
    }

    void DisableLooseObjects()
    {
        string[] killList = { "Barrier", "Net", "Net Frame", "Goal Blue", "Goal Red", "Barrier Bottom Border", "Barrier Top Border", "Arena", "Stadium", "Roof", "Bleachers" };
        foreach(var name in killList)
        {
            var obj = GameObject.Find(name);
            if (obj != null && obj.transform.root != spawnedMapInstance.transform) obj.SetActive(false);
        }
    }

    bool IsCriticalObject(string name)
    {
        if (name.Contains("Spawn") || name.Contains("Audio") || name.Contains("Sound") || name.Contains("Manager") || name.Contains("Camera") || name.Contains("PostProcessing") || name.Contains("Reflection")) return true;
        return false;
    }

    void HandleGoals()
    {
        GameObject goalRed = FindObjectIncludingInactive("Goal Red") ?? FindObjectIncludingInactive("Level/Goal Red");
        GameObject goalBlue = FindObjectIncludingInactive("Goal Blue") ?? FindObjectIncludingInactive("Level/Goal Blue");
        GameObject spawn0 = FindRootObjectLoose(cfg.Team0SpawnName);
        GameObject spawn1 = FindRootObjectLoose(cfg.Team1SpawnName);
        ProcessGoal(goalBlue, spawn0, "Blue");
        ProcessGoal(goalRed, spawn1, "Red");
    }

    void ProcessGoal(GameObject goal, GameObject spawn, string teamName)
    {
        if (goal != null && spawn != null && cfg.MoveGoalsToSpawns)
        {
            goal.transform.position = spawn.transform.position;
            goal.transform.rotation = spawn.transform.rotation;
            goal.transform.localScale = Vector3.one * MAP_SCALE;
            goal.name = $"Goal {teamName}_MOVED"; 
            goal.SetActive(true);
        }
    }

    void OverrideVanillaBounds()
    {
        LevelManager lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
        if (lm != null)
        {
            var field = typeof(LevelManager).GetField("iceMeshRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var renderer = field.GetValue(lm) as MeshRenderer;
                if (renderer != null)
                {
                    renderer.gameObject.SetActive(true);
                    renderer.enabled = true;
                    Mesh hugeMesh = new Mesh();
                    hugeMesh.bounds = new Bounds(Vector3.zero, new Vector3(300, 50, 300));
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.mesh = hugeMesh;
                }
            }
        }
    }

    GameObject FindRootObjectLoose(string name)
    {
        if (spawnedMapInstance != null)
        {
            var found = spawnedMapInstance.transform.Find(name);
            if (found == null) found = spawnedMapInstance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    GameObject FindObjectIncludingInactive(string name)
    {
        return Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(o => o.scene.IsValid() && o.name == name);
    }

    void Log(string msg) { if (cfg.LogDebug) Debug.Log($"[CTP] {msg}"); }
}