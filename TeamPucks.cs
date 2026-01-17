using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace CTP
{
    [HarmonyPatch]
    public static class TeamPucks
    {
        // Public helper so CTPEnforcer can call it after the delay
        public static void SpawnScatterPucks(PuckManager manager)
        {
            // 1. Clear the ice
            manager.Server_DespawnPucks(true);

            int layerIce = 13;
            int spawnCount = 25;
            int successfulSpawns = 0;
            int maxAttempts = 1000; 
            int attempts = 0;

            // Define how large the "No Spawn Zone" is around the center tree
            float treeExclusionRadius = 6.0f; 

            // 2. Spawn 25 scattered pucks
            while (successfulSpawns < spawnCount && attempts < maxAttempts)
            {
                attempts++;
                float randX = UnityEngine.Random.Range(-15f, 15f);
                float randZ = UnityEngine.Random.Range(-15f, 15f);
                
                // --- EXCLUSION LOGIC START ---
                
                // 1. HARD RADIUS CHECK (Center Tree)
                // If the random point is within 6 units of the center (0,0), skip it.
                // This guarantees no pucks inside the tree, regardless of raycasts.
                if (new Vector2(randX, randZ).magnitude < treeExclusionRadius) 
                {
                    continue;
                }

                // --- EXCLUSION LOGIC END ---

                // 2. RAYCAST CHECK (Mounds/Boards)
                // Used to avoid spawning inside mounds or walls that are further out
                Vector3 rayOrigin = new Vector3(randX, 10f, randZ);
                RaycastHit hit;

                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 20f))
                {
                    // Only spawn if we hit the ICE layer (13) first.
                    // If we hit Layer 12 (Obstacles), this will skip.
                    if (hit.collider.gameObject.layer == layerIce)
                    {
                        Vector3 spawnPosition = new Vector3(randX, 0.0038f, randZ);
                        manager.Server_SpawnPuck(spawnPosition, Quaternion.identity, Vector3.zero, false);
                        successfulSpawns++;
                    }
                }
            }
            
            if (attempts >= maxAttempts) Debug.LogWarning($"[CTP] Could not find safe spawn spots for all pucks.");
            
            Debug.Log("[CTP] Scattered 25 pucks onto the ice (avoiding tree).");
        }

        [HarmonyPatch(typeof(PuckManager), "Server_SpawnPucksForPhase")]
        public static class SpawnPucksPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PuckManager __instance, GamePhase phase)
            {
                // WARMUP: Spawn scatter immediately
                if (phase == GamePhase.Warmup)
                {
                    SpawnScatterPucks(__instance);
                    return false; 
                }

                // FACE-OFF: 
                // Let vanilla spawn the center puck so the timer UI works.
                // We start our custom countdown in CTPEnforcer to swap them out after 4s.
                if (phase == GamePhase.FaceOff)
                {
                    if (CTPEnforcer.Instance != null)
                    {
                        CTPEnforcer.Instance.StartPuckSwapCountdown();
                    }
                    return true; 
                }

                return true;
            }
        }
    }
}