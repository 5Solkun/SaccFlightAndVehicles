﻿
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace SaccFlightAndVehicles
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class SaccRacingTrigger : UdonSharpBehaviour
    {
        public string PlaneName;
        [Tooltip("Set automatically on build if left empty")]
        public SaccRaceToggleButton RaceToggler;
        public GameObject[] DisabledRaces;
        public GameObject[] InstanceRecordDisallowedRaces;
        public TextMeshProUGUI Racename_text;
        public TextMeshProUGUI Time_text;
        public TextMeshProUGUI SplitTime_text;
        [System.NonSerialized, FieldChangeCallback(nameof(TrackForward))] public bool _TrackForward = true;
        public bool TrackForward
        {
            set
            {
                _TrackForward = value;
                // SetUpNewRace();
            }
            get => _TrackForward;
        }
        [System.NonSerialized] public float[] CurrentSplits;
        private bool CurrentTrackAllowReverse;
        private SaccRaceCourseAndScoreboard CurrentCourse;
        private int CurrentCourseSelection = -1;
        private int NextCheckpoint;
        private bool InProgress;
        private int TrackDirection = 1;
        private int FirstCheckPoint = 1;
        private int FinalCheckpoint;
        private bool RaceOn;
        private float RaceStartTime = 0;
        private float RaceTime;
        private Animator CurrentCheckPointAnimator;
        private Animator NextCheckPointAnimator;
        private VRCPlayerApi localPlayer;
        private bool InEditor = false;
        private float LastTime;
        private LayerMask ThisObjLayer;
        private float LastFrameCheckpointDist;
        private CapsuleCollider ThisCapsuleCollider;
        private Rigidbody PlaneRigidbody;
        private Vector3 PlaneClosestPos;
        private Vector3 PlaneClosestPosLastFrame;
        private float PlaneDistanceFromCheckPoint;
        private float PlaneDistanceFromCheckPointLastFrame;
        private Vector3 PlaneClosestPosInverseFromPlane;
        private float RaceFinishTime;
        private bool LoopFinalSplit = false;
        private bool Initialized = false;
        private void Initialize()
        {
            Initialized = true;
            // _TrackForward = true;//Why is this needed?
            GameObject Objs = gameObject;
            //checking if a rigidbody is null in a while loop doesnt work in udon for some reason, use official vrchat workaround
            while (!Utilities.IsValid(PlaneRigidbody) && Objs.transform.parent)
            {
                Objs = Objs.transform.parent.gameObject;
                PlaneRigidbody = Objs.GetComponent<Rigidbody>();
            }
            ThisCapsuleCollider = gameObject.GetComponent<CapsuleCollider>();
            ThisObjLayer = 1 << gameObject.layer;
            localPlayer = Networking.LocalPlayer;
            SetSplitEmpty();
            if (localPlayer == null) { InEditor = true; }
        }
        public void SetTimerEmpty()
        { if (Time_text) { Time_text.text = string.Empty; } }
        public void SetSplitEmpty()
        { if (Time_text) { SplitTime_text.text = string.Empty; } }
        void Start()
        { SetTimerEmpty(); }
        private string SecsToMinsSec(float Seconds)
        {
            float mins = Mathf.Floor(Seconds / 60f);
            float secs = Seconds % 60f;
            if (mins > 0)
            {
                return mins.ToString("F0") + ":" + secs.ToString("F3");//There's probably a better way to format this but idk
            }
            return secs.ToString("F3");
        }
        private void Update()
        {
            if (RaceOn)
            {
                RaceTime = Time.realtimeSinceStartup - RaceStartTime;
                if (Time_text) { Time_text.text = SecsToMinsSec(RaceTime).PadLeft(6, '0'); }
            }
        }
        private int CurrentSplit = 0;
        private void UpdateSplitTime()
        {
            CurrentSplits[CurrentSplit] = RaceTime + CalcSubFrameTime();
            float splitdif;
            if (TrackForward)
            {
                splitdif = CurrentSplits[CurrentSplit] - CurrentCourse.SplitTimes[CurrentSplit];
                bool faster = splitdif < 0;
                bool firstrun = CurrentCourse.SplitTimes[CurrentSplit] == 0;
                if (SplitTime_text) { SplitTime_text.text = (!firstrun ? (faster ? "<color=green>" : "<color=red>+") + SecsToMinsSec(splitdif) : string.Empty) + "\n<color=white>" + CurrentSplits[CurrentSplit].ToString("F3").PadLeft(6, '0'); }
            }
            else
            {
                splitdif = CurrentSplits[CurrentSplit] - CurrentCourse.SplitTimes_R[CurrentSplit];
                bool faster = splitdif < 0;
                bool firstrun = CurrentCourse.SplitTimes_R[CurrentSplit] == 0;
                if (SplitTime_text) { SplitTime_text.text = (!firstrun ? (faster ? "<color=green>" : "<color=red>+") + SecsToMinsSec(splitdif) : string.Empty) + "\n<color=white>" + CurrentSplits[CurrentSplit].ToString("F3").PadLeft(6, '0'); }
            }
            CurrentSplit++;
            SendCustomEventDelayedSeconds(nameof(SetSplitEmpty), 3);
        }
        private void FixedUpdate()
        {
            Collider checkpoint = CurrentCheckPointAnimator.gameObject.GetComponent<Collider>();
            //save values from last frame, for comparing distance
            PlaneDistanceFromCheckPointLastFrame = PlaneDistanceFromCheckPoint;
            PlaneClosestPosLastFrame = PlaneClosestPos;
            //getclosest point on checkpoint trigger collider to plane
            //find closest point on plane to that^
            PlaneClosestPos = ThisCapsuleCollider.ClosestPoint(checkpoint.ClosestPoint(transform.position));

            //Raycast from plane in direction of movement of the closest point on the plane to the checkpoint, which will hit the checkpoint if moving toward it.
            PlaneClosestPosInverseFromPlane = transform.InverseTransformPoint(PlaneClosestPos);
            RaycastHit hit;
            ThisCapsuleCollider.enabled = false;//somehow this raycast can hit itself if we don't
            if (Physics.Raycast(PlaneClosestPos, PlaneRigidbody.GetPointVelocity(PlaneClosestPos), out hit, 50, ThisObjLayer, QueryTriggerInteraction.Collide))
            {
                PlaneDistanceFromCheckPoint = hit.distance;
                //Debug.Log("racetrigger hit");
                //Debug.Log(string.Concat("dist: ", hit.distance));
            }
            /*             else
                        {
                            Debug.Log("racetrigger miss");
                        } */
            ThisCapsuleCollider.enabled = true;
        }
        void ReportTime()
        {
            if (TrackForward)
            { CurrentCourse.SplitTimes = CurrentSplits; }
            else
            { CurrentCourse.SplitTimes_R = CurrentSplits; }
            RaceTime = 0;
            //Debug.Log("Finish Race!");
            RaceFinishTime = Time.realtimeSinceStartup;
            RaceTime = LastTime = (RaceFinishTime - RaceStartTime - CalcSubFrameTime());
            CurrentCourse.TimeReporter.MyLastRace_Reverse = !TrackForward;
            if (TrackForward || !CurrentTrackAllowReverse)//track was finished forward
            {
                //if i am not on board, AND my time is better than the worst time on board OR
                //if i am on board, and my time is better than my previous time
                //add submit my time
                CurrentCourse.TimeReporter.MyLastTime = RaceTime;
                CurrentCourse.TimeReporter.MyLastVehicle = PlaneName;
                CurrentCourse.UpdateMyLastTime();
                int mypos = CurrentCourse.CheckIfOnBoard(localPlayer.displayName, ref CurrentCourse.PlayerNames);
                if (mypos < 0)//i am not on the board
                {
                    if (CurrentCourse.PlayerTimes.Length > 0)
                    {
                        if (RaceTime < CurrentCourse.PlayerTimes[CurrentCourse.PlayerTimes.Length - 1] || CurrentCourse.PlayerTimes.Length < CurrentCourse.MaxRecordedTimes)
                        { SendMyTime(false); }
                    }
                    else
                    { SendMyTime(false); }
                }
                else//i am on the board
                {
                    if (RaceTime < CurrentCourse.PlayerTimes[mypos])
                    { SendMyTime(false); }
                }
            }
            else//track was finished backward
            {
                CurrentCourse.TimeReporter.MyLastTime_R = RaceTime;
                CurrentCourse.TimeReporter.MyLastVehicle_R = PlaneName;
                CurrentCourse.UpdateMyLastTime();
                int mypos = CurrentCourse.CheckIfOnBoard(localPlayer.displayName, ref CurrentCourse.PlayerNames_R);
                if (mypos < 0)//i am not on the board
                {
                    if (CurrentCourse.PlayerTimes_R.Length > 0)
                    {
                        if (RaceTime < CurrentCourse.PlayerTimes[CurrentCourse.PlayerTimes.Length - 1] || CurrentCourse.PlayerTimes.Length < CurrentCourse.MaxRecordedTimes)
                        { SendMyTime(true); }
                    }
                    else
                    { SendMyTime(true); }
                }
                else//i am on the board
                {
                    if (RaceTime < CurrentCourse.PlayerTimes_R[mypos])
                    { SendMyTime(true); }
                }
            }

            RaceTime = 0;
            if (TrackForward && !CurrentCourse.LoopRace)
            {
                string TimeImproved = LastTime < CurrentCourse.MyBestTime ? "<color=green>" : "<color=red>";
                if (Time_text) { Time_text.text = TimeImproved + LastTime.ToString("F3"); }
            }
            else
            {
                string TimeImproved = LastTime < CurrentCourse.MyBestTime_R ? "<color=green>" : "<color=red>";
                if (Time_text) { Time_text.text = TimeImproved + LastTime.ToString("F3"); }
            }
            SendCustomEventDelayedSeconds(nameof(SetTimerEmpty), 3);
        }
        void OnTriggerEnter(Collider other)
        {
            if (Time.realtimeSinceStartup - RaceFinishTime < 1f) { return; }
            if (CurrentCourseSelection != -1 && (other && other.gameObject == CurrentCourse.RaceCheckpoints[NextCheckpoint]))
            {
                if (NextCheckpoint == FinalCheckpoint)//end of the race
                {
                    NextCheckpoint = FirstCheckPoint;
                    if (Utilities.IsValid(CurrentCheckPointAnimator))
                    { CurrentCheckPointAnimator.SetBool("Current", false); }
                    StartCheckPointAnims();
                    if (CurrentCourse.LoopRace)
                    {
                        LoopFinalSplit = true;
                        UpdateSplitTime();
                    }
                    else
                    {
                        CurrentSplit = 0;
                        if (InProgress)
                        {
                            RaceToggler.RacesInProgress--;
                            InProgress = false;
                            CurrentCourse.RaceInProgress = false;
                        }
                        RaceOn = false;
                        ReportTime();
                    }
                }
                else if (NextCheckpoint == FirstCheckPoint)//starting the race
                {
                    if (LoopFinalSplit)
                    {
                        UpdateSplitTime();
                        ReportTime();
                        LoopFinalSplit = false;
                        CurrentSplit = 0;
                    }
                    CurrentSplits = new float[CurrentCourse.RaceCheckpoints.Length];
                    //subframe accuracy is done on the first and last checkpoint, the code for the last checkpoint(above) is commented
                    RaceTime = 0;

                    //Debug.Log("Start Race!");
                    RaceStartTime = Time.realtimeSinceStartup - CalcSubFrameTime();
                    RaceOn = true;
                    if (!InProgress)
                    {
                        RaceToggler.RacesInProgress++;
                        InProgress = true;
                        CurrentCourse.RaceInProgress = true;
                    }
                    if (CurrentCourse.LoopRace && !TrackForward && CurrentCourse.RaceCheckpoints[NextCheckpoint] == CurrentCourse.RaceCheckpoints[0]) { NextCheckpoint = CurrentCourse.RaceCheckpoints.Length; }
                    NextCheckpoint += TrackDirection;
                    if (CurrentCourse.LoopRace && !TrackForward) { TrackDirection = -1; }
                    ProgressCheckPointAnims();
                }
                else
                {
                    //Debug.Log("CheckPoint!");
                    NextCheckpoint += TrackDirection;
                    ProgressCheckPointAnims();
                    UpdateSplitTime();
                    //check if the next checkpoint is the end of the race, because if it is we need to get subframe time
                }
            }
        }
        private float CalcSubFrameTime()
        {
            float subframetime = 0;
            //get world space position of the point that was closest to the checkpoint the frame before the race was finished
            Vector3 LastFrameClosestPosThisFrame = transform.TransformPoint(PlaneClosestPosInverseFromPlane);
            //get the speed of the plane by comparing the two
            float speed = Vector3.Distance(PlaneClosestPosLastFrame, LastFrameClosestPosThisFrame);
            //check if the plane is travelling further per frame than the distance to the checkpoint from the last frame, just incase the raycast somehow missed
            //(only do subframe time if it'll be valid)
            if (speed > PlaneDistanceFromCheckPointLastFrame)
            {
                float passedratio = PlaneDistanceFromCheckPointLastFrame / speed;
                subframetime = -Time.fixedDeltaTime * passedratio + Time.fixedDeltaTime;
            }
            return subframetime;
        }
        private void SendMyTime(bool reverse)
        {
            Networking.SetOwner(localPlayer, CurrentCourse.TimeReporter.gameObject);
            CurrentCourse.TimeReporter.ReportedVehicle = PlaneName;
            CurrentCourse.TimeReporter.Reported_RaceReverse = reverse;
            CurrentCourse.TimeReporter.ReportedTime = RaceTime;
            RequestSerialization();
        }
        public void SetCourse()
        {
            TurnOffeCurrentCheckPoints();
            CurrentCourseSelection = RaceToggler.CurrentCourseSelection;
            if (CurrentCourseSelection != -1)
            {
                CurrentCourse = RaceToggler.Races[CurrentCourseSelection].GetComponent<SaccRaceCourseAndScoreboard>();
                CurrentTrackAllowReverse = CurrentCourse.AllowReverse;
                if (TrackForward || !CurrentTrackAllowReverse)
                {
                    NextCheckpoint = FirstCheckPoint = 0;
                    FinalCheckpoint = CurrentCourse.RaceCheckpoints.Length - 1;
                    TrackDirection = 1;
                }
                else
                {
                    if (CurrentCourse.LoopRace)
                    {
                        NextCheckpoint = FirstCheckPoint = 0;
                        FinalCheckpoint = 1;
                        TrackDirection = -1;
                    }
                    else
                    {
                        NextCheckpoint = FirstCheckPoint = CurrentCourse.RaceCheckpoints.Length - 1;
                        FinalCheckpoint = 0;
                        TrackDirection = -1;
                    }
                }
                if (InEditor || gameObject.activeInHierarchy) { StartCheckPointAnims(); }//don't turn on lights when switching course unless in editor for testing, or pressing while in a vehicle
            }
        }
        public void SetUpNewRace()
        {
            if (!Initialized) { Initialize(); }
            RaceOn = false;
            RaceTime = 0;
            LoopFinalSplit = false;
            if (CheckDisallowedRace())
            {
                CurrentCourseSelection = -1;
                return;
            }
            if (CurrentCourseSelection != -1)
            { if (Racename_text) { Racename_text.text = RaceToggler.Races[CurrentCourseSelection].RaceName; } }
            SetCourse();
        }
        void OnEnable()//Happens when you get in a plane, if a race is enabled
        {
            SetUpNewRace();
            StartCheckPointAnims();
            SetTimerEmpty();
        }
        void OnDisable()
        {
            TurnOffeCurrentCheckPoints();
        }
        public void TurnOffeCurrentCheckPoints()
        {
            if (CurrentCourse)
            {
                if (InProgress)
                {
                    RaceToggler.RacesInProgress--;
                    InProgress = false;
                    CurrentCourse.RaceInProgress = false;
                }
                if (Utilities.IsValid(CurrentCheckPointAnimator))
                {
                    CurrentCheckPointAnimator.SetBool("Current", false);
                    CurrentCheckPointAnimator.SetTrigger("Reset");
                }
                if (Utilities.IsValid(NextCheckPointAnimator))
                {
                    NextCheckPointAnimator.SetBool("Next", false);
                    NextCheckPointAnimator.SetTrigger("Reset");
                }
            }
        }
        void ProgressCheckPointAnims()
        {
            if (Utilities.IsValid(CurrentCheckPointAnimator))
            { CurrentCheckPointAnimator.SetBool("Current", false); }

            if (Utilities.IsValid(NextCheckPointAnimator))
            {
                NextCheckPointAnimator.SetBool("Next", false);
                NextCheckPointAnimator.SetBool("Current", true);
            }
            CurrentCheckPointAnimator = NextCheckPointAnimator;
            int next2 = NextCheckpoint + TrackDirection;
            if (next2 > -1 && next2 < CurrentCourse.RaceCheckpoints.Length)
            {
                NextCheckPointAnimator = CurrentCourse.RaceCheckpoints[next2].GetComponent<Animator>();
                if (Utilities.IsValid(NextCheckPointAnimator))
                {
                    NextCheckPointAnimator.SetBool("Reverse", !_TrackForward);
                    NextCheckPointAnimator.SetBool("Next", true);
                }
            }
        }
        void StartCheckPointAnims()
        {
            if (CurrentCourseSelection == -1) { return; }
            if (CurrentCourse)
            {
                if (CurrentCourse.RaceCheckpoints.Length > 0)
                {
                    CurrentCheckPointAnimator = CurrentCourse.RaceCheckpoints[FirstCheckPoint].GetComponent<Animator>();
                    if (Utilities.IsValid(CurrentCheckPointAnimator))
                    {
                        CurrentCheckPointAnimator.SetBool("Reverse", !_TrackForward);
                        CurrentCheckPointAnimator.SetBool("Current", true);
                        CurrentCheckPointAnimator.SetBool("Reverse", !_TrackForward);
                        CurrentCheckPointAnimator.SetBool("Next", false);
                    }
                }
                if (CurrentCourse.RaceCheckpoints.Length > 1)
                {
                    NextCheckPointAnimator = CurrentCourse.RaceCheckpoints[FirstCheckPoint + (FirstCheckPoint + TrackDirection < 0 ? CurrentCourse.RaceCheckpoints.Length - 1 : TrackDirection)].GetComponent<Animator>();
                    if (Utilities.IsValid(NextCheckPointAnimator))
                    {
                        NextCheckPointAnimator.SetBool("Reverse", !_TrackForward);
                        NextCheckPointAnimator.SetBool("Next", true);
                    }
                }
            }
        }
        private bool CheckDisallowedRace()
        {
            int ccs = RaceToggler.CurrentCourseSelection;
            if (ccs == -1) { return true; }
            foreach (GameObject race in DisabledRaces)
            {
                if (race == RaceToggler.Races[ccs].gameObject)
                {
                    return true;
                }
            }
            return false;
        }
        private bool CheckRecordDisallowedRace()
        {
            foreach (GameObject race in InstanceRecordDisallowedRaces)
            {
                if (race == RaceToggler.Races[CurrentCourseSelection].gameObject)
                {
                    return true;
                }
            }
            return false;
        }
    }
}