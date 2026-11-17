using System;
using System.Collections.Generic;
using System.Reflection;
using HypaCore;
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
    /// hands (pinch or palm) when you reach it, and a <see cref="DistanceGrabInteractable"/> and a
    /// <see cref="DistanceHandGrabInteractable"/> for when you don't: point at it and pinch (or press the grip) and it comes to
    /// your hand. Things in the hut stand on the floor by the wall, and most people won't walk over and bend down to them.
    ///
    /// The app's camera rig (the Building Blocks rig in HypaShell, or a scene's own) carries no Interaction SDK interactors, so
    /// the first thing made grabbable adds a small set under it, from the SDK's prefabs (<see cref="StoryAssets"/>): the camera
    /// rig reference, the hand and controller data sources, and per hand and per controller a close grab and a distance grab
    /// in one best-select group (so only one of them takes a thing). Only grab: no SDK rays, poke or locomotion, and its colliders sit on the Ignore Raycast layer, so HypaCore's pinch
    /// pointer and the camp's walking are untouched.
    /// A scene that already has the SDK's interactors keeps its own.
    ///
    /// HypaCore's pinch ray and the distance grab both answer a pinch. The story cards hover just over their things, so a
    /// distance grab only starts while that ray is not on a pinch target (a card, a button): pinching a card plays it and
    /// leaves the thing under it where it is (<see cref="NotOnPinchTarget"/>).
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

        static Func<int> Make(Rigidbody body, StoryAssets assets, bool heavy)
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
                grabbable.InjectOptionalThrowWhenUnselected(!heavy); // a heavy pot is set down, not thrown
                if (heavy)
                {
                    // both hands to lift it; one hand only rocks it (QuestCarry)
                    grabbable.InjectOptionalOneGrabTransformer(go.AddComponent<TooHeavyOneHand>());
                    grabbable.InjectOptionalTwoGrabTransformer(go.AddComponent<CarryTwoHands>());
                    grabbable.MaxGrabPoints = 2;
                    body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // let go, it lands upright
                }

                var forControllers = go.AddComponent<GrabInteractable>();
                forControllers.InjectAllGrabInteractable(body);
                forControllers.InjectOptionalPointableElement(grabbable);

                var forHands = go.AddComponent<HandGrabInteractable>();
                forHands.InjectAllHandGrabInteractable(GrabTypeFlags.All, body, GrabbingRule.DefaultPinchRule, GrabbingRule.DefaultPalmRule);
                forHands.InjectOptionalPointableElement(grabbable);

                // from a distance (the default movement: it flies to your hand, then stays in it): light things only. A heavy one
                // pulled to the hand would fly at your face; you go to it and take it in both hands.
                if (!heavy)
                {
                    var farControllers = go.AddComponent<DistanceGrabInteractable>();
                    farControllers.InjectAllGrabInteractable(body);
                    farControllers.InjectOptionalPointableElement(grabbable);

                    var farHands = go.AddComponent<DistanceHandGrabInteractable>();
                    farHands.InjectAllDistanceHandGrabInteractable(GrabTypeFlags.All, body, GrabbingRule.DefaultPinchRule, GrabbingRule.DefaultPalmRule);
                    farHands.InjectOptionalPointableElement(grabbable);
                }

                return () => grabbable != null ? grabbable.SelectingPointsCount : 0;
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
                || a.grabHandInteractor == null || a.grabControllerInteractor == null
                || a.grabDistanceHandInteractor == null || a.grabDistanceControllerInteractor == null)
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
                var hmd = core.GetComponentInChildren<Hmd>(true);

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
                    var near = Object.Instantiate(a.grabHandInteractor, hands.transform);
                    near.name = "HandGrabInteractor";
                    foreach (var r in near.GetComponentsInChildren<HandRef>(true)) r.InjectHand(hand);
                    var far = Object.Instantiate(a.grabDistanceHandInteractor, hands.transform);
                    far.name = "DistanceHandGrabInteractor";
                    foreach (var r in far.GetComponentsInChildren<HandRef>(true)) r.InjectHand(hand);
                    foreach (var r in far.GetComponentsInChildren<HmdRef>(true)) r.InjectHmd(hmd);
                    Group(hands.transform, "Hand Grab Group", near.GetComponentInChildren<HandGrabInteractor>(true),
                        far.GetComponentInChildren<DistanceHandGrabInteractor>(true));
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
                    var near = Object.Instantiate(a.grabControllerInteractor, controllers.transform);
                    near.name = "ControllerGrabInteractor";
                    foreach (var r in near.GetComponentsInChildren<ControllerRef>(true)) r.InjectController(controller);
                    var far = Object.Instantiate(a.grabDistanceControllerInteractor, controllers.transform);
                    far.name = "ControllerDistanceGrabInteractor";
                    foreach (var r in far.GetComponentsInChildren<ControllerRef>(true)) r.InjectController(controller);
                    Group(controllers.transform, "Controller Grab Group", near.GetComponentInChildren<GrabInteractor>(true),
                        far.GetComponentInChildren<DistanceGrabInteractor>(true));
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

        /// <summary>
        /// A close grab and a distance grab for one hand or controller, in a best-select group: one of them at a time takes a
        /// thing. The distance one only starts while HypaCore's pinch ray is off the cards and buttons (<see cref="NotOnPinchTarget"/>).
        /// </summary>
        static void Group(Transform parent, string name, IInteractor near, IInteractor far)
        {
            var farMono = (MonoBehaviour)far;
            var gate = farMono.gameObject.AddComponent<NotOnPinchTarget>();
            gate.Interactor = far;
            gate.Inner = GetPrivate(farMono, "_activeState") as IActiveState; // keep its own condition (hand or controller tracked)
            // on the generic Interactor<,> base, so not reachable through IInteractor
            farMono.GetType().GetMethod("InjectOptionalActiveState", BindingFlags.Instance | BindingFlags.Public).Invoke(farMono, new object[] { gate });
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<BestSelectInteractorGroup>().InjectAllInteractorGroupBestSelect(new List<IInteractor> { near, far });
        }

        static object GetPrivate(object target, string field)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
            }
            return null;
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

    /// <summary>
    /// Lets a distance grab start only while HypaCore's pinch ray is not on a pinch target (a story card over a thing, a
    /// button), so pinching a card never also pulls the thing under it. Once it holds something it stays active (an inactive
    /// interactor drops what it holds), and it keeps the interactor's own condition (<see cref="Inner"/>: the hand or
    /// controller is tracked).
    /// </summary>
    sealed class NotOnPinchTarget : MonoBehaviour, IActiveState
    {
        public IInteractor Interactor;
        public IActiveState Inner;

        public bool Active
        {
            get
            {
                if (Inner != null && !Inner.Active) return false;
                if (Interactor != null && Interactor.State == InteractorState.Select) return true;
                var p = PinchPointer.Instance;
                return p == null || p.Hover == null;
            }
        }
    }
}
