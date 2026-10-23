using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Trigger volume (tag EntryZone) at the end of the vestibule behind the service door. Walking into it starts Block 1 (after the trail)
    /// or Block 2 (after the first questionnaire), by loading SCN_SmokeRoom additively.
    /// </summary>
    public class BlockLauncher : MonoBehaviour
    {
        [SerializeField] BoxCollider zone;
        [SerializeField] float relaunchGuardSeconds = 3f;

        float _lastLaunch = -99f;

        public BoxCollider Zone { get => zone; set => zone = value; }

        void Update()
        {
            var rig = PlayerRig.Instance; var sm = SessionManager.Instance; var sf = SceneFlow.Instance;
            if (zone == null || rig == null || rig.Head == null || sm == null || sf == null || sf.IsBusy) return;
            if (Time.time - _lastLaunch < relaunchGuardSeconds) return;
            Vector3 p = rig.Head.position; p.y = zone.bounds.center.y;
            if (!zone.bounds.Contains(p)) return;

            int block = 0;
            if (sm.Current == Stage.TrailFollowed) { sm.Advance(Stage.Block1); block = 1; }
            else if (sm.Current == Stage.Block1 && EgressBlockController.CompletedBlocks == 0) block = 1;
            else if (sm.Current == Stage.Block2 && EgressBlockController.CompletedBlocks == 1) block = 2;
            if (block == 0) return;

            sm.CurrentBlock = block;
            _lastLaunch = Time.time;
            DS.Log($"Entering the smoke room for block {block}");
            sf.EnterSmokeRoom();
        }
    }
}
