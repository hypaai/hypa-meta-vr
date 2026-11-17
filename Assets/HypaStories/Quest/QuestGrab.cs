using System;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HypaStories.Quest
{
    // Compiled only when the Meta XR Core SDK v207+ is installed (asmdef defineConstraints: HYPA_META_XR).

    /// <summary>
    /// Picking things up with the Meta XR Interaction SDK (<see cref="StoryGrab"/>): the hut's household items.
    ///
    /// A thing gets the SDK's own components: a <see cref="Grabbable"/> (it follows the hand, kinematic while held, with the
    /// throw on release), a <see cref="GrabInteractable"/> for controllers (grip) and a <see cref="HandGrabInteractable"/> for
    /// hands (pinch or palm).
    ///
    /// The app's camera rig (the Building Blocks rig in HypaShell, or a scene's own) carries no Interaction SDK interactors, so
    /// the first thing made grabbable adds a small set under it, from the SDK's prefabs (<see cref="StoryAssets"/>): the camera
    /// rig reference, the hand and controller data sources, a hand grab interactor per hand and a grab interactor per
    /// controller. Only grab: no SDK rays, poke or locomotion, and its colliders sit on the Ignore Raycast layer, so HypaCore's pinch
    /// pointer and the camp's walking are untouched.
    /// A scene that already has the SDK's interactors keeps its own.
    /// </summary>
    public static class QuestGrab
    {
        const int IgnoreRaycast = 2; // Unity's built-in "Ignore Raycast" layer
        static GameObject _rig;
        static bool _failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            _rig = null; _failed = false;
            StoryGrab.Make = Make;
        }

        static Func<bool> Make(Rigidbody body, StoryAssets assets)
        {
            if (body == null || !EnsureRig(assets)) return null;
            var go = body.gameObject;
            var wasActive = go.activeSelf;
            go.SetActive(false); // the SDK's components read their fields in Awake: add and wire them asleep
            try
            {
                var grabbable = go.AddComponent<Grabbable>();
                grabbable.InjectOptionalRigidbody(body);
                grabbable.InjectOptionalKinematicWhileSelected(true);
                grabbable.InjectOptionalThrowWhenUnselected(true);

                var forControllers = go.AddComponent<GrabInteractable>();
                forControllers.InjectAllGrabInteractable(body);
                forControllers.InjectOptionalPointableElement(grabbable);

                var forHands = go.AddComponent<HandGrabInteractable>();
                forHands.InjectAllHandGrabInteractable(GrabTypeFlags.All, body, GrabbingRule.DefaultPinchRule, GrabbingRule.DefaultPalmRule);
                forHands.InjectOptionalPointableElement(grabbable);

                return () => grabbable != null && grabbable.SelectingPointsCount > 0;
            }
            finally { go.SetActive(wasActive); }
        }

        /// <summary>The grab interactors under the camera rig. False when there is no rig (the editor without XR) or no prefabs.</summary>
        static bool EnsureRig(StoryAssets a)
        {
            if (_rig != null) return true;
            if (_failed) return false;
            if (Object.FindAnyObjectByType<HandGrabInteractor>() != null || Object.FindAnyObjectByType<GrabInteractor>() != null) return true; // the scene has its own
            var ovr = Object.FindAnyObjectByType<OVRCameraRig>();
            if (ovr == null) return false; // no headset rig here: things stay where they are
            if (a == null || a.grabInteraction == null || a.grabHands == null || a.grabControllers == null
                || a.grabHandInteractor == null || a.grabControllerInteractor == null)
            {
                _failed = true;
                Debug.LogWarning("Hypa Stories: the Interaction SDK prefabs aren't set on StoryAssets (run Hypa > Stories > Create Story Scene), so nothing can be picked up.");
                return false;
            }

            var root = new GameObject("Hypa Grab Rig");
            root.SetActive(false); // wired asleep, started whole
            root.transform.SetParent(ovr.transform, false);
            try
            {
                // the camera rig reference, the tracking-to-world transform and the headset
                var core = Object.Instantiate(a.grabInteraction, root.transform);
                core.name = "OVRInteraction";
                var rigRef = core.GetComponentInChildren<OVRCameraRigRef>(true);
                rigRef.InjectAllOVRCameraRigRef(ovr, false);
                var toWorld = core.GetComponentInChildren<TrackingToWorldTransformerOVR>(true);
                toWorld.InjectAllTrackingToWorldTransformerOVR(rigRef);

                // hands: the SDK's data sources, then a grab interactor on each
                var hands = Object.Instantiate(a.grabHands, core.transform);
                hands.name = "OVRHandsDataSource";
                foreach (var source in hands.GetComponentsInChildren<FromOVRHandDataSource>(true))
                {
                    SetPrivate(source, "_cameraRigRef", rigRef); // the one reference this source has no Inject for
                    source.InjectTrackingToWorldTransformer(toWorld);
                }
                foreach (var hand in hands.GetComponents<Hand>())
                {
                    var grab = Object.Instantiate(a.grabHandInteractor, hands.transform);
                    grab.name = "HandGrabInteractor";
                    grab.GetComponentInChildren<HandRef>(true).InjectHand(hand);
                }

                // controllers: the same, with the grip button
                var controllers = Object.Instantiate(a.grabControllers, core.transform);
                controllers.name = "OVRControllersDataSource";
                foreach (var source in controllers.GetComponentsInChildren<FromOVRControllerDataSource>(true))
                {
                    source.InjectCameraRigRef(rigRef);
                    source.InjectTrackingToWorldTransformer(toWorld);
                }
                foreach (var controller in controllers.GetComponents<Controller>())
                {
                    var grab = Object.Instantiate(a.grabControllerInteractor, controllers.transform);
                    grab.name = "ControllerGrabInteractor";
                    grab.GetComponentInChildren<ControllerRef>(true).InjectController(controller);
                }

                // The interactors carry trigger colliders at the hands, and HypaCore's pinch ray starts just behind the hand: on the
                // default layer they would be the first thing it hits, and no button or card could be pinched. Physics.Raycast
                // skips the Ignore Raycast layer, which still touches the things to be grabbed.
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IgnoreRaycast;
                root.SetActive(true);
                _rig = root;
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Object.Destroy(root);
                Debug.LogError("Hypa Stories: couldn't set up the Interaction SDK grab rig, so nothing can be picked up: " + e);
                return false;
            }
        }

        static void SetPrivate(object target, string field, object value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f == null) continue;
                f.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().Name, field);
        }
    }
}
