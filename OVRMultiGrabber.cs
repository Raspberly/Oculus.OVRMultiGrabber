using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OVRMultiGrabber : MonoBehaviour
{

    public float grabBegin = 0.55f;
    public float grabEnd = 0.35f;

    [SerializeField]
    protected bool m_parentHeldObject = false;

    [SerializeField]
    protected Transform m_gripTransform = null;
    [SerializeField]
    protected Collider[] m_grabVolumes = null;

    [SerializeField]
    protected OVRInput.Controller m_controller;

    [SerializeField]
    protected Transform m_parentTransform;

    protected bool m_grabVolumeEnabled = true;
    protected Vector3 m_lastPos;
    protected Quaternion m_lastRot;
    protected Quaternion m_anchorOffsetRotation;
    protected Vector3 m_anchorOffsetPosition;
    protected float m_prevFlex;
    protected Vector3 m_grabbedObjectPosOff;
    protected Quaternion m_grabbedObjectRotOff;
    protected Dictionary<OVRGrabbable, int> m_grabCandidates = new Dictionary<OVRGrabbable, int>();
    protected bool operatingWithoutOVRCameraRig = true;

    protected List<OVRGrabbable> m_grabbedObjs = new List<OVRGrabbable>();

    public List<OVRGrabbable> grabbedObjects
    {
        get { return m_grabbedObjs; }
    }


    public void ForceRelease (OVRGrabbable grabbable)
    {
        bool canRelease = (
            (m_grabbedObjs != null)
        );
        if(canRelease)
        {
            GrabEnd();
        }
    }

    protected virtual void Awake ()
    {
        m_anchorOffsetPosition = transform.localPosition;
        m_anchorOffsetRotation = transform.localRotation;

        OVRCameraRig rig = null;
        if(transform.parent != null && transform.parent.parent != null)
            rig = transform.parent.parent.GetComponent<OVRCameraRig>();

        if(rig != null)
        {
            rig.UpdatedAnchors += (r) => { OnUpdatedAnchors(); };
            operatingWithoutOVRCameraRig = false;
        }
    }

    protected virtual void Start ()
    {
        m_lastPos = transform.position;
        m_lastRot = transform.rotation;
        if(m_parentTransform == null)
        {
            if(gameObject.transform.parent != null)
            {
                m_parentTransform = gameObject.transform.parent.transform;
            }
            else
            {
                m_parentTransform = new GameObject().transform;
                m_parentTransform.position = Vector3.zero;
                m_parentTransform.rotation = Quaternion.identity;
            }
        }
    }



    void FixedUpdate ()
    {
        if(operatingWithoutOVRCameraRig)
            OnUpdatedAnchors();
    }

    void OnUpdatedAnchors ()
    {

        Vector3 handPos = OVRInput.GetLocalControllerPosition(m_controller);
        Quaternion handRot = OVRInput.GetLocalControllerRotation(m_controller);
        Vector3 destPos = m_parentTransform.TransformPoint(m_anchorOffsetPosition + handPos);
        Quaternion destRot = m_parentTransform.rotation * handRot * m_anchorOffsetRotation;
        GetComponent<Rigidbody>().MovePosition(destPos);
        GetComponent<Rigidbody>().MoveRotation(destRot);

        if(!m_parentHeldObject)
        {
            MoveGrabbedObject(destPos, destRot);
        }
        m_lastPos = transform.position;
        m_lastRot = transform.rotation;

        float prevFlex = m_prevFlex;
        m_prevFlex = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, m_controller);

        CheckForGrabOrRelease(prevFlex);
    }

    void OnDestroy ()
    {
        if(m_grabbedObjs != null)
        {
            GrabEnd();
        }
    }

    void OnTriggerEnter (Collider otherCollider)
    {
        OVRGrabbable grabbable = otherCollider.GetComponent<OVRGrabbable>() ?? otherCollider.GetComponentInParent<OVRGrabbable>();
        if(grabbable == null) return;

        int refCount = 0;
        m_grabCandidates.TryGetValue(grabbable, out refCount);
        m_grabCandidates[grabbable] = refCount + 1;
    }

    void OnTriggerExit (Collider otherCollider)
    {
        OVRGrabbable grabbable = otherCollider.GetComponent<OVRGrabbable>() ?? otherCollider.GetComponentInParent<OVRGrabbable>();
        if(grabbable == null) return;

        int refCount = 0;
        bool found = m_grabCandidates.TryGetValue(grabbable, out refCount);
        if(!found)
        {
            return;
        }

        if(refCount > 1)
        {
            m_grabCandidates[grabbable] = refCount - 1;
        }
        else
        {
            m_grabCandidates.Remove(grabbable);
        }
    }

    protected void CheckForGrabOrRelease (float prevFlex)
    {
        if((m_prevFlex >= grabBegin) && (prevFlex < grabBegin))
        {
            GrabBegin();
        }
        else if((m_prevFlex <= grabEnd) && (prevFlex > grabEnd))
        {
            GrabEnd();
        }
    }




    protected void GrabBegin ()
    {
        m_grabbedObjs = new List<OVRGrabbable>(m_grabCandidates.Keys);


        //掴める範囲内の全てのgrabbableを取得する
        foreach(OVRGrabbable grabbable in m_grabbedObjs)
        {
            grabbable.GrabBegin(null, grabbable.grabPoints[0]);
        }
        GrabVolumeEnable(false);

        if(m_grabbedObjs.Count != 0)
        {
            m_lastPos = transform.position;
            m_lastRot = transform.rotation;

            MoveGrabbedObject(m_lastPos, m_lastRot, true);
            if(m_parentHeldObject)
            {
                foreach(var grabbed in m_grabbedObjs)
                {
                    grabbed.transform.parent = transform;
                }
            }
        }
    }




    protected void MoveGrabbedObject (Vector3 pos, Quaternion rot, bool forceTeleport = false)
    {
        if(m_grabbedObjs == null)
        {
            return;
        }
        for(var i = 0; i < m_grabbedObjs.Count; i++)
        {
            if(m_grabbedObjs[i] == null) return;
            Vector3 relPos = m_grabbedObjs[i].transform.position - transform.position;
            relPos = Quaternion.Inverse(transform.rotation) * relPos;
            m_grabbedObjectPosOff = relPos;

            Quaternion relOri = Quaternion.Inverse(transform.rotation) * m_grabbedObjs[i].transform.rotation;
            m_grabbedObjectRotOff = relOri;

            Rigidbody grabbedRigidbody = m_grabbedObjs[i].grabbedRigidbody;
            Vector3 grabbablePosition = pos + rot * m_grabbedObjectPosOff;
            Quaternion grabbableRotation = rot * m_grabbedObjectRotOff;

            if(forceTeleport)
            {
                grabbedRigidbody.transform.position = grabbablePosition;
                grabbedRigidbody.transform.rotation = grabbableRotation;
            }
            else
            {
                grabbedRigidbody.MovePosition(grabbablePosition);
                grabbedRigidbody.MoveRotation(grabbableRotation);
            }
        }
    }

    protected void GrabEnd ()
    {
        if(m_grabbedObjs != null)
        {
            for(var i = 0; i < m_grabbedObjs.Count; i++)
            {
                OVRPose localPose = new OVRPose { position = OVRInput.GetLocalControllerPosition(m_controller), orientation = OVRInput.GetLocalControllerRotation(m_controller) };
                OVRPose offsetPose = new OVRPose { position = m_anchorOffsetPosition, orientation = m_anchorOffsetRotation };
                localPose = localPose * offsetPose;

                OVRPose trackingSpace = transform.ToOVRPose(true) * localPose.Inverse();
                Vector3 linearVelocity = trackingSpace.orientation * OVRInput.GetLocalControllerVelocity(m_controller);
                Vector3 angularVelocity = trackingSpace.orientation * OVRInput.GetLocalControllerAngularVelocity(m_controller);
                GrabbableRelease(m_grabbedObjs[i], linearVelocity, angularVelocity);
            }
        }
        m_grabbedObjs = null;
        GrabVolumeEnable(true);
    }


    protected void GrabbableRelease (OVRGrabbable grabbable, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        grabbable.GrabEnd(linearVelocity, angularVelocity);
        if(m_parentHeldObject) grabbable.transform.parent = null;
    }





    protected virtual void GrabVolumeEnable (bool enabled)
    {
        if(m_grabVolumeEnabled == enabled)
        {
            return;
        }

        m_grabVolumeEnabled = enabled;
        for(int i = 0; i < m_grabVolumes.Length; ++i)
        {
            Collider grabVolume = m_grabVolumes[i];
            grabVolume.enabled = m_grabVolumeEnabled;
        }

        if(!m_grabVolumeEnabled)
        {
            m_grabCandidates.Clear();
        }
    }


}
