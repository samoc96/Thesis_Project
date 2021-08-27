/******************************************************************************\
* Copyright (C) 2016 Dexta Robotics. All rights reserved.                      *
* Use subject to the terms of the Libdexmo Unity SDK Agreement at              *
* LibdexmoUnitySDKLicense.txt                                                  *
\******************************************************************************/


using UnityEngine;
using System;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using Libdexmo.Client;
using Libdexmo.Client.NotifyEvent;
using Libdexmo.Model;
using Libdexmo.Unity.Core;
using Libdexmo.Unity.Core.HandController;
using Libdexmo.Unity.Core.Model;
using Libdexmo.Unity.Core.Model.Calibration;
using Libdexmo.Unity.Core.Utility;
using Debug = UnityEngine.Debug;
using Joint = Libdexmo.Model.Joint;

namespace Libdexmo.Unity.HandController
{
    /// <summary>
    /// This class controls update of all hand models from Dexmo devices and send force feedback commands
    /// to Dexmo devices correspondingly.
    /// </summary>
    public class DexmoController : MonoBehaviour, IDexmoController
    {

        // Settings (See "IDexmoController Properties" region for more details)
        #region IDexmoControllerSettings
        [SerializeField]
        private bool _useVRControllerTracking = true;
        // Not fully supported yet
        private bool _useDexmoIMUTracking = false;
        // Unused feature
        private bool _impedanceControlOnPickingOnly = false;
        #endregion

        /// <summary>
        /// Transform that represents the VR origin. Can be null if Dexmo is not tracked
        /// using VR controllers.
        /// </summary>
        [Tooltip("Transform that represents the VR origin. Can be null if Dexmo is not tracked using VR controllers")]
        public Transform VRCameraOrigin;

        /// <summary>
        /// VR left controller used to track left hand.
        /// </summary>
        [Tooltip("VR left controller used to track left hand.")]
        public GameObject VRControllerLeft;

        /// <summary>
        /// VR right controller used to track right hand.
        /// </summary>
        [Tooltip("VR right controller used to track right hand.")]
        public GameObject VRControllerRight;

        #region IDexmoController Properties
        /// <summary>
        /// The GameObject that this script is attached to. Used for the IDexmoController interface.
        /// </summary>
        public GameObject BindedGameObject { get { return gameObject; } }

        /// <summary>
        /// The layer of "Hand". All hand model colliders will be in "Hand" layer to avoid collision detection
        /// among themselves.
        /// </summary>
        public int HandLayer { get; private set; }

        /// <summary>
        /// Whether to move hand model by VR controllers.
        /// </summary>
        public bool UseVRControllerTracking { get { return _useVRControllerTracking; } }

        /// <summary>
        /// Whether to update hand models' attitude from Dexmo IMU data. It is currently not fully implemented yet.
        /// </summary>
        public bool UseDexmoIMUTracking { get { return _useDexmoIMUTracking; } }

        /// <summary>
        /// If set to true, it only allows force feedback when hand models grasp and start picking up objects, so there will be no force feedback
        /// when hand models are just touching objects. Set to false by default.
        /// </summary>
        public bool ImpedanceControlOnPickingOnly { get { return _impedanceControlOnPickingOnly; } }

        /// <summary>
        /// Pool of hand model pairs. All hand models can be configured here in the Unity Inspector.
        /// </summary>
        /// <remarks>
        /// All hand models are configured in HandPool, which has a list of hand pairs (HandPool.HandPairs).
        /// In any pair of hand model, it is possible that one of the hand model is set to null. 
        /// For example, we only want to use right hand model, so we can set all left hand models
        /// to null. In this case, only right hand models will get updated by the DexmoController and has
        /// force feedback.
        /// </remarks>
        public UnityHandPool HandPool { get { return _handPool; } }

        /// <summary>
        /// A list of hand controller instances that manage the interaction of its corresponding hand model. Each hand model
        /// will be internally assigned a UnityHandController.
        /// </summary>
        /// <remarks>
        /// Each hand model configured in HandPool will be assigned a UnityHandController instance to manage the
        /// interaction of the hand model. The index of the list of HandControllerPairs correspond to the index of
        /// the list of HandPool.HandPairs. If any hand model is set to null, it will not be assigned a UnityHandController.
        /// E.g. If for a hand model pair, only the right hand model is configured and left hand model is set to null,
        /// its corresponding hand controller pair will be a null for left and a UnityHandController instance for right.
        /// <seealso cref="UnityHandPool.HandPairs"/>
        /// </remarks>
        public List<LeftRightPair<UnityHandController>> HandControllerPairs
        { get { return _handControllerPairs; } }
        #endregion

        /// <summary>
        /// Libdexmo client controller that manages the connection with Libdexmo server application.
        /// </summary>
        /// <remarks>
        /// Libdexmo client controller will contain updated finger rotation data. Force feedback
        /// commands are also sent through libdexmo client controller.
        /// </remarks>
        public Controller LibdexmoClientController { get; private set; }

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static DexmoController Instance;


        public class RawHandDataEventArgs : EventArgs
        {
            public Hand HandData { get; set; }
            public RawHandDataEventArgs(Hand handData)
            {
                HandData = handData;
            }
        }

        public event EventHandler<RawHandDataEventArgs> UpdateRawHandDataEvent;


        // This script manages all the hand models' calibration profile, i.e. the mapping between normalized rotation
        // obtained from Dexmo device and the actual rotation of hand model in Unity's world.
        private CalibrationProfileManager _handModelCalibProfileManager;

        // Stores pairs of UnityHandController corresponding to the hand pairs in HandPool.
        private List<LeftRightPair<UnityHandController>> _handControllerPairs;
        // Pairs of VR controller GameObject corresponding to the hand pairs in HandPool.
        private List<LeftRightPair<GameObject>> _vrControllerObjReferencePairs;
        // List of hand controller instances created. If hand model is set to null in HandPool, no hand controller
        // instance will be created for that model.
        private List<UnityHandController> _handControllers;
        // Temporary HashSet used to figure out what are the current Dexmo devices connected.
        private HashSet<int> _connectedHandIdsTemp;
        // Actual hand pool in which all hand models are configured in Unity Inspector.
        [SerializeField]
        private UnityHandPool _handPool = new UnityHandPool();
        // Used to quickly switch VR controller tracking reference.
        private bool _isLeftRightReferenceReversed = false;

        #region Keyboard control test
        /// <summary>
        /// When UseVRControllerTracking is set to false, hand model will follow the position and the rotation
        /// of this Transform. Used for debug purpose.
        /// </summary>
        public Transform DummyHandMotionTransform { get { return _dummyHandMotionTransform; } }

        public bool ObjectTouchedFixed { get => objectTouchedFixed; set => objectTouchedFixed = value; }

        /// <summary>
        /// Whether to use keyboard to control fingers of hand models instead of being updated from Dexmo.
        /// Used for debug purpose.
        /// </summary>
        [Header("Debug Info:")]
        [SerializeField]
        private bool _keyboardControlTest = false;

        /// <summary>
        /// If set to true, and _keyboardControlTest is also set to true, the finger rotation of the right
        /// hand model will be controlled and right hand model will follow DummyHandMotionTransform (if not null).
        /// Otherwise, it is the left hand model that is under keyboard control.
        /// </summary>
        [SerializeField]
        private bool _keyboardControlRight = true;

        [SerializeField]
        private Transform _dummyHandMotionTransform;

        /// <summary>
        /// Normalized rotation of thumb rotate in the scale of 0 to 1, with 0 being straight and 1 being most bent.
        /// </summary>
        public float _keyboardControlledThumbRotate = 0;

        /// <summary>
        /// Normalized rotation of thumb split in the scale of 0 to 1, with 0 being straight and 1 being most bent.
        /// </summary>
        public float _keyboardControlledThumbSplit = 0;

        /// <summary>
        /// Normalized rotation of the rest of fingers' split in the scale of 0 to 1, with 0 being straight and 1 being most bent.
        /// </summary>
        public float _keyboardControlledFingerSplit = 0;

        /// <summary>
        /// List of normalized rotation of bending of fingers excluding thumb in the scale of 0 to 1.
        /// </summary>
        public List<float> _keyboardControlledFingerBend = new List<float> { 0, 0, 0, 0, 0 };
        #endregion

        #region LibdexmoClientController event handler
        /// <summary>
        /// Libdexmo client controller event handler. Handles the event of change of
        /// connection status.
        /// </summary>
        /// <param name="sender">Reference to the object that invokes the event</param>
        /// <param name="e">The current connection status information.</param>
        private void ControllerStatusChangedHandler(object sender,
            ConnectionStatusChangedEventArgs e)
        {
            if (e.Status == ConnectionStatus.Connected)
            {
                OnConnect();
            }
            else if (e.Status == ConnectionStatus.Disconnected)
            {
                OnDisconnect();
            }
        }

        private void OnConnect()
        {
            print("LibdexmoClientController is connected");
        }

        private void OnDisconnect()
        {
            print("LibdexmoClientController is disconnected.");
            ResetHandControllers();
        }
        #endregion

        /// <summary>
        /// Initialization of some fields in Awake()
        /// </summary>
        private void AwakeInit()
        {
            HandLayer = LayerMask.NameToLayer("Hand");
            if (HandLayer == -1)
            {
                Debug.LogError("Must define \"Hand\" layer");
            }
            _vrControllerObjReferencePairs = new List<LeftRightPair<GameObject>>();
            _vrControllerObjReferencePairs.Add(new LeftRightPair<GameObject>(
                    VRControllerLeft, VRControllerRight));
            _handPool.Init();
            _connectedHandIdsTemp = new HashSet<int>();
        }

        /// <summary>
        /// In the Inspector, users can dynamically adjust the size of the list of hand pairs
        /// in _handPool, so to ensure new hand pairs are initialized properly, MonoBehaviour.OnValidate
        /// is used.
        /// </summary>
        void OnValidate()
        {
            _handPool.OnValidate();
        }

        /// <summary>
        /// Initialization of some fields in Start()
        /// </summary>
        private void StartInit()
        {
            setlatestHandData(firstAngles);
            InitHandControllers();
            AttachVRCameraOrigin();
        }

        /// <summary>
        /// Attach VR Camera origin game object to hand controllers. VR camera origin
        /// is the origin of the tracking coordinate.
        /// </summary>
        private void AttachVRCameraOrigin()
        {
            foreach (var pair in _handControllerPairs)
            {
                var left = pair.Left;
                if (left != null)
                {
                    left.AttachVRCameraOrigin(VRCameraOrigin);
                }
                var right = pair.Right;
                if (right != null)
                {
                    right.AttachVRCameraOrigin(VRCameraOrigin);
                }
            }
        }

        /// <summary>
        /// Reset some flags of hand controllers. Used when client is disconnected.
        /// </summary>
        private void ResetHandControllers()
        {
            foreach (UnityHandController handController in _handControllers)
            {
                handController.Active = false;
            }
        }

        /// <summary>
        /// Initialize hand controller pairs corresponding to each of the hand model pairs
        /// configured in _handPool.HandPairs list. If any of the hand model in
        /// _handPool.HandPairs is not configured properly, its corresponding hand controller
        /// will be null and will not be updated afterwards.
        /// </summary>
        private void InitHandControllers()
        {
            _handModelCalibProfileManager = CalibrationProfileManager.Instance;
            _handControllerPairs = new List<LeftRightPair<UnityHandController>>();
            _handControllers = new List<UnityHandController>();
            int n = _handPool.HandPairs.Count;
            int vrControllerPairNum = _vrControllerObjReferencePairs.Count;
            for (int i = 0; i < n; i++)
            {
                UnityHandRepresentation leftRep = _handPool.HandPairs[i].Left;
                UnityHandController left = null;
                if (leftRep.Initialized)
                {
                    HandModelCalibrationProfile profile =
                        _handModelCalibProfileManager.FindProfile(
                            leftRep.GraphicsHandModel.Hand);
                    left = new UnityHandController(false, leftRep, this);
                    GameObject vrControllerObj = i < vrControllerPairNum ?
                        _vrControllerObjReferencePairs[i].Left : null;
                    left.StartInit(profile, vrControllerObj,
                        leftRep.BendInwardLocalDirection);
                    _handControllers.Add(left);
                }
                UnityHandRepresentation rightRep = _handPool.HandPairs[i].Right;
                UnityHandController right = null;
                if (rightRep.Initialized)
                {
                    HandModelCalibrationProfile profile =
                        _handModelCalibProfileManager.FindProfile(
                            rightRep.GraphicsHandModel.Hand);
                    right = new UnityHandController(true, rightRep, this);
                    GameObject vrControllerObj = i < vrControllerPairNum ?
                        _vrControllerObjReferencePairs[i].Right : null;
                    right.StartInit(profile, vrControllerObj,
                        rightRep.BendInwardLocalDirection);
                    _handControllers.Add(right);
                }
                LeftRightPair<UnityHandController> pair =
                    new LeftRightPair<UnityHandController>(left, right);
                _handControllerPairs.Add(pair);
            }
        }

