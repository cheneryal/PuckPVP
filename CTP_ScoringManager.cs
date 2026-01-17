using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

namespace CTP
{
    public class CTP_ScoringManager : MonoBehaviour
    {
        public static CTP_ScoringManager Instance { get; private set; }

        private int redPucksOnPad = 0;
        private int bluePucksOnPad = 0;
        
        public float internalRedScore = 0;
        public float internalBlueScore = 0;
        
        private const int ScoreToWin = 100;
        private const float ScoreMultiplier = 0.05f; 

        private bool isRoundOver = false;
        private GamePhase lastPhase = GamePhase.None;

        // Radius for capture AND sticky physics
        private const float CaptureRadius = 7.0f; 
        
        // 0.90 = Strong braking (like mud). Lower = stickier.
        private const float StickyFactor = 0.90f; 

        private Transform bluePadTx;
        private Transform redPadTx;
        
        private List<Puck> cachedPucks = new List<Puck>();

        private readonly Vector3 BlueBaseFallback = new Vector3(-36.4674f, 0.0f, 36.6389f);
        private readonly Vector3 RedBaseFallback = new Vector3(38.3345f, 0.0f, -35.8137f);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
            }
        }

        private void Start()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                StartCoroutine(ScoreUpdateRoutine());
                StartCoroutine(RefreshPuckCacheRoutine());
            }
        }

        public void ResetGame()
        {
            Debug.Log("[CTP] Resetting Scoring Manager for new game.");
            internalRedScore = 0;
            internalBlueScore = 0;
            redPucksOnPad = 0;
            bluePucksOnPad = 0;
            isRoundOver = false;
            
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Server_UpdateGameState(null, null, 1, 0, 0);
            }
        }

        public void RegisterKill(PlayerTeam victimTeam)
        {
            if (!NetworkManager.Singleton.IsServer || isRoundOver) return;

            if (victimTeam == PlayerTeam.Blue) internalRedScore += 1.0f;
            else if (victimTeam == PlayerTeam.Red) internalBlueScore += 1.0f;

            int displayRed = Mathf.FloorToInt(internalRedScore);
            int displayBlue = Mathf.FloorToInt(internalBlueScore);
            
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Server_UpdateGameState(null, null, null, displayBlue, displayRed);
            }
            CheckWinCondition();
        }

        // --- NEW: PHYSICS BRAKING LOOP ---
        private void FixedUpdate()
        {
            if (!NetworkManager.Singleton.IsServer || isRoundOver) return;
            if (bluePadTx == null || redPadTx == null) return;

            Vector2 flatBlue = new Vector2(bluePadTx.position.x, bluePadTx.position.z);
            Vector2 flatRed = new Vector2(redPadTx.position.x, redPadTx.position.z);

            // Iterate backwards to safely handle destroyed pucks
            for (int i = cachedPucks.Count - 1; i >= 0; i--)
            {
                Puck p = cachedPucks[i];
                if (p == null) 
                {
                    cachedPucks.RemoveAt(i);
                    continue;
                }

                Vector3 pPos = p.transform.position;
                Vector2 flatPuck = new Vector2(pPos.x, pPos.z);

                // Check Red Pad
                if (Vector2.Distance(flatPuck, flatRed) <= CaptureRadius)
                {
                    ApplyBrakes(p);
                }
                // Check Blue Pad
                else if (Vector2.Distance(flatPuck, flatBlue) <= CaptureRadius)
                {
                    ApplyBrakes(p);
                }
            }
        }

        private void ApplyBrakes(Puck p)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Multiply velocity by < 1 to slow it down every frame
                rb.linearVelocity *= StickyFactor;
                rb.angularVelocity *= StickyFactor;
            }
        }

        private IEnumerator RefreshPuckCacheRoutine()
        {
            while (true)
            {
                // Find all pucks every 1 second
                var found = FindObjectsByType<Puck>(FindObjectsSortMode.None);
                cachedPucks = found.ToList();
                yield return new WaitForSeconds(1.0f);
            }
        }

        private IEnumerator ScoreUpdateRoutine()
        {
            yield return new WaitForSeconds(2.0f);

            while (true)
            {
                yield return new WaitForSeconds(1.0f);

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || GameManager.Instance == null)
                    continue;

                GamePhase currentPhase = GameManager.Instance.GameState.Value.Phase;

                if (currentPhase == GamePhase.Warmup && lastPhase != GamePhase.Warmup) ResetGame();
                if (currentPhase == GamePhase.PeriodOver && lastPhase == GamePhase.Playing) CheckWinnerAndEnd();

                lastPhase = currentPhase;

                if (bluePadTx == null || redPadTx == null) LocatePads();

                if (bluePadTx != null && redPadTx != null && !isRoundOver && currentPhase == GamePhase.Playing)
                {
                    CalculatePuckCounts();
                    ProcessScores();
                }
            }
        }

        private void LocatePads()
        {
            var platforms = FindObjectsByType<CTP_CapturePlatform>(FindObjectsSortMode.None);
            foreach (var plat in platforms)
            {
                if (plat.PlatformTeam == PlayerTeam.Blue) bluePadTx = plat.transform;
                if (plat.PlatformTeam == PlayerTeam.Red) redPadTx = plat.transform;
            }

            if (bluePadTx == null)
            {
                var go = GameObject.Find("BlueCapturePad") ?? GameObject.Find("BlueZone");
                if (go != null) bluePadTx = go.transform;
            }
            if (redPadTx == null)
            {
                var go = GameObject.Find("RedCapturePad") ?? GameObject.Find("RedZone");
                if (go != null) redPadTx = go.transform;
            }

            if (bluePadTx == null) bluePadTx = CreateVirtualPad("Virtual_BluePad", BlueBaseFallback);
            if (redPadTx == null) redPadTx = CreateVirtualPad("Virtual_RedPad", RedBaseFallback);
        }

        private Transform CreateVirtualPad(string name, Vector3 pos)
        {
            GameObject go = new GameObject(name);
            go.transform.position = pos;
            return go.transform;
        }

        private void CalculatePuckCounts()
        {
            int currentRed = 0;
            int currentBlue = 0;

            if (bluePadTx == null || redPadTx == null) return;

            Vector2 flatBlue = new Vector2(bluePadTx.position.x, bluePadTx.position.z);
            Vector2 flatRed = new Vector2(redPadTx.position.x, redPadTx.position.z);

            // Use the cached list for scoring too (Efficient!)
            foreach (var p in cachedPucks)
            {
                if (p == null) continue;
                Vector3 pPos = p.transform.position;
                Vector2 flatPuck = new Vector2(pPos.x, pPos.z);

                if (Vector2.Distance(flatPuck, flatBlue) <= CaptureRadius) currentBlue++;
                else if (Vector2.Distance(flatPuck, flatRed) <= CaptureRadius) currentRed++;
            }

            if (currentBlue != bluePucksOnPad)
            {
                bluePucksOnPad = currentBlue;
                BroadcastPuckStatus("Blue", bluePucksOnPad, (int)internalBlueScore);
            }

            if (currentRed != redPucksOnPad)
            {
                redPucksOnPad = currentRed;
                BroadcastPuckStatus("Red", redPucksOnPad, (int)internalRedScore);
            }
        }

        private void BroadcastPuckStatus(string teamName, int puckCount, int powerLevel)
        {
            string colorTag = teamName.ToLower() == "red" ? "red" : "blue";
            string msg = $"<color={colorTag}>{teamName.ToUpper()} has {puckCount} pucks! Power level = {powerLevel}</color>";
            BroadcastScoreUpdate(msg);
        }

        private void ProcessScores()
        {
            bool scoreChanged = false;

            if (redPucksOnPad > 0)
            {
                internalRedScore += redPucksOnPad * ScoreMultiplier;
                scoreChanged = true;
            }
            if (bluePucksOnPad > 0)
            {
                internalBlueScore += bluePucksOnPad * ScoreMultiplier;
                scoreChanged = true;
            }

            if (scoreChanged)
            {
                int displayRed = Mathf.FloorToInt(internalRedScore);
                int displayBlue = Mathf.FloorToInt(internalBlueScore);
                GameManager.Instance.Server_UpdateGameState(null, null, null, displayBlue, displayRed);
            }
            CheckWinCondition();
        }

        private void CheckWinCondition()
        {
            int displayRed = Mathf.FloorToInt(internalRedScore);
            int displayBlue = Mathf.FloorToInt(internalBlueScore);

            if (displayRed >= ScoreToWin) EndGame(PlayerTeam.Red);
            else if (displayBlue >= ScoreToWin) EndGame(PlayerTeam.Blue);
        }

        private void CheckWinnerAndEnd()
        {
            isRoundOver = true;
            string msg = "";
            if (internalRedScore > internalBlueScore)
                msg = $"<color=red>RED WINS by Points! ({Mathf.FloorToInt(internalRedScore)} vs {Mathf.FloorToInt(internalBlueScore)})</color>";
            else if (internalBlueScore > internalRedScore)
                msg = $"<color=blue>BLUE WINS by Points! ({Mathf.FloorToInt(internalBlueScore)} vs {Mathf.FloorToInt(internalRedScore)})</color>";
            else
                msg = "<color=yellow>DRAW GAME!</color>";

            BroadcastScoreUpdate(msg);
            
            GameManager.Instance.Server_UpdateGameState(GamePhase.GameOver, null, null, null, null);
        }

        private void EndGame(PlayerTeam winningTeam)
        {
            isRoundOver = true;
            BroadcastScoreUpdate($"<color={(winningTeam == PlayerTeam.Red ? "red" : "blue")}>{winningTeam} WINS THE GAME (100 Points)!</color>");
            GameManager.Instance.Server_UpdateGameState(GamePhase.GameOver, null, null, null, null);
        }

        private void BroadcastScoreUpdate(string message)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            var uiChat = UnityEngine.Object.FindFirstObjectByType<UIChat>();
            if (uiChat != null) uiChat.Server_SendSystemChatMessage(message);
        }
    }
}