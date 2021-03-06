﻿using System;
using System.Collections.Generic;
using Leap;

namespace Coloreality.LeapWrapper.Sender
{
    public class LeapReader : Controller
    {
        /// <summary>
        /// First instance.
        /// </summary>
        public static LeapReader Default = null;

        public event SerializationReadyEventHandler OnSerializationReady;

        private LeapData _data = new LeapData();
        public LeapData Data {
            get { return _data; }
            set { _data = value; }
        }

        private int _sendNoHandMaxCount = 5;
        /// <summary>
        /// The maximined times to send frame data continuously with NO HAND but only frame Id, when When Leap device does not detect any hands.
        /// Set -1 to not limit the count and keep sending.
        /// Notice when setting this small on a high {sendInterval} value connection, it could be skipped and not sending any no-hand frames.
        /// </summary>
        public int SendNoHandMaxCount
        {
            get { return _sendNoHandMaxCount; }
            set { _sendNoHandMaxCount = value; }
        }

        private bool readBones = false;
        public bool ReadBones
        {
            get { return readBones; }
            set { readBones = value; }
        }

        private bool readArm = true;
        public bool ReadArm
        {
            get { return readArm; }
            set { readArm = value; }
        }

        int sentNoHandTimes = 0;

        public int limitedHandCount = -1;
        
        private const int FINGER_COUNT = 5;
        private const int BONE_COUNT = 4;
        public LeapReader(bool hmdOptimized = true)
        {
            SetPolicy(PolicyFlag.POLICY_BACKGROUND_FRAMES);
            SetPolicy(hmdOptimized ? PolicyFlag.POLICY_OPTIMIZE_HMD : PolicyFlag.POLICY_DEFAULT);
            if (hmdOptimized)
            {
                limitedHandCount = 2;
            }
            FrameReady += DataReady;

            if (Default != null)
            {
                Default = this;
            }
        }

        private void DataReady(object sender, FrameEventArgs e)
        {
            if (OnSerializationReady == null) return;

            Frame frame = e.frame;
            if (frame == null)
            {
                //OnSerializationReady.Invoke(this, new SerializationEventArgs(LeapSerialization.DATA_INDEX));
                return;
            }

            LeapFrame newFrame = new LeapFrame()
            {
                Id = frame.Id
            };

            List<Hand> hands = frame.Hands;
            
            int handCount = limitedHandCount == -1 ? hands.Count : Math.Min(limitedHandCount, hands.Count);
            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                Hand hand = hands[handIndex];

                List<Finger> fingers = hand.Fingers;
                List<LeapFinger> leapFingers = new List<LeapFinger>();
                for (int fingerIndex = 0; fingerIndex < FINGER_COUNT; fingerIndex++)
                {
                    Finger finger = fingers[fingerIndex];
                    LeapFinger leapFinger = new LeapFinger()
                    {
                        Id = finger.Id,
                        IsExtended = fingers[fingerIndex].IsExtended,
                        TimeVisible = fingers[fingerIndex].TimeVisible,
                        Width = fingers[fingerIndex].Width,
                        Length = fingers[fingerIndex].Length,
                        TipPosition = fingers[fingerIndex].TipPosition.ToSerialiableVector(),
                        Direction = fingers[fingerIndex].Direction.ToSerialiableVector(),
                        TipVelocity = fingers[fingerIndex].TipVelocity.ToSerialiableVector()
                        //StabilizedTipVelocity = fingers[fingerIndex].ToSerialiableVector()
                    };

                    if (readBones)
                    {
                        Bone[] bones = fingers[fingerIndex].bones;
                        LeapBone[] leapBones = new LeapBone[BONE_COUNT];
                        for (int boneIndex = 0; boneIndex < BONE_COUNT; boneIndex++)
                        {
                            leapBones[boneIndex] = new LeapBone()
                            {
                                Length = bones[boneIndex].Length,
                                Width = bones[boneIndex].Width,
                                Center = bones[boneIndex].Center.ToSerialiableVector(),
                                Direction = bones[boneIndex].Direction.ToSerialiableVector(),
                                Rotation = bones[boneIndex].Rotation.ToSerialiableQuaternion()
                            };
                        }
                        leapFinger.bones = leapBones;
                    }

                    leapFingers.Add(leapFinger);
                }
                
                LeapHand leapHand = new LeapHand()
                {
                    Id = hand.Id,
                    IsLeft = hand.IsLeft,
                    Confidence = hand.Confidence,
                    TimeVisible = hand.TimeVisible,
                    GrabStrength = hand.GrabStrength,
                    GrabAngle = hand.GrabAngle,
                    PinchStrength = hand.PinchStrength,
                    PinchDistance = hand.PinchDistance,
                    PalmWidth = hand.PalmWidth,
                    PalmPosition = hand.PalmPosition.ToSerialiableVector(),
                    PalmVelocity = hand.PalmVelocity.ToSerialiableVector(),
                    Direction = hand.Direction.ToSerialiableVector(),
                    PalmNormal = hand.PalmNormal.ToSerialiableVector(),
                    Rotation = hand.Rotation.ToSerialiableQuaternion(),
                    WristPosition = hand.WristPosition.ToSerialiableVector(),
                    //StabilizedPalmPosition = hand.StabilizedPalmPosition.ToSerialiableVector(),

                    Fingers = leapFingers,
                };

                if (readArm)
                {
                    Arm arm = hand.Arm;
                    LeapArm leapArm = new LeapArm()
                    {
                        Length = arm.Length,
                        Width = arm.Width,
                        Elbow = arm.ElbowPosition.ToSerialiableVector(),
                        Wrist = arm.WristPosition.ToSerialiableVector(),
                        Center = hand.Arm.Center.ToSerialiableVector(),
                        Direction = hand.Arm.Direction.ToSerialiableVector(),
                        Rotation = hand.Arm.Rotation.ToSerialiableQuaternion()
                    };

                    leapHand.Arm = leapArm;
                }

                newFrame.Hands.Add(leapHand);
            }
            
            _data.frame = newFrame;

            if (hands.Count > 0)
            {
                OnSerializationReady.Invoke(this, new SerializationEventArgs(LeapData.DATA_INDEX, SerializationUtil.Serialize(_data), true));
                sentNoHandTimes = 0;
            }
            else if (SendNoHandMaxCount == -1 || ++sentNoHandTimes <= SendNoHandMaxCount)
            {
                OnSerializationReady.Invoke(this, new SerializationEventArgs(LeapData.DATA_INDEX, SerializationUtil.Serialize(_data), true));
            }
        }

    }
}
