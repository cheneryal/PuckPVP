using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    public class CTP_ScoringManager : MonoBehaviour
    {
        public static CTP_ScoringManager Instance { get; private set; }

        private int redPucksOnPad = 0;
        private int bluePucksOnPad = 0;
        
        private float internalRedScore = 0;
        private float internalBlueScore = 0;
        
        private const int ScoreToWin = 100;
        private const float ScoreMultiplier = 0.05f; 

        private bool isRoundOver = false;
        private GamePhase lastPhase = GamePhase.None;

        // Radius for capture
        private const float CaptureRadius = 7.0f; 

        // Dynamic Transforms
        private Transform bluePadTx;
        private Transform redPadTx;

        // Fallbacks
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
            }
        }

        private void ResetGame()
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

        private IEnumerator ScoreUpdateRoutine()
        {
            yield return new WaitForSeconds(2.0f);

            while (true)
            {
                yield return new WaitForSeconds(1.0f);

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || GameManager.Instance == null)
                    continue;

                GamePhase currentPhase = GameManager.Instance.GameState.Value.Phase;

                if (currentPhase == GamePhase.Warmup && lastPhase != GamePhase.Warmup)
                {
                    ResetGame();
                }

                if (currentPhase == GamePhase.PeriodOver && lastPhase == GamePhase.Playing)
                {
                    CheckWinnerAndEnd();
                }

                lastPhase = currentPhase;

                if (bluePadTx == null || redPadTx == null)
                {
                    LocatePads();
                }

                if (bluePadTx != null && redPadTx != null && 
                    !isRoundOver && 
                    currentPhase == GamePhase.Playing)
                {
                    CalculatePuckCounts();
                    ProcessScores();
                }
            }
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

            var pucks = FindObjectsByType<Puck>(FindObjectsSortMode.None);
            
            Vector3 flatBlue = new Vector3(bluePadTx.position.x, 0, bluePadTx.position.z);
            Vector3 flatRed = new Vector3(redPadTx.position.x, 0, redPadTx.position.z);

            foreach (var p in pucks)
            {
                if (p == null) continue;
                Vector3 pPos = p.transform.position;
                Vector3 flatPuck = new Vector3(pPos.x, 0, pPos.z);

                if (Vector3.Distance(flatPuck, flatBlue) <= CaptureRadius) currentBlue++;
                else if (Vector3.Distance(flatPuck, flatRed) <= CaptureRadius) currentRed++;
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

            int displayRed = Mathf.FloorToInt(internalRedScore);
            int displayBlue = Mathf.FloorToInt(internalBlueScore);

            if (scoreChanged)
            {
                GameManager.Instance.Server_UpdateGameState(null, null, null, displayBlue, displayRed);
            }

            if (displayRed >= ScoreToWin) EndGame(PlayerTeam.Red);
            else if (displayBlue >= ScoreToWin) EndGame(PlayerTeam.Blue);
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