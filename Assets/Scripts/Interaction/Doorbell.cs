using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Press it to call the inspector early (anyone can — that's where the arguments start).
    public class Doorbell : MonoBehaviour, IPressable
    {
        [SerializeField] Transform button;

        public string PressPrompt => JobManager.Instance != null && JobManager.Instance.IsSpawned && JobManager.Instance.Phase == JobPhase.Renovation
            ? "ring the doorbell: we're done, start the inspection" : null;

        public void Press(int playerIndex)
        {
            var job = JobManager.Instance;
            if (job == null || job.Phase != JobPhase.Renovation) return;
            job.RingDoorbellRpc(playerIndex);
        }
    }
}
