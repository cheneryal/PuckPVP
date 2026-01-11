using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    public class CTP_ScoringManager : NetworkBehaviour
    {
        public static CTP_ScoringManager Instance { get; private set; }

        private NetworkVariable<int> redPucks = new NetworkVariable<int>(0);
        private NetworkVariable<int> bluePucks = new NetworkVariable<int>(0);

        private const int ScoreToWin = 100;

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

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                StartCoroutine(ScoreUpdateCoroutine());
            }
        }

        private System.Collections.IEnumerator ScoreUpdateCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                UpdateScore();
            }
        }

        public void PuckEnteredPlatform(PlayerTeam team)
        {
            if (!IsServer) return;
            if (team == PlayerTeam.Red) redPucks.Value++;
            else bluePucks.Value++;
        }

        public void PuckExitedPlatform(PlayerTeam team)
        {
            if (!IsServer) return;
            if (team == PlayerTeam.Red) redPucks.Value = Mathf.Max(0, redPucks.Value - 1);
            else bluePucks.Value = Mathf.Max(0, bluePucks.Value - 1);
        }

        private void UpdateScore()
        {
            if (!IsServer) return;

            var gameState = GameManager.Instance.GameState.Value;
            int currentRedScore = gameState.RedScore;
            int currentBlueScore = gameState.BlueScore;

            bool scoreChanged = false;
            if (redPucks.Value > 0)
            {
                currentRedScore += redPucks.Value;
                scoreChanged = true;
            }
            if (bluePucks.Value > 0)
            {
                currentBlueScore += bluePucks.Value;
                scoreChanged = true;
            }

            if(scoreChanged)
            {
                GameManager.Instance.Server_UpdateGameState(null, null, null, currentBlueScore, currentRedScore);
                BroadcastScoreUpdate($"<color=red>Red: {currentRedScore}</color> | <color=blue>Blue: {currentBlueScore}</color>");
            }

            if (currentRedScore >= ScoreToWin)
            {
                EndRound(PlayerTeam.Red);
            }
            else if (currentBlueScore >= ScoreToWin)
            {
                EndRound(PlayerTeam.Blue);
            }
        }
        
        private void BroadcastScoreUpdate(string message)
        {
            try
            {
                if (!NetworkManager.Singleton.IsServer) return;
        
                var uiChat = FindFirstObjectByType<UIChat>();
                if (uiChat != null)
                {
                    uiChat.Server_SendSystemChatMessage(message);
                }
            }
            catch {}
        }

        private void EndRound(PlayerTeam winningTeam)
        {
            // Logic for ending the round and awarding a point will be added here
            BroadcastScoreUpdate($"<color={(winningTeam == PlayerTeam.Red ? "red" : "blue")}>{winningTeam} wins the round!</color>");
            
            var gameState = GameManager.Instance.GameState.Value;
            GameManager.Instance.Server_UpdateGameState(null, null, gameState.Period + 1, 0, 0);
        }
    }
}
