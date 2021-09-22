
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class DFUNC_Catapult : UdonSharpBehaviour
{
    [SerializeField] private UdonSharpBehaviour SAVControl;
    [Tooltip("Object enabled when function is active (used on MFD)")]
    [SerializeField] private GameObject Dial_Funcon;
    [Tooltip("Oneshot sound played when attaching to catapult")]
    [SerializeField] private AudioSource CatapultLock;
    [Tooltip("Maximum angular difference between vehicle and catapult allowed when attaching")]
    [SerializeField] private float MaxAttachAngle = 15;
    [Tooltip("Layer to check for catapult triggers on")]
    [SerializeField] private int CatapultLayer = 24;
    [Tooltip("Reference to the landing gear function so we can tell it to be disabled when on a catapult")]
    [SerializeField] private UdonSharpBehaviour GearFunc;
    private SaccEntity EntityControl;
    private bool UseLeftTrigger = false;
    private bool TriggerLastFrame;
    private bool Selected;
    private bool OnCatapult;
    private bool Launching = false;
    private bool Piloting = false;
    private Transform VehicleTransform;
    private Transform CatapultTransform;
    private int CatapultDeadTimer;
    private Rigidbody VehicleRigidbody;
    private float InVehicleThrustVolumeFactor;
    private Animator VehicleAnimator;
    private int ONCATAPULT_STRING = Animator.StringToHash("oncatapult");
    private float PlaneCatapultBackDistance;
    private float PlaneCatapultUpDistance;
    private Quaternion PlaneCatapultRotDif;
    private Quaternion CatapultRotLastFrame;
    private Vector3 CatapultPosLastFrame;
    private Animator CatapultAnimator;
    //these bools exist to make sure this script only ever adds/removes 1 from the value in enginecontroller
    private bool DisableTaxiRotation = false;
    private bool DisableGearToggle = false;
    private bool OverrideConstantForce = false;
    private bool InEditor;
    private bool IsOwner;
    public void DFUNC_LeftDial() { UseLeftTrigger = true; }
    public void DFUNC_RightDial() { UseLeftTrigger = false; }
    public void SFEXT_L_EntityStart()
    {
        InEditor = Networking.LocalPlayer == null;
        if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
        EntityControl = (SaccEntity)SAVControl.GetProgramVariable("EntityControl");
        VehicleTransform = EntityControl.transform;
        VehicleRigidbody = EntityControl.GetComponent<Rigidbody>();
        VehicleAnimator = EntityControl.GetComponent<Animator>();
    }
    public void DFUNC_Selected()
    {
        TriggerLastFrame = true;//To prevent function enabling if you hold the trigger when selecting it
        Selected = true;
    }
    public void DFUNC_Deselected()
    {
        Selected = false;
        TriggerLastFrame = false;
    }
    public void SFEXT_O_PilotEnter()
    {
        gameObject.SetActive(true);
        Piloting = true;
        DisableOverrides();
        TriggerLastFrame = false;
    }
    public void SFEXT_O_PilotExit()
    {
        if (!Launching)
        {
            gameObject.SetActive(false);
            Piloting = false;
            if (OnCatapult) { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockOff)); }
        }
        Selected = false;
        TriggerLastFrame = false;
        DisableOverrides();
    }
    public void SFEXT_O_PassengerEnter()
    {
        if (Dial_Funcon) Dial_Funcon.SetActive(OnCatapult);
    }
    public void SFEXT_O_TakeOwnership()
    {
        IsOwner = true;
        if (OnCatapult) { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockOff)); }
    }
    public void SFEXT_O_LoseOwnership()
    {
        IsOwner = false;
        Launching = false;
        OnCatapult = false;
        Piloting = false;
        gameObject.SetActive(false);
        DisableOverrides();
    }
    public void SFEXT_O_Explode()
    {
        OnCatapult = false;
        DisableOverrides();
    }
    public void SFEXT_G_RespawnButton()
    {
        if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
    }
    private void EnableOneFrameToFindAnimator()
    {
        if (!IsOwner)
        {
            gameObject.SetActive(true);
            SendCustomEventDelayedFrames("DisableThisObjNonOnwer", 1);
        }
    }
    private void DisableThisObjNonOnwer()
    {
        if (!IsOwner)
        { gameObject.SetActive(false); }
    }
    private bool FindCatapultAnimator(GameObject other)
    {
        GameObject CatapultObjects = other.gameObject;
        CatapultAnimator = null;
        CatapultAnimator = other.GetComponent<Animator>();
        while (!Utilities.IsValid(CatapultAnimator) && CatapultObjects.transform.parent)
        {
            CatapultObjects = CatapultObjects.transform.parent.gameObject;
            CatapultAnimator = CatapultObjects.GetComponent<Animator>();
        }
        return (Utilities.IsValid(CatapultAnimator));
    }
    private void OnTriggerEnter(Collider other)
    {
        if (Piloting && !EntityControl.dead)
        {
            if (other)
            {
                if (!OnCatapult)
                {
                    if (other.gameObject.layer == CatapultLayer)
                    {
                        if (!FindCatapultAnimator(other.gameObject)) { return; }
                        CatapultTransform = other.transform;
                        //Hit detected, check if the plane is facing in the right direction..
                        if (Vector3.Angle(VehicleTransform.forward, CatapultTransform.transform.forward) < MaxAttachAngle)
                        {
                            CatapultPosLastFrame = CatapultTransform.position;
                            //then lock the plane to the catapult! Works with the catapult in any orientation whatsoever.
                            //match plane rotation to catapult excluding pitch because some planes have shorter front or back wheels
                            VehicleTransform.rotation = Quaternion.Euler(new Vector3(VehicleTransform.rotation.eulerAngles.x, CatapultTransform.rotation.eulerAngles.y, CatapultTransform.rotation.eulerAngles.z));

                            //move the plane to the catapult, excluding the y component (relative to the catapult), so we are 'above' it
                            PlaneCatapultUpDistance = CatapultTransform.transform.InverseTransformDirection(CatapultTransform.position - VehicleTransform.position).y;
                            VehicleTransform.position = CatapultTransform.position;
                            VehicleTransform.position -= CatapultTransform.up * PlaneCatapultUpDistance;

                            //move the plane back so that the catapult is aligned to the catapult detector
                            PlaneCatapultBackDistance = VehicleTransform.InverseTransformDirection(VehicleTransform.position - transform.position).z;
                            VehicleTransform.position += CatapultTransform.forward * PlaneCatapultBackDistance;

                            PlaneCatapultRotDif = CatapultTransform.rotation * Quaternion.Inverse(VehicleTransform.rotation);

                            if (!DisableGearToggle && GearFunc)
                            {
                                GearFunc.SetProgramVariable("DisableGearToggle", (int)GearFunc.GetProgramVariable("DisableGearToggle") + 1);
                                DisableGearToggle = true;
                            }
                            if (!DisableTaxiRotation)
                            {
                                SAVControl.SetProgramVariable("DisableTaxiRotation", (int)SAVControl.GetProgramVariable("DisableTaxiRotation") + 1);
                                DisableTaxiRotation = true;
                            }
                            if (!OverrideConstantForce)
                            {
                                SAVControl.SetProgramVariable("OverrideConstantForce", (int)SAVControl.GetProgramVariable("OverrideConstantForce") + 1);
                                SAVControl.SetProgramVariable("CFRelativeForceOverride", Vector3.zero);
                                SAVControl.SetProgramVariable("CFRelativeTorqueOverride", Vector3.zero);
                                OverrideConstantForce = true;
                            }
                            //use dead to make plane invincible for x frames when entering the catapult to prevent taking G damage from stopping instantly
                            EntityControl.dead = true;
                            CatapultDeadTimer = 5;

                            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockIn));
                        }
                    }
                }
            }
        }
        else//should only ever be true after EnableOneFrameToFindAnimator is called via network event
        {
            if (other)
            {
                FindCatapultAnimator(other.gameObject);
            }
        }
    }
    private void Update()
    {
        if ((Piloting && OnCatapult) || Launching)
        {
            if (!Launching && Selected)
            {
                float Trigger;
                if (UseLeftTrigger)
                { Trigger = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger"); }
                else
                { Trigger = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger"); }
                if (Trigger > 0.75)
                {
                    if (!TriggerLastFrame)
                    {
                        Launching = true;
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(PreLaunchCatapult));
                        EntityControl.SendEventToExtensions("SFEXT_O_LaunchFromCatapult");
                    }
                    TriggerLastFrame = true;
                }
                else { TriggerLastFrame = false; }
            }
            if (EntityControl.dead)
            {
                CatapultDeadTimer -= 1;
                if (CatapultDeadTimer == 0) { EntityControl.dead = false; }
            }

            VehicleTransform.rotation = PlaneCatapultRotDif * CatapultTransform.rotation;
            VehicleTransform.position = CatapultTransform.position;
            VehicleTransform.position -= CatapultTransform.up * PlaneCatapultUpDistance;
            VehicleTransform.position += CatapultTransform.forward * PlaneCatapultBackDistance;
            VehicleRigidbody.velocity = Vector3.zero;
            VehicleRigidbody.angularVelocity = Vector3.zero;
            Quaternion CatapultRotDif = CatapultTransform.rotation * Quaternion.Inverse(CatapultRotLastFrame);
            if (Launching && !CatapultTransform.gameObject.activeInHierarchy)
            {
                float DeltaTime = Time.deltaTime;
                TriggerLastFrame = false;
                Launching = false;
                DisableOverrides();
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(CatapultLockOff));
                SAVControl.SetProgramVariable("Taxiinglerper", 0f);
                VehicleRigidbody.velocity = (CatapultTransform.position - CatapultPosLastFrame) / DeltaTime;
                Vector3 CatapultRotDifEULER = CatapultRotDif.eulerAngles;
                //.eulerangles is dumb (convert 0 - 360 to -180 - 180)
                if (CatapultRotDifEULER.x > 180) { CatapultRotDifEULER.x -= 360; }
                if (CatapultRotDifEULER.y > 180) { CatapultRotDifEULER.y -= 360; }
                if (CatapultRotDifEULER.z > 180) { CatapultRotDifEULER.z -= 360; }
                Vector3 CatapultRotDifrad = (CatapultRotDifEULER * Mathf.Deg2Rad) / DeltaTime;
                VehicleRigidbody.angularVelocity = CatapultRotDifrad;
                EntityControl.dead = true;
                SendCustomEventDelayedFrames(nameof(deadfalse), 5);
            }
            CatapultRotLastFrame = CatapultTransform.rotation;
            CatapultPosLastFrame = CatapultTransform.position;
        }
    }
    private void DisableOverrides()
    {
        if (DisableGearToggle && GearFunc)
        {
            GearFunc.SetProgramVariable("DisableGearToggle", (int)GearFunc.GetProgramVariable("DisableGearToggle") - 1);
            DisableGearToggle = false;
        }
        if (DisableTaxiRotation)
        {
            SAVControl.SetProgramVariable("DisableTaxiRotation", (int)SAVControl.GetProgramVariable("DisableTaxiRotation") - 1);
            DisableTaxiRotation = false;
        }
        if (OverrideConstantForce)
        {
            SAVControl.SetProgramVariable("OverrideConstantForce", (int)SAVControl.GetProgramVariable("OverrideConstantForce") - 1);
            OverrideConstantForce = false;
        }
    }
    public void deadfalse()
    {
        EntityControl.dead = false;
        if (!Piloting)
        {
            SFEXT_O_PilotExit();
        }
    }
    public void KeyboardInput()
    {
        if (OnCatapult && !Launching)
        {
            Launching = true;
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(PreLaunchCatapult));
        }
    }
    public void PreLaunchCatapult()
    {
        if (!IsOwner) { EnableOneFrameToFindAnimator(); }
        SendCustomEventDelayedFrames(nameof(LaunchCatapult), 3);
    }
    public void LaunchCatapult()
    {
        CatapultAnimator.SetTrigger("launch");
        if (Dial_Funcon) { Dial_Funcon.SetActive(false); }
        VehicleRigidbody.WakeUp();//i don't think it actually sleeps anyway but this might help other clients sync the launch faster idk
    }

    public void CatapultLockIn()
    {
        OnCatapult = true;
        VehicleAnimator.SetBool(ONCATAPULT_STRING, true);
        VehicleRigidbody.Sleep();//don't think this actually helps
        if (CatapultLock) { CatapultLock.Play(); }
        if (Dial_Funcon) Dial_Funcon.SetActive(true);
    }
    public void CatapultLockOff()
    {
        OnCatapult = false;
        VehicleAnimator.SetBool(ONCATAPULT_STRING, false);
    }
}