using HarmonyLib;
using UnityEngine;
using System.IO;
using System.Reflection;
using System;
using System.Collections.Generic;

namespace CTP
{
    [HarmonyPatch]
    public static class TeamPucks
    {
        [HarmonyPatch(typeof(PuckManager), "SetPuckPositions")]
        public static class ClearDefaultFaceOffPuckPatch
        {
            [HarmonyPostfix]
            public static void Postfix(List<PuckPosition> puckPositions)
            {
                if (puckPositions != null)
                {
                    puckPositions.RemoveAll(p => p != null && p.Phase == GamePhase.FaceOff);
                }
            }
        }

        [HarmonyPatch(typeof(PuckManager), "Server_SpawnPucksForPhase")]
        public static class SpawnPucksPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(PuckManager __instance, GamePhase phase)
            {
                if (phase == GamePhase.FaceOff)
                {
                    // Remove all existing pucks to ensure a clean slate
                    __instance.Server_DespawnPucks(true);

                    // Define the rectangular exclusion zone for the tree
                    float treeExclusionMinX = -7.0f;
                    float treeExclusionMaxX = 5.0f;
                    float treeExclusionMinZ = -4.0f;
                    float treeExclusionMaxZ = 8.0f;

                    // Spawn 25 new pucks, avoiding the exclusion zone
                    for (int i = 0; i < 25; i++)
                    {
                        Vector3 spawnPosition;
                        do
                        {
                            spawnPosition = new Vector3(UnityEngine.Random.Range(-15f, 15f), 0.0038f, UnityEngine.Random.Range(-15f, 15f));
                        } 
                        while (spawnPosition.x >= treeExclusionMinX && spawnPosition.x <= treeExclusionMaxX &&
                               spawnPosition.z >= treeExclusionMinZ && spawnPosition.z <= treeExclusionMaxZ);
                        
                        __instance.Server_SpawnPuck(spawnPosition, Quaternion.identity, Vector3.zero, false);
                    }

                    // Skip the original puck spawning method to prevent default pucks from spawning
                    return false; 
                }
                
                // For any other game phase, allow the original method to run
                return true;
            }
        }
    }
}
