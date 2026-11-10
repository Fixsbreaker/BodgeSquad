using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Press it to call the inspector early (anyone can — that's where the arguments start).
    public class Doorbell : MonoBehaviour, IPressable
    {
        [SerializeField] Transform button;

        public string PressPrompt => JobManager.Instance?.Phase == JobPhase.Renovation ? "call the inspector" : null;

        public void Press(int playerIndex)
        {
            var job = JobManager.Instance;
            if (job == null || job.Phase != JobPhase.Renovation) return;
            if (button != null) button.localPosition += new Vector3(0, 0, -0.02f);
            job.Say($"P{playerIndex + 1} rang the doorbell!");
            job.CallInspector();
        }
    }
}
