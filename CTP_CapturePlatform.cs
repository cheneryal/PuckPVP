using UnityEngine;
using Unity.Netcode;

namespace CTP
{
    public class CTP_CapturePlatform : MonoBehaviour
    {
        [SerializeField]
        private PlayerTeam platformTeam = default;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Puck"))
            {
                Puck puck = other.GetComponent<Puck>();
                if (puck != null)
                {
                    CTP_ScoringManager.Instance.PuckEnteredPlatform(platformTeam);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Puck"))
            {
                Puck puck = other.GetComponent<Puck>();
                if (puck != null)
                {
                    CTP_ScoringManager.Instance.PuckExitedPlatform(platformTeam);
                }
            }
        }
    }
}
