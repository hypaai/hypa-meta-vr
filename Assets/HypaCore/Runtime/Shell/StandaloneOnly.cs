using UnityEngine;

namespace HypaCore
{
    /// <summary>
    /// Put on a mode scene's own camera. The camera is used when the scene is played on its own (quick testing) and
    /// removed when the shell is running, so there is only ever one camera rig.
    /// </summary>
    // Runs before everything else (including OVRManager/OVRCameraRig on the same object), so a mode scene's own rig
    // is switched off before it can start a second OVRManager next to the shell's.
    [DefaultExecutionOrder(-32000)]
    public sealed class StandaloneOnly : MonoBehaviour
    {
        void Awake()
        {
            if (HypaShell.Instance == null) return;
            gameObject.tag = "Untagged";   // so Camera.main resolves to the shell's camera this frame
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