        /// <summary>
        /// When VR controller GameObject is used as the reference to move hand models, it switches
        /// the left/right VR controller reference for the left/right hand models, so if originally
        /// left VR conroller moves the left hand model, it is now the right VR controller that moves
        /// the left hand model.
        /// </summary>
        private void SwitchHandPositionAndAttitudeReference()
        {
            _isLeftRightReferenceReversed = !_isLeftRightReferenceReversed;
            int pairNum = _vrControllerObjReferencePairs.Count;
            int handControllerPairNum = _handControllerPairs.Count;
            for (int i = 0; i < pairNum && i < handControllerPairNum; i++)
            {
                LeftRightPair<UnityHandController> handControllerPair = _handControllerPairs[i];
                LeftRightPair<GameObject> vrControllerObjReferencePair = _vrControllerObjReferencePairs[i];
                GameObject leftReference = vrControllerObjReferencePair.Left;
                GameObject rightReference = vrControllerObjReferencePair.Right;
                UnityHandController leftHandController = handControllerPair.Left;
                UnityHandController rightHandController = handControllerPair.Right;
                if (_isLeftRightReferenceReversed)
                {
                    if (leftHandController != null)
                    {
                        leftHandController.AssignPositionAndAttitudeReference(rightReference);
                    }
                    if (rightHandController != null)
                    {
                        rightHandController.AssignPositionAndAttitudeReference(leftReference);
                    }
                }
                else
                {
                    if (leftHandController != null)
                    {
                        leftHandController.AssignPositionAndAttitudeReference(leftReference);
                    }
                    if (rightHandController != null)
                    {
                        rightHandController.AssignPositionAndAttitudeReference(rightReference);
                    }
                }
            }
        }

        /// <summary>
        /// First initialization step.
        /// </summary>
        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                if (Instance != this)
                {
                    //DestroyImmediate(gameObject);
                }
            }

            LibdexmoClientController = new Controller(true);
            LibdexmoClientController.ConnectionStatusChanged += ControllerStatusChangedHandler;
            AwakeInit();
        }

        public GameObject agent;

        /// <summary>
        /// Second initialization step.
        /// </summary>
        void Start()
        {
            StartInit();
        }

        /// <summary>
        /// When this script is destroyed, send commands to release all Dexmo fingers if
        /// any of them is under force feedback. Then dispose resources.
        /// </summary>
        void OnDestroy()
        {
            if (LibdexmoClientController != null)
            {
                bool[] targets = new bool[] { true, true, true, true, true };
                foreach (UnityHandController handController in _handControllers)
                {
                    if (handController.Active)
                    {
                        int assignedId = handController.DeviceInfo.AssignedId;
                        LibdexmoClientController.StopImpedanceControlFingers(assignedId, targets);
                    }
                }
                LibdexmoClientController.ConnectionStatusChanged -= ControllerStatusChangedHandler;
                LibdexmoClientController.Dispose();
            }
            if (_handControllers != null)
            {
                int n = _handControllers.Count;
                for (int i = 0; i < n; i++)
                {
                    _handControllers[i].Dispose();
                }
            }
        }

        /// <summary>
        /// Use keyboard to control hand models if enabled. This is done by generating
        /// fake Frame.
        /// </summary>
        /// <param name="fakeHand"></param>
        /// 
        //public float[] fingerBendAngles = { 0, 0, 0, 0, 0 };

        /// <summary>
        /// Get the latest Frame maintained by libdexmo client and update all hand controllers
        /// with the corresponding hand data from the Frame.
        /// </summary>
        private void UpdateHandMotion()
        {
            // Get the latest Frame received from libdexmo server.
            Frame newFrame = LibdexmoClientController.GetFrame();
            if (newFrame == null)
            {
                Debug.Log("No new frame.");
                return;
            }
            _connectedHandIdsTemp.Clear();
            // latestHands is a dictionary:
            // Key: ID of the Dexmo device that libdexmo server decides. During the lifetime of the server,
            // each Dexmo device will be assigned a unique id.
            // Value: Latest hand data of the Dexmo device with the corresponding ID.
            Dictionary<int, Hand> latestHands = LibdexmoClientController.LatestHands;
            // connectedDexmoInfos is a dictionary:
            // Key: ID of the Dexmo device that libdexmo server decides.
            // Value: The detailed information of dexmo device with the corresponding ID. This includes
            // the Dexmo device's physical ID that uniquely identifies the device in the world.
            Dictionary<int, DexmoInfo> connectedDexmoInfos = LibdexmoClientController.ConnectedDexmoInfos;

            // Extract all IDs corresponding to the connected Dexmo devices.
            foreach (int id in latestHands.Keys)
            {
                Hand hand = latestHands[id];
                if (hand != null && connectedDexmoInfos[id].Connected)
                {
                    _connectedHandIdsTemp.Add(id);
                }
            }
            if (_connectedHandIdsTemp.Count == 0)
            {
                Debug.Log("No dexmo is connected at the moment");
            }

            // Update each hand controller with the latest hand mocap data
            foreach (UnityHandController handController in _handControllers)
            {
                Hand handData = null;
                int matchedId = 0;
                // Hand controller is only active when it receives hand data of the Dexmo
                // device that it binds to.
                if (handController.Active)
                {
                    int id = handController.DeviceInfo.AssignedId;
                    // Check if the Dexmo device that hand controller previously matches is
                    // still connected. Although ID is unique during the lifetime of the
                    // server, when server restarts, the id for the same Dexmo device may change.
                    // Hence, we need to check if the physical ID matches as well.
                    if (_connectedHandIdsTemp.Contains(id) &&
                        handController.DeviceInfo.Equals(connectedDexmoInfos[id]))
                    {
                        // Both ID and physical ID matches, so Dexmo device that hand controller
                        // previously binds is still connected.
                        handData = latestHands[id];
                        _connectedHandIdsTemp.Remove(id);
                        matchedId = id;
                    }
                }
                // Hand controller is not active or the dexmo device that hand controller previously
                // binds is no longer connected. Need to find a new Dexmo device for this hand controller.
                if (handData == null)
                {
                    // Find new dexmo hand that matches the hand controller's left/right property.
                    if (MatchDeviceHand(handController.IsRight,
                        _connectedHandIdsTemp, latestHands, out matchedId, out handData))
                    {
                        // Matched hand found. The corresponding hand data is stored in handData, which
                        // will later be used to update this hand controller. Remove the matchedId from
                        // the pool of unassigned handIDs.
                        _connectedHandIdsTemp.Remove(matchedId);
                    }
                }
                if (handData == null)
                {
                    // No matched Dexmo device is found. Set hand controller to be inactive.
                    handController.Active = false;
                }
                else
                {
                    // Matched Dexmo device is found. Set hand controller to be active and
                    // update the hand controller with handData and DeviceInfo.
                    handController.Active = true;
                    RawHandDataEventArgs args = new RawHandDataEventArgs(handData);
                    Miscellaneous.InvokeEvent(UpdateRawHandDataEvent, this, args);
                    handController.FixedUpdate(handData);
                    handController.DeviceInfo = connectedDexmoInfos[matchedId];
                }
            }
        }

        //private Hand _yourVariable;

        //private void SubScribe()
        //{
        //    DexmoController.Instance.UpdateRawHandDataEvent += UpdateHandRotation;
        //}

        //private void UpdateHandRotation(object obj, RawHandDataEventArgs args)
        //{
        //    Hand handData = args.HandData;
        //    // Change according to your need
        //    if (!handData.Right)
        //    {
        //        _yourVariable = args.HandData;
        //    }
        //    float bend = _yourVariable.Fingers[(int)FingerType.Middle].Joints[(int)JointType.MCP].RotationNormalized[2];
        //}

        /// <summary>
        /// Find a currently connected Dexmo device with left/right property
        /// </summary>
        /// <param name="isRight">Left or Right Dexmo device to match</param>
        /// <param name="connectedIdSet">A set of ID assigned to the currently connected 
        /// Dexmo devices</param>
        /// <param name="latestHands">Dictionary of hand data corresponding to the IDs</param>
        /// <param name="matchedId">ID of the Dexmo device matched</param>
        /// <param name="handData">Hand mocap data of the Dexmo device matched</param>
        /// <returns>True if a connected Dexmo device is matched.</returns>
        private bool MatchDeviceHand(bool isRight, HashSet<int> connectedIdSet,
            Dictionary<int, Hand> latestHands, out int matchedId, out Hand handData)
        {
            handData = null;
            matchedId = 0;
            bool matchFound = false;
            foreach (int id in connectedIdSet)
            {
                Hand hand = latestHands[id];
                if (isRight == hand.Right)
                {
                    // Matched hand found
                    matchFound = true;
                    matchedId = id;
                    handData = hand;
                    break;
                }
            }
            return matchFound;
        }

        /// <summary>
        /// Update hand controllers with hand mocap data.
        /// </summary>
        /// 

        private float[] firstAngles = { 0, 0, 0, 0, 0, 0 };
        public Hand setlatestHandData(float[] fingerBendAngles)
        {
            Hand fakeHand = new Hand();
            Finger[] fakeFingerData = new Finger[5];
            Joint fakeJointMCPThumb;
            Joint fakeJointPIPThumb;
            Joint fakeJointDIPThumb;

            fakeJointMCPThumb = new Joint(JointType.MCP, 0, fingerBendAngles[5], fingerBendAngles[0]);
            fakeJointPIPThumb = new Joint(JointType.PIP, 0, 0, fingerBendAngles[0]);
            fakeJointDIPThumb = new Joint(JointType.DIP, 0, 0, fingerBendAngles[0]);
            Joint[] fakeJointsThumb = new Joint[3] { fakeJointMCPThumb, fakeJointPIPThumb, fakeJointDIPThumb };
            fakeFingerData[0] = new Finger(FingerType.Thumb, fakeJointsThumb, null);

            Joint[] fakeJointsIndex = new Joint[3];
            fakeJointsIndex[0] = new Joint(JointType.MCP, 0, 0, fingerBendAngles[1]);
            fakeJointsIndex[1] = new Joint(JointType.PIP, 0, 0, fingerBendAngles[1]);
            fakeJointsIndex[2] = new Joint(JointType.DIP, 0, 0, fingerBendAngles[1]);

            fakeFingerData[1] = new Finger(FingerType.Index, fakeJointsIndex, null);


            Joint[] fakeJointsMiddle = new Joint[3];
            fakeJointsMiddle[0] = new Joint(JointType.MCP, 0, 0, fingerBendAngles[2]);
            fakeJointsMiddle[1] = new Joint(JointType.PIP, 0, 0, fingerBendAngles[2]);
            fakeJointsMiddle[2] = new Joint(JointType.DIP, 0, 0, fingerBendAngles[2]);
            fakeFingerData[2] = new Finger(FingerType.Middle, fakeJointsMiddle, null);

            Joint[] fakeJointsRing = new Joint[3];
            fakeJointsRing[0] = new Joint(JointType.MCP, 0, 0, fingerBendAngles[3]);
            fakeJointsRing[1] = new Joint(JointType.PIP, 0, 0, fingerBendAngles[3]);
            fakeJointsRing[2] = new Joint(JointType.DIP, 0, 0, fingerBendAngles[3]);
            fakeFingerData[3] = new Finger(FingerType.Ring, fakeJointsRing, null);

            Joint[] fakeJointsPinky = new Joint[3];
            fakeJointsPinky[0] = new Joint(JointType.MCP, 0, 0, fingerBendAngles[4]);
            fakeJointsPinky[1] = new Joint(JointType.PIP, 0, 0, fingerBendAngles[4]);
            fakeJointsPinky[2] = new Joint(JointType.DIP, 0, 0, fingerBendAngles[4]);
            fakeFingerData[4] = new Finger(FingerType.Pinky, fakeJointsPinky, null);
            SensorReadings fakeSensorReadings = new SensorReadings(null, null, null, null);
            fakeHand = new Hand(123, _keyboardControlRight, fakeFingerData, fakeSensorReadings); return fakeHand;
        }

        private bool objectTouchedFixed = false;
        public void setObjectTouched(bool update)
        {
            ObjectTouchedFixed = update;
        }
        public bool getObjectTouched()
        {
            return ObjectTouchedFixed;
        }

        public float returnBendAngle(UnityHandController hand, int finger, int joint)
        {
            float value = hand.GetCurrentFingerRotationInfo().Fingers[finger].Split.Value;
            return value;
        }

        private float returnModelBendAngle(Hand hand, int finger)
        {
            return hand.Fingers[finger].Joints[0].RotationNormalized[0];
        }


        public void testFunction()
        {
            FixedUpdate();
        }
        void FixedUpdate()
        {
            if (_keyboardControlTest)
            {
                float[] currentAngles = agent.GetComponent<HandAgent>().getAngleValue();
                UnityHandController hc = _keyboardControlRight ? _handControllerPairs[0].Right : _handControllerPairs[0].Left;
                Hand currentHand = setlatestHandData(currentAngles);
                hc.FixedUpdate(currentHand);
            }
            else
            {

                if (LibdexmoClientController == null)
                {
                    return;
                }
                if (!LibdexmoClientController.Connected)
                {
                    print("LibdexmoClientController not connected.");
                    return;
                }
                UpdateHandMotion();
            }

        }

        /// <summary>
        /// Little utility to display FPS on the top left hand corner of the screen.
        /// </summary>
        //void OnGUI()
        //{
        //    GUI.Label(new Rect(0, 0, 200, 100), "Frame rate: " + (1.0f / Time.deltaTime));
        //}

        /// <summary>
        /// Mainly used to process things after the update of physics cycles, such as
        /// manage hand model collisions with virtual objects.
        /// </summary>
        void LateUpdate()
        {
            foreach (UnityHandController handController in _handControllers)
            {
                if (_keyboardControlTest)
                {
                    handController.Update();
                }
                else
                {
                    if (handController.Active)
                    {
                        handController.Update();
                    }
                }
            }
        }

        /// <summary>
        /// Handles some keyboard commands used for debug purposes.
        /// </summary>


        /// <summary>
        /// Display rotation information of joints of all active hand models.
        /// </summary>
        public void ShowHandsRotationInfo()
        {
            StringBuilder ss = new StringBuilder();
            foreach (UnityHandController handController in _handControllers)
            {
                ss.Append(handController.IsRight ? "Right hand, " : "Left hand, ");
                ss.AppendLine();
                IHandRotationNormalized handRotationNormalized =
                    handController.GetCurrentFingerRotationInfo();
                string rotationInfo = handRotationNormalized.ToString();
                ss.Append(rotationInfo);
                ss.AppendLine();
            }
            string handsRotationInfo = ss.ToString();
            Debug.Log(handsRotationInfo);
        }

        #region LibdexmoClientController impedance control wrapper

        /// <summary>
        /// Issue command to release all fingers from any force feedback.
        /// </summary>
        /// <param name="assignedId">ID of the Dexmo device to be controlled.</param>
        public void ImpedanceControlStopAllFingers(int assignedId)
        {
            bool[] targets = new bool[5] { true, true, true, true, true };
            ImpedanceControlStopFingers(assignedId, targets);
        }

        /// <summary>
        /// Issue command to release some fingers from any force feedback.
        /// </summary>
        /// <param name="assignedId">ID of the Dexmo device to be controlled</param>
        /// <param name="fingerTargets">Which fingers to control. Array of bool of
        /// size 5. Index 0, 1, 2, 3, 4 corresponds to thumb, index, middle, ring
        /// and pinky respectively.</param>
        public void ImpedanceControlStopFingers(int assignedId, bool[] fingerTargets)
        {
            if (LibdexmoClientController == null)
            {
                return;
            }
            if (LibdexmoClientController.Connected)
            {
                LibdexmoClientController.StopImpedanceControlFingers(assignedId, fingerTargets);
            }
            else
            {
                throw new ArgumentNullException();
            }
        }

        /// <summary>
        /// Issue the most general force feedback command to one finger.
        /// </summary>
        /// <param name="assignedId">ID of the the Dexmo device to be controlled.</param>
        /// <param name="fingerType">Type of the finger.</param>
        /// <param name="stiffness">Stiffness of the force feedback, with 0 being release command</param>
        /// <param name="positionSetpoint">Position setpoint from 0 to 1, related to the angle
        /// that finger starts to feel any force.</param>
        /// <param name="isInwardControl">Direction of the force. If set to true, fingers will feel
        /// force when they bend inwards (think of grasping).</param>
        public void ImpedanceControlFinger(int assignedId, FingerType fingerType,
            float stiffness, float positionSetpoint, bool isInwardControl)
        {
            if (LibdexmoClientController == null)
            {
                return;
            }
            if (LibdexmoClientController.Connected)
            {
                LibdexmoClientController.ImpedanceControlOneFinger(assignedId, fingerType, stiffness, positionSetpoint,
                    isInwardControl);
            }
        }

        /// <summary>
        /// Issue the most general feedback command to any fingers.
        /// </summary>
        /// <param name="assignedId">ID of the the Dexmo device to be controlled.</param>
        /// <param name="fingerTargets">Which fingers to control. Array of bool of
        /// size 5. Index 0, 1, 2, 3, 4 corresponds to thumb, index, middle, ring
        /// and pinky respectively.</param>
        /// <param name="stiffness">Stiffness of the force feedback, with 0 being release command</param>
        /// <param name="positionSetpoint">Position setpoint from 0 to 1, related to the angle
        /// that finger starts to feel any force.</param>
        /// <param name="isInwardControl">Direction of the force. If set to true, fingers will feel
        /// force when they bend inwards (think of grasping).</param>
        public void ImpedanceControlFingers(int assignedId, bool[] fingerTargets, float[] stiffness,
                                            float[] positionSetpoints, bool[] isInwardControl)
        {
            if (LibdexmoClientController == null)
            {
                return;
            }
            if (LibdexmoClientController.Connected)
            {
                LibdexmoClientController.ImpedanceControlFingers(assignedId, fingerTargets, stiffness, positionSetpoints,
                    isInwardControl);
            }
        }

        /// <summary>
        /// Wrapper function to issue the most general impedance control command to any fingers.
        /// </summary>
        /// <param name="assignedId">ID of the the Dexmo device to be controlled.</param>
        /// <param name="fingerControlInfoList">All the necessary information of the force feedback command.</param>
        public void ImpedanceControlFingers(int assignedId, List<ImpedanceControlFingerInfo> fingerControlInfoList)
        {
            bool[] fingerTargets = new bool[5] { false, false, false, false, false };
            float[] stiffness = new float[5];
            float[] positionSetpoints = new float[5];
            bool[] inwardControl = new bool[5];
            int n = fingerControlInfoList.Count;
            for (int i = 0; i < n; i++)
            {
                ImpedanceControlFingerInfo fingerControlInfo = fingerControlInfoList[i];
                int fingerIndex = (int)fingerControlInfo.FingerType;
                fingerTargets[fingerIndex] = true;
                stiffness[fingerIndex] = fingerControlInfo.Stiffness;
                positionSetpoints[fingerIndex] = fingerControlInfo.PositionSetpoint;
                inwardControl[fingerIndex] = fingerControlInfo.InwardControl;
                if (stiffness[fingerIndex] < float.Epsilon)
                {
                    //Debug.LogWarningFormat("Release finger {0}", fingerIndex);
                }
                else
                {
                    //Debug.LogWarningFormat("Imp Control to finger: {0}, stiffness {1:F2}, setpoint {2}",
                    //    fingerIndex, stiffness[fingerIndex], positionSetpoints[fingerIndex]);
                }
            }
            ImpedanceControlFingers(assignedId, fingerTargets, stiffness, positionSetpoints,
                inwardControl);
        }

        #endregion

        #region IDexmoController Methods

        /// <summary>
        /// Utility function to start a generic coroutine. Used by other non-MonoBehaviour classes
        /// inside DexmoController.
        /// </summary>
        /// <typeparam name="T">Type of the yield instruction</typeparam>
        /// <param name="action">Function to execute after the yield.</param>
        public void StartActionCoroutine<T>(Action action) where T : YieldInstruction, new()
        {
            StartCoroutine(ActionCoroutine<T>(action));
        }

        /// <summary>
        /// The actual coroutine used.
        /// </summary>
        /// <typeparam name="T">Type of the yield instruction</typeparam>
        /// <param name="action">Function to execute after the yield.</param>
        /// <returns></returns>
        private IEnumerator ActionCoroutine<T>(Action action) where T : YieldInstruction, new()
        {
            yield return new T();
            action();
        }

        #endregion
        /// <summary>
        /// Clean up GameObjects that hand models are interacting if those GameObjects are
        /// destroyed unexpectedly. This is to handle the worst case. Usually don't need to use.
        /// </summary>
        public void CleanUpDestroyedObjects()
        {
            foreach (UnityHandController handController in _handControllers)
            {
                if (handController.Active)
                {
                    handController.CleanUpDestroyedObjects();
                }
            }
        }
    }
}
