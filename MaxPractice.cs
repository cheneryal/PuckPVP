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
using UnityEngine.UIElements;

namespace CTP
{
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

        public void Awake()
        {
            harmony = new Harmony("com.CTP.plugin");
            harmony.PatchAll();
        }

        public void OnUnload()
        {
            harmony.UnpatchSelf();
        }
    }

    public static class HP_Mechanic_Patches
    {
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

        [HarmonyPatch(typeof(PlayerBodyV2), "Server_OnDeferredCollision")]
        public static class DeferredCollisionPatch
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerBodyV2 __instance, GameObject gameObject, float force)
            {
                if (!NetworkManager.Singleton.IsServer) return;
                if (gameObject == null) return;

                float damageMultiplier = 0f;
                float damageCap = 100f;

                Puck puck = gameObject.GetComponentInParent<Puck>();
                Stick stick = gameObject.GetComponentInParent<Stick>();
                PlayerBodyV2 otherPlayer = gameObject.GetComponentInParent<PlayerBodyV2>();

                if (puck != null)
                {
                    damageMultiplier = 3.0f; 
                    damageCap = 34f; 
                }
                else if (stick != null)
                {
                    damageMultiplier = 5.0f; 
                    damageCap = 25f; 
                }
                else if (otherPlayer != null)
                {
                    var rb_instance = __instance.GetComponent<Rigidbody>();
                    var rb_other = otherPlayer.GetComponent<Rigidbody>();

                    if (rb_instance != null && rb_other != null)
                    {
                        if (rb_instance.linearVelocity.magnitude >= rb_other.linearVelocity.magnitude)
                        {
                            return;
                        }
                    }
                    
                    damageMultiplier = 4.0f; 
                    damageCap = 40f; 
                }
                else
                {
                    return; 
                }
                
                if (force > 0.1f) 
                {
                    float finalDamage = force * damageMultiplier;
                    finalDamage = Mathf.Min(finalDamage, damageCap);

                    if (__instance.Player != null)
                    {
                        var hp = __instance.Player.GetComponent<CTP_PlayerHealth>();
                        if (hp != null)
                        {
                            hp.TakeDamage(finalDamage);
                        }
                    }
                }
            }
        }

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
                        // hp.TakeDamage(9999f); 
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PlayerBodyV2), "OnStandUp")]
        public class PatchPreventStandUp
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerBodyV2 __instance)
            {
                try
                {
                    if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer) return true;
                    if (__instance.Player == null) return true;

                    ulong clientId = __instance.Player.OwnerClientId;

                    if (CTP_KnockdownManager.SuppressedStandUp.Contains(clientId))
                    {
                        return false; 
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CTP] Error in OnStandUp patch: {ex.Message}");
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerBodyV2), "FixedUpdate")]
        public class PatchEnforceKnockdown
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerBodyV2 __instance)
            {
                try
                {
                    if (!NetworkManager.Singleton.IsServer) return;
                    if (__instance.Player == null) return;
                    
                    ulong clientId = __instance.Player.OwnerClientId;

                    if (CTP_KnockdownManager.SuppressedStandUp.Contains(clientId))
                    {
                        if (!__instance.HasFallen)
                        {
                            __instance.HasFallen = true;
                        }
                        if (__instance.HasSlipped)
                        {
                            __instance.HasSlipped = false;
                        }
                        
                        if (__instance.KeepUpright.Balance > 0.1f)
                        {
                            __instance.KeepUpright.Balance = 0f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CTP] Error in EnforceKnockdown patch: {ex.Message}");
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_UIHUDController_Start
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("UIHUDController");
                if (type == null) return null;
                return AccessTools.Method(type, "Start");
            }

            [HarmonyPostfix]
            static void Postfix()
            {
                MonoBehaviourSingleton<EventManager>.Instance.AddEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(UIHUDController_HealthPatch.Event_OnPlayerHPChanged));
            }
        }
        
        [HarmonyPatch]
        public static class Patch_UIHUDController_OnDestroy
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("UIHUDController");
                if (type == null) return null;
                return AccessTools.Method(type, "OnDestroy");
            }
            
            [HarmonyPostfix]
            static void Postfix()
            {
                MonoBehaviourSingleton<EventManager>.Instance.RemoveEventListener("Event_Client_OnHealthChanged", new Action<Dictionary<string, object>>(UIHUDController_HealthPatch.Event_OnPlayerHPChanged));
            }
        }
        
        public static class UIHUDController_HealthPatch
        {
            public static void Event_OnPlayerHPChanged(Dictionary<string, object> message)
            {
                try
                {
                    if (message.ContainsKey("clientId"))
                    {
                        ulong id = (ulong)message["clientId"];
                        if (NetworkManager.Singleton.LocalClientId == id)
                        {
                            var newHP = (float)message["newHP"];
                            // Debug.Log($"[CTP] HUD Update Event Received for local player. HP: {newHP}");
                            UIGameState_Helper.UpdateHp(newHP);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CTP] Error in Event_OnPlayerHPChanged: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(Puck), "FixedUpdate")]
        public static class PuckHazardPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Puck __instance)
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    if (__instance.transform.position.y < -5.0f)
                    {
                        var rb = __instance.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            float treeExclusionMinX = -7.0f;
                            float treeExclusionMaxX = 5.0f;
                            float treeExclusionMinZ = -4.0f;
                            float treeExclusionMaxZ = 8.0f;

                            Vector3 spawnPosition;
                            do
                            {
                                spawnPosition = new Vector3(UnityEngine.Random.Range(-15f, 15f), 0.0038f, UnityEngine.Random.Range(-15f, 15f));
                            } 
                            while (spawnPosition.x >= treeExclusionMinX && spawnPosition.x <= treeExclusionMaxX &&
                                   spawnPosition.z >= treeExclusionMinZ && spawnPosition.z <= treeExclusionMaxZ);

                            rb.position = spawnPosition;
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerController))]
    public static class PlayerController_Patches
    {
        [HarmonyPatch("Event_Client_OnPlayerRequestPositionSelect")]
        [HarmonyPrefix]
        public static bool Prefix_RequestPositionSelect(PlayerController __instance)
        {
            try
            {
                var player = __instance.GetComponent<Player>();
                if (player != null && player.IsOwner)
                {
                    var hp = player.GetComponent<CTP_PlayerHealth>();
                    if (hp != null && hp.IsDead) return false; 

                    if (GameManager.Instance != null && GameManager.Instance.GameState.Value.Phase == GamePhase.Playing)
                    {
                        return false;
                    }
                }
            }
            catch(Exception ex) { Debug.LogError($"[CTP] Error: {ex}"); }
            return true;
        }

        [HarmonyPatch("Event_Client_OnPauseMenuClickSwitchTeam")]
        [HarmonyPrefix]
        public static bool Prefix_SwitchTeam(PlayerController __instance)
        {
            try
            {
                var player = __instance.GetComponent<Player>();
                if (player != null && player.IsOwner)
                {
                    var hp = player.GetComponent<CTP_PlayerHealth>();
                    if (hp != null && hp.IsDead) return false; 

                    if (GameManager.Instance != null && GameManager.Instance.GameState.Value.Phase == GamePhase.Playing)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[CTP] Error: {ex}"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameManager), "OnNetworkSpawn")]
    public static class GameManagerDurationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameManager __instance)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                if (__instance.PhaseDurationMap.ContainsKey(GamePhase.Playing))
                {
                    __instance.PhaseDurationMap[GamePhase.Playing] = 900; // 15 minutes
                    Debug.Log("[CTP] Overriding Playing Phase duration to 15 minutes (900s).");
                }
            }
        }
    }

    [HarmonyPatch(typeof(SynchronizedObjectManager))]
    public static class NetworkBoundsPatches
    {
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
                    field.SetValue(__instance, 0.006f); 
                }
                else
                {
                    field.SetValue(__instance, 0.03f); 
                }
            }
        }

        [HarmonyPatch("OnClientTick")]
        [HarmonyPrefix]
        static bool OnClientTickPrefix(SynchronizedObject __instance, Vector3 position, Quaternion rotation, float serverDeltaTime)
        {
            if (serverDeltaTime <= 0f) return false;

            Vector3 rawLinearVel = (position - __instance.transform.position) / serverDeltaTime;
            Vector3 rawAngularVel = (rotation * Quaternion.Inverse(__instance.transform.rotation)).eulerAngles / serverDeltaTime;

            if (rawAngularVel.x > 180) rawAngularVel.x -= 360; else if (rawAngularVel.x < -180) rawAngularVel.x += 360;
            if (rawAngularVel.y > 180) rawAngularVel.y -= 360; else if (rawAngularVel.y < -180) rawAngularVel.y += 360;
            if (rawAngularVel.z > 180) rawAngularVel.z -= 360; else if (rawAngularVel.z < -180) rawAngularVel.z += 360;

            float speed = rawLinearVel.magnitude;
            float smoothFactor = Mathf.Lerp(0.1f, 0.8f, Mathf.Clamp01(speed / 4.0f));

            __instance.PredictedLinearVelocity = Vector3.Lerp(__instance.PredictedLinearVelocity, rawLinearVel, smoothFactor);
            __instance.PredictedAngularVelocity = Vector3.Lerp(__instance.PredictedAngularVelocity, rawAngularVel, smoothFactor);

            var type = typeof(SynchronizedObject);
            type.GetField("lastReceivedPosition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, position);
            type.GetField("lastReceivedRotation", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, rotation);

            float distError = Vector3.Distance(__instance.transform.position, position);
            
            if (distError > 0.1f) 
            {
                __instance.transform.position = position;
                __instance.transform.rotation = rotation;
            }
            else
            {
                __instance.transform.position = Vector3.Lerp(__instance.transform.position, position, 0.5f);
                __instance.transform.rotation = Quaternion.Slerp(__instance.transform.rotation, rotation, 0.5f);
            }

            return false;
        }
    }

    public class CTPEnforcer : MonoBehaviour
    {
        public static CTPEnforcer Instance { get; private set; }
        CTPConfig cfg;
        static AssetBundle loadedBundle;
        GameObject spawnedMapInstance;
        PhysicsMaterial customBoardMaterial;
        PhysicsMaterial customIceMaterial;
        private static bool uiHealthBarInitialized = false;

        const int LAYER_ICE = 13;    
        const int LAYER_BOARDS = 12; 
        
        const float MAP_SCALE = 1.0f; 

        void Awake()
        {
            Instance = this;
            cfg = new CTPConfig();
            UScene.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            Instance = null;
            UScene.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!cfg.ModEnabled) return;

            if (!uiHealthBarInitialized)
            {
                GameObject uiHealthBarGo = new GameObject("UIHealthBarController");
                uiHealthBarGo.AddComponent<UIHealthBarController>();
                DontDestroyOnLoad(uiHealthBarGo);

                // CTP_HealthSyncer removed, logic moved to CTP_PlayerHealth via CustomMessaging

                GameObject scoringMgr = new GameObject("CTP_ScoringManager");
                scoringMgr.AddComponent<CTP_ScoringManager>();
                DontDestroyOnLoad(scoringMgr);

                uiHealthBarInitialized = true;
            }

            StartCoroutine(ProcessMapRoutine());
        }

        IEnumerator ProcessMapRoutine()
        {
            if (spawnedMapInstance != null) yield break;

            yield return new WaitForSeconds(1.0f);

            if (spawnedMapInstance == null) yield return StartCoroutine(LoadAndInstantiateMap());

            if (spawnedMapInstance != null)
            {
                Log($"Configuring Map: {spawnedMapInstance.name}...");
                try 
                {
                    CreatePhysicsMaterials();
                    HandleSceneCleanup();
                    ConfigureCustomMapPhysics(spawnedMapInstance);
                    HandleGoals();
                    HandleCapturePads(); 
                    OverrideVanillaBounds();
                    HandleAudioEnvironment();
                    HandleSkybox();
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
            var barrier = GameObject.Find("Barrier - Left") ?? GameObject.Find("Barrier - Right") ?? GameObject.Find("Barrier");
            if (barrier != null)
            {
                var vanillaMat = barrier.GetComponent<Collider>()?.sharedMaterial;
                if (vanillaMat != null)
                {
                    customBoardMaterial = new PhysicsMaterial("CTP_Barrier_From_Vanilla");
                    customBoardMaterial.bounciness = 0.2f; 
                    customBoardMaterial.dynamicFriction = vanillaMat.dynamicFriction;
                    customBoardMaterial.staticFriction = vanillaMat.staticFriction;
                    customBoardMaterial.frictionCombine = vanillaMat.frictionCombine;
                    customBoardMaterial.bounceCombine = vanillaMat.bounceCombine;
                    Log("Successfully duplicated vanilla board physics material and increased bounciness.");
                }
            }

            if (customBoardMaterial == null)
            {
                Log("Could not find vanilla board material, creating one with estimated values.");
                customBoardMaterial = new PhysicsMaterial("CTP_Barrier");
                customBoardMaterial.bounciness = 0.2f;
                customBoardMaterial.dynamicFriction = 0.0f;
                customBoardMaterial.staticFriction = 0.0f;
                customBoardMaterial.bounceCombine = PhysicsMaterialCombine.Average;
                customBoardMaterial.frictionCombine = PhysicsMaterialCombine.Average;
            }
            
            customIceMaterial = new PhysicsMaterial("CTP_Ice");
            customIceMaterial.dynamicFriction = 0f;
            customIceMaterial.staticFriction = 0f;
        }

        void HandleSkybox()
        {
            if (loadedBundle != null)
            {
                Material skyboxMaterial = loadedBundle.LoadAsset<Material>("FS002_Night");
                if (skyboxMaterial != null)
                {
                    RenderSettings.skybox = skyboxMaterial;
                    Log("Successfully set skybox to FS002_Night.");
                }
                else
                {
                    Log("Could not find skybox material 'FS002_Night' in the asset bundle.");
                }
            }
        }

        void HandleAudioEnvironment()
        {
            var reverbZone = Resources.FindObjectsOfTypeAll<AudioReverbZone>().FirstOrDefault(o => o.gameObject.scene.IsValid());
            if (reverbZone != null)
            {
                reverbZone.gameObject.SetActive(true);
                reverbZone.transform.position = Vector3.zero;
                reverbZone.maxDistance = 500f;
                Log($"Adjusted and enlarged reverb zone '{reverbZone.name}' to {reverbZone.maxDistance}m.");
            }
            else
            {
                Log("Could not find vanilla reverb zone. Stick audio might sound flat.");
            }
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

        void HandleCapturePads()
        {
            GameObject bluePad = FindObjectIncludingInactive("BlueCapturePad") ?? FindObjectIncludingInactive("BlueZone");
            GameObject redPad = FindObjectIncludingInactive("RedCapturePad") ?? FindObjectIncludingInactive("RedZone");

            SetupCapturePad(bluePad, PlayerTeam.Blue);
            SetupCapturePad(redPad, PlayerTeam.Red);
        }

        void SetupCapturePad(GameObject pad, PlayerTeam team)
        {
            if (pad != null)
            {
                pad.gameObject.layer = LAYER_ICE; 

                var allColliders = pad.GetComponentsInChildren<Collider>(true);
                foreach (var c in allColliders)
                {
                    c.sharedMaterial = customIceMaterial; // Frictionless
                    c.gameObject.layer = LAYER_ICE;       // Ice layer
                    c.isTrigger = false;                  // No triggers
                    
                    if (c is MeshCollider mc)
                    {
                        mc.convex = false; // Pure mesh floor
                    }
                }

                Log($"Setup capture pad for {team} on object {pad.name} (Physics/Visual Only)");
            }
            else
            {
                Log($"WARNING: Could not find capture pad for {team}");
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

        public void StartDelayedFreeze(PlayerBodyV2 playerBody, float delay)
        {
            StartCoroutine(DelayedFreeze(playerBody, delay));
        }

        private IEnumerator DelayedFreeze(PlayerBodyV2 playerBody, float delay)
        {
            yield return new WaitForSeconds(delay);
            try
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && playerBody != null && playerBody.Player != null)
                {
                    playerBody.Server_Freeze();
                    Debug.Log($"[CTP] Player {playerBody.Player.Username.Value} frozen after knockdown.");
                }
            }
            catch (Exception ex)
            {
                 Debug.LogError($"[CTP] Error in DelayedFreeze: {ex.Message}");
            }
        }

        public void DrowningRespawn(Player player, float delay)
        {
            StartCoroutine(DrowningRespawnCoroutine(player, delay));
        }

        private IEnumerator DrowningRespawnCoroutine(Player player, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (player == null || !player.NetworkObject.IsSpawned)
            {
                Debug.Log($"[CTP] Player for drowning respawn is no longer valid.");
                yield break;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                var hp = player.GetComponent<CTP_PlayerHealth>();
                if (hp != null)
                {
                    hp.ResetHealth();
                }

                Vector3 spawnPos = Vector3.zero;
                if (player.Team.Value == PlayerTeam.Blue)
                {
                    spawnPos = new Vector3(-36.4674f, 0.5f, 36.6389f) + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));
                }
                else if (player.Team.Value == PlayerTeam.Red)
                {
                    spawnPos = new Vector3(38.3345f, 0.5f, -35.8137f) + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));
                }
                
                player.Server_RespawnCharacter(spawnPos, Quaternion.identity, player.Role.Value);
                player.Client_SetPlayerStateRpc(PlayerState.Play, 0.1f);

                // Send Respawn Message
                var uiChat = UnityEngine.Object.FindFirstObjectByType<UIChat>();
                if (uiChat != null)
                {
                    uiChat.Server_SendSystemChatMessage($"<color=yellow>{player.Username.Value} has respawned.</color>");
                }
            }
        }
    }

    public static class UIGameState_Helper
    {
        public static UnityEngine.UIElements.Label hpLabel;
        
        public static void UpdateHp(float currentHP)
        {
            // Dynamic re-fetch if reference is lost
            if (hpLabel == null)
            {
                var uiHud = UnityEngine.Object.FindFirstObjectByType<UIHUD>();
                if (uiHud != null)
                {
                    var doc = uiHud.GetComponent<UnityEngine.UIElements.UIDocument>();
                    if (doc != null && doc.rootVisualElement != null)
                    {
                        hpLabel = doc.rootVisualElement.Q<UnityEngine.UIElements.Label>("PlayerHPLabel");
                    }
                }
            }

            if (hpLabel != null)
            {
                hpLabel.text = $"HP: {Mathf.RoundToInt(currentHP)}";
            }
        }
    }

    [HarmonyPatch]
    public static class UIHUD_Patch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("UIHUD");
            if (type == null)
            {
                Debug.LogError("[CTP] UIHUD type not found for patching.");
                return null;
            }
            var method = AccessTools.Method(type, "Initialize");
            if (method == null)
            {
                Debug.LogError("[CTP] UIHUD.Initialize method not found for patching.");
            }
            return method;
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance, VisualElement rootVisualElement)
        {
            Debug.Log("[CTP] UIHUD_Patch.Postfix triggered.");
            try
            {
                var container = rootVisualElement.Q<VisualElement>("PlayerContainer");
                if (container == null)
                {
                    Debug.LogError("[CTP] PlayerContainer not found!");
                    return;
                }
                Debug.Log("[CTP] PlayerContainer found.");

                var existingLabel = container.Q<UnityEngine.UIElements.Label>("PlayerHPLabel");
                if (existingLabel != null)
                {
                     UIGameState_Helper.hpLabel = existingLabel;
                     Debug.Log("[CTP] HP Label already exists.");
                     return;
                }

                var hpLabel = new UnityEngine.UIElements.Label
                {
                    name = "PlayerHPLabel",
                    text = "HP: 100"
                };

                var speedLabel = container.Q<UnityEngine.UIElements.Label>("SpeedLabel");
                if (speedLabel != null)
                {
                    Debug.Log("[CTP] SpeedLabel found, copying styles.");
                    hpLabel.style.color = speedLabel.style.color;
                    hpLabel.style.fontSize = speedLabel.style.fontSize;
                    hpLabel.style.unityFontStyleAndWeight = speedLabel.style.unityFontStyleAndWeight;
                    hpLabel.style.unityTextAlign = speedLabel.style.unityTextAlign;
                    hpLabel.style.marginLeft = speedLabel.style.marginLeft;
                    hpLabel.style.marginRight = speedLabel.style.marginRight;
                    hpLabel.style.marginTop = speedLabel.style.marginTop;
                    hpLabel.style.marginBottom = speedLabel.style.marginBottom;
                }
                else
                {
                    Debug.LogWarning("[CTP] SpeedLabel not found. Applying default styles to HP Label.");
                     hpLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
                     hpLabel.style.fontSize = 22;
                     hpLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                }

                container.Add(hpLabel);
                UIGameState_Helper.hpLabel = hpLabel;
                Debug.Log("[CTP] HP Label added to PlayerContainer.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CTP] Error in UIHUD_Patch: {ex}");
            }
        }
    }
    
    [HarmonyPatch]
    public static class PlayerSpawningPatches
    {
        private static readonly Vector3 BlueSpawnPos = new Vector3(-36.4674f, 0.5f, 36.6389f);
        private static readonly Vector3 RedSpawnPos = new Vector3(38.3345f, 0.5f, -35.8137f);

        [HarmonyPatch(typeof(Player), "Server_SpawnCharacter")]
        [HarmonyPrefix]
        public static void OverrideSpawnPosition(Player __instance, ref Vector3 position)
        {
            if (__instance.Team.Value == PlayerTeam.Blue)
            {
                position = BlueSpawnPos + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));
            }
            else if (__instance.Team.Value == PlayerTeam.Red)
            {
                position = RedSpawnPos + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0, UnityEngine.Random.Range(-1.5f, 1.5f));
            }
        }
    }
}