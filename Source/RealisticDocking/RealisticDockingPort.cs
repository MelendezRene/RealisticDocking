using System;
using System.Reflection;
using UnityEngine;

namespace RealisticDocking
{
    public class RealisticDockingPort : PartModule
    {
        [KSPField(isPersistant = false)] public float softCaptureRange = 0.25f;
        [KSPField(isPersistant = false)] public float hardCaptureRange = 0.035f;
        [KSPField(isPersistant = false)] public float maxCaptureVelocity = 0.15f;
        [KSPField(isPersistant = false)] public float nominalCaptureVelocity = 0.05f;
        [KSPField(isPersistant = false)] public float maxAngularError = 8.0f;
        [KSPField(isPersistant = false)] public float hardCaptureAngularError = 1.0f;
        [KSPField(isPersistant = false)] public float maxLateralOffset = 0.12f;
        [KSPField(isPersistant = false)] public float hardCaptureOffset = 0.02f;
        [KSPField(isPersistant = false)] public float softAcquireForce = 0.35f;
        [KSPField(isPersistant = false)] public float softAcquireTorque = 0.18f;
        [KSPField(isPersistant = false)] public float hardAcquireForce = 1.2f;
        [KSPField(isPersistant = false)] public float hardAcquireTorque = 0.8f;
        [KSPField(isPersistant = false)] public bool verboseLogging = false;

        [KSPField(guiActive = true, guiName = "RD State")] public string rdState = "IDLE";
        [KSPField(guiActive = true, guiName = "Closing rate", guiUnits = " m/s", guiFormat = "F3")] public float closingRate = 0f;
        [KSPField(guiActive = true, guiName = "Angular error", guiUnits = "°", guiFormat = "F2")] public float angularError = 0f;
        [KSPField(guiActive = true, guiName = "Lateral offset", guiUnits = " m", guiFormat = "F3")] public float lateralOffset = 0f;

        private ModuleDockingNode node;
        private float stockAcquireForce;
        private float stockAcquireTorque;
        private float stockAcquireRange;
        private bool initialized;
        private FieldInfo otherNodeField;
        private FieldInfo nodeTransformField;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            node = part.FindModuleImplementing<ModuleDockingNode>();
            if (node == null)
            {
                rdState = "NO DOCKING NODE";
                return;
            }

            stockAcquireForce = node.acquireForce;
            stockAcquireTorque = node.acquireTorque;
            stockAcquireRange = node.acquireRange;

            Type t = typeof(ModuleDockingNode);
            otherNodeField = t.GetField("otherNode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            nodeTransformField = t.GetField("nodeTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            node.acquireRange = softCaptureRange;
            node.acquireForce = 0f;
            node.acquireTorque = 0f;
            initialized = true;
            Log("initialized on " + part.partInfo.title);
        }

        public void OnDestroy()
        {
            RestoreStock();
        }

        private void RestoreStock()
        {
            if (node == null) return;
            node.acquireForce = stockAcquireForce;
            node.acquireTorque = stockAcquireTorque;
            node.acquireRange = stockAcquireRange;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !initialized || node == null || vessel == null) return;

            ModuleDockingNode other = GetOtherNode();
            if (other == null || other.part == null || other.vessel == null || other.vessel == vessel)
            {
                rdState = "APPROACH";
                node.acquireRange = softCaptureRange;
                node.acquireForce = 0f;
                node.acquireTorque = 0f;
                closingRate = 0f;
                angularError = 0f;
                lateralOffset = 0f;
                return;
            }

            Transform a = GetNodeTransform(node);
            Transform b = GetNodeTransform(other);
            if (a == null || b == null)
            {
                rdState = "NO TRANSFORM";
                return;
            }

            Vector3 delta = b.position - a.position;
            float axial = Vector3.Dot(delta, a.forward);
            Vector3 lateral = delta - a.forward * axial;
            lateralOffset = lateral.magnitude;

            Vector3 va = vessel.rb_velocity;
            Vector3 vb = other.vessel.rb_velocity;
            Vector3 relV = vb - va;
            closingRate = -Vector3.Dot(relV, a.forward);

            angularError = Vector3.Angle(a.forward, -b.forward);

            float distance = delta.magnitude;
            bool softEnvelope = distance <= softCaptureRange &&
                                Mathf.Abs(closingRate) <= maxCaptureVelocity &&
                                angularError <= maxAngularError &&
                                lateralOffset <= maxLateralOffset;

            bool hardEnvelope = distance <= hardCaptureRange &&
                                Mathf.Abs(closingRate) <= nominalCaptureVelocity &&
                                angularError <= hardCaptureAngularError &&
                                lateralOffset <= hardCaptureOffset;

            if (!softEnvelope)
            {
                rdState = Mathf.Abs(closingRate) > maxCaptureVelocity ? "TOO FAST" :
                          angularError > maxAngularError ? "MISALIGNED" :
                          lateralOffset > maxLateralOffset ? "OFF AXIS" : "APPROACH";
                node.acquireRange = softCaptureRange;
                node.acquireForce = 0f;
                node.acquireTorque = 0f;
                return;
            }

            node.acquireRange = softCaptureRange;
            if (!hardEnvelope)
            {
                rdState = "SOFT CAPTURE";
                node.acquireForce = softAcquireForce;
                node.acquireTorque = softAcquireTorque;
            }
            else
            {
                rdState = "HARD CAPTURE";
                node.acquireForce = hardAcquireForce;
                node.acquireTorque = hardAcquireTorque;
            }
        }

        private ModuleDockingNode GetOtherNode()
        {
            try
            {
                if (otherNodeField == null) return null;
                return otherNodeField.GetValue(node) as ModuleDockingNode;
            }
            catch
            {
                return null;
            }
        }

        private Transform GetNodeTransform(ModuleDockingNode n)
        {
            try
            {
                if (n == null) return null;
                if (nodeTransformField != null)
                {
                    Transform tr = nodeTransformField.GetValue(n) as Transform;
                    if (tr != null) return tr;
                }
                return n.part != null ? n.part.transform : null;
            }
            catch
            {
                return n != null && n.part != null ? n.part.transform : null;
            }
        }

        private void Log(string message)
        {
            if (verboseLogging) Debug.Log("[RealisticDocking] " + message);
        }
    }
}
