using System;
using System.Reflection;
using UnityEngine;

namespace RealisticDocking
{
    public class RealisticDockingPort : PartModule
    {
        [KSPField(isPersistant = false)] public string dockingRole = "AUTO";
        [KSPField(isPersistant = false)] public string dockingProfile = "GENERIC";

        [KSPField(isPersistant = false)] public float targetDetectionRange = 5.0f;
        [KSPField(isPersistant = false)] public float alignmentAssistRange = 2.5f;
        [KSPField(isPersistant = false)] public float maxAssistAngularError = 25.0f;
        [KSPField(isPersistant = false)] public float positionGain = 0.65f;
        [KSPField(isPersistant = false)] public float velocityDamping = 1.10f;
        [KSPField(isPersistant = false)] public float attitudeGain = 3.0f;
        [KSPField(isPersistant = false)] public float angularDamping = 1.25f;
        [KSPField(isPersistant = false)] public float maxAlignmentAcceleration = 0.12f;
        [KSPField(isPersistant = false)] public float maxAlignmentTorque = 8.0f;

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

        [KSPField(isPersistant = true, guiActive = true, guiName = "Alignment assist")]
        public bool alignmentAssistEnabled = true;

        [KSPField(guiActive = true, guiName = "RD Role")] public string detectedRole = "AUTO";
        [KSPField(guiActive = true, guiName = "RD State")] public string rdState = "IDLE";
        [KSPField(guiActive = true, guiName = "Target port")] public string targetPortName = "NONE";
        [KSPField(guiActive = true, guiName = "Target range", guiUnits = " m", guiFormat = "F2")] public float targetRange = 0f;
        [KSPField(guiActive = true, guiName = "Closing rate", guiUnits = " m/s", guiFormat = "F3")] public float closingRate = 0f;
        [KSPField(guiActive = true, guiName = "Angular error", guiUnits = " deg", guiFormat = "F2")] public float angularError = 0f;
        [KSPField(guiActive = true, guiName = "Lateral offset", guiUnits = " m", guiFormat = "F3")] public float lateralOffset = 0f;

        private ModuleDockingNode node;
        private RealisticDockingPort targetPort;
        private float stockAcquireForce;
        private float stockAcquireTorque;
        private float stockAcquireRange;
        private bool initialized;
        private FieldInfo otherNodeField;
        private FieldInfo nodeTransformField;
        private float nextTargetScan;

        [KSPEvent(guiActive = true, guiName = "Toggle alignment assist", active = true)]
        public void ToggleAlignmentAssist()
        {
            alignmentAssistEnabled = !alignmentAssistEnabled;
        }

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

            detectedRole = ResolveRole();
            node.acquireRange = softCaptureRange;
            node.acquireForce = 0f;
            node.acquireTorque = 0f;
            initialized = true;
            Log("initialized " + part.partInfo.name + " role=" + detectedRole + " profile=" + dockingProfile);
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

            detectedRole = ResolveRole();

            ModuleDockingNode stockOther = GetOtherNode();
            RealisticDockingPort stockOtherPort = null;
            if (stockOther != null && stockOther.part != null)
                stockOtherPort = stockOther.part.FindModuleImplementing<RealisticDockingPort>();

            if (stockOtherPort != null)
                targetPort = stockOtherPort;
            else if (IsActiveRole() && Time.time >= nextTargetScan)
            {
                nextTargetScan = Time.time + 0.20f;
                targetPort = FindBestPassiveTarget();
            }

            if (!TargetIsValid(targetPort))
            {
                ClearTargetTelemetry();
                rdState = IsPassiveRole() ? "PASSIVE / READY" : "SEARCHING PASSIVE";
                SuppressMagnet();
                return;
            }

            UpdateTelemetry(targetPort);

            if (IsActiveRole() && alignmentAssistEnabled)
                ApplyPassiveAlignmentAssist(targetPort);

            ControlCaptureEnvelope(targetPort);
        }

        private void ControlCaptureEnvelope(RealisticDockingPort otherPort)
        {
            float distance = targetRange;
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
                rdState = Mathf.Abs(closingRate) > maxCaptureVelocity && distance <= softCaptureRange ? "TOO FAST" :
                          angularError > maxAngularError && distance <= softCaptureRange ? "MISALIGNED" :
                          lateralOffset > maxLateralOffset && distance <= softCaptureRange ? "OFF AXIS" :
                          IsActiveRole() && alignmentAssistEnabled && distance <= alignmentAssistRange ? "AUTO ALIGN" :
                          "APPROACH";
                SuppressMagnet();
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

        private void SuppressMagnet()
        {
            if (node == null) return;
            node.acquireRange = softCaptureRange;
            node.acquireForce = 0f;
            node.acquireTorque = 0f;
        }

        private void UpdateTelemetry(RealisticDockingPort otherPort)
        {
            Transform a = GetNodeTransform(node);
            Transform b = GetNodeTransform(otherPort.node);
            if (a == null || b == null) return;

            Vector3 delta = b.position - a.position;
            targetRange = delta.magnitude;
            targetPortName = otherPort.part != null && otherPort.part.partInfo != null
                ? otherPort.part.partInfo.title
                : "PASSIVE";

            float axial = Vector3.Dot(delta, a.forward);
            Vector3 lateral = delta - a.forward * axial;
            lateralOffset = lateral.magnitude;

            Vector3 va = vessel.rb_velocity;
            Vector3 vb = otherPort.vessel.rb_velocity;
            Vector3 relV = vb - va;
            closingRate = -Vector3.Dot(relV, a.forward);
            angularError = Vector3.Angle(a.forward, -b.forward);
        }

        private void ApplyPassiveAlignmentAssist(RealisticDockingPort otherPort)
        {
            if (targetRange <= 0f || targetRange > alignmentAssistRange) return;
            if (angularError > maxAssistAngularError) return;
            if (part == null || part.Rigidbody == null) return;

            Transform a = GetNodeTransform(node);
            Transform b = GetNodeTransform(otherPort.node);
            if (a == null || b == null) return;

            Vector3 delta = b.position - a.position;

            // Preserve pilot control along the docking axis. Only correct lateral displacement.
            Vector3 targetAxis = -b.forward.normalized;
            float axial = Vector3.Dot(delta, targetAxis);
            Vector3 lateralError = delta - targetAxis * axial;

            Vector3 relVelocity = (Vector3)(vessel.rb_velocity - otherPort.vessel.rb_velocity);
            Vector3 lateralVelocity = relVelocity - targetAxis * Vector3.Dot(relVelocity, targetAxis);

            Vector3 desiredAccel = lateralError * positionGain - lateralVelocity * velocityDamping;
            desiredAccel = Vector3.ClampMagnitude(desiredAccel, maxAlignmentAcceleration);

            float vesselMass = Mathf.Max((float)vessel.totalMass, 0.001f);
            Vector3 alignmentForce = desiredAccel * vesselMass;
            part.Rigidbody.AddForce(alignmentForce, ForceMode.Force);

            Vector3 desiredForward = -b.forward.normalized;
            Vector3 cross = Vector3.Cross(a.forward.normalized, desiredForward);
            float sin = cross.magnitude;
            if (sin > 0.00001f)
            {
                Vector3 axis = cross / sin;
                float angleRad = Mathf.Asin(Mathf.Clamp(sin, -1f, 1f));
                if (Vector3.Dot(a.forward, desiredForward) < 0f)
                    angleRad = Mathf.PI - angleRad;

                Vector3 angularVelocity = part.Rigidbody.angularVelocity;
                Vector3 torque = axis * (angleRad * attitudeGain) - angularVelocity * angularDamping;
                torque = Vector3.ClampMagnitude(torque, maxAlignmentTorque);
                part.Rigidbody.AddTorque(torque, ForceMode.Force);
            }
        }

        private RealisticDockingPort FindBestPassiveTarget()
        {
            Transform a = GetNodeTransform(node);
            if (a == null) return null;

            RealisticDockingPort best = null;
            float bestScore = float.MaxValue;

            foreach (Vessel candidateVessel in FlightGlobals.VesselsLoaded)
            {
                if (candidateVessel == null || candidateVessel == vessel || !candidateVessel.loaded) continue;

                foreach (Part candidatePart in candidateVessel.parts)
                {
                    if (candidatePart == null) continue;
                    RealisticDockingPort candidate = candidatePart.FindModuleImplementing<RealisticDockingPort>();
                    if (candidate == null || !candidate.initialized || !candidate.IsPassiveRole()) continue;
                    if (candidate.node == null || !NodeTypesCompatible(node, candidate.node)) continue;

                    Transform b = candidate.GetNodeTransform(candidate.node);
                    if (b == null) continue;

                    Vector3 delta = b.position - a.position;
                    float distance = delta.magnitude;
                    if (distance > targetDetectionRange || distance < 0.001f) continue;

                    Vector3 direction = delta / distance;
                    float facing = Vector3.Dot(a.forward, direction);
                    if (facing < 0.35f) continue;

                    float faceError = Vector3.Angle(a.forward, -b.forward);
                    float score = distance + faceError * 0.025f - facing * 0.25f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            if (best != null)
                Log("passive target detected: " + best.part.partInfo.name);

            return best;
        }

        private bool TargetIsValid(RealisticDockingPort p)
        {
            if (p == null || p == this || p.part == null || p.vessel == null || p.node == null) return false;
            if (p.vessel == vessel) return false;
            if (!NodeTypesCompatible(node, p.node)) return false;

            Transform a = GetNodeTransform(node);
            Transform b = GetNodeTransform(p.node);
            if (a == null || b == null) return false;

            return Vector3.Distance(a.position, b.position) <= targetDetectionRange * 1.25f;
        }

        private static bool NodeTypesCompatible(ModuleDockingNode a, ModuleDockingNode b)
        {
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.nodeType) || string.IsNullOrEmpty(b.nodeType)) return false;
            return string.Equals(a.nodeType, b.nodeType, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsActiveRole()
        {
            return string.Equals(detectedRole, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPassiveRole()
        {
            return string.Equals(detectedRole, "PASSIVE", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveRole()
        {
            if (!string.IsNullOrEmpty(dockingRole) && !string.Equals(dockingRole, "AUTO", StringComparison.OrdinalIgnoreCase))
                return dockingRole.ToUpperInvariant();

            if (part == null || part.partInfo == null) return "ACTIVE";
            string n = part.partInfo.name ?? string.Empty;
            string title = part.partInfo.title ?? string.Empty;

            if (n.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("passive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Equals("B10_IDA", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("B10_hybrid_female", StringComparison.OrdinalIgnoreCase))
                return "PASSIVE";

            if (n.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Equals("B10_NDS", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("B10_APASv2", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("B10_hybrid_male", StringComparison.OrdinalIgnoreCase))
                return "ACTIVE";

            return "ACTIVE";
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

        private void ClearTargetTelemetry()
        {
            targetPort = null;
            targetPortName = "NONE";
            targetRange = 0f;
            closingRate = 0f;
            angularError = 0f;
            lateralOffset = 0f;
        }

        private void Log(string message)
        {
            if (verboseLogging) Debug.Log("[RealisticDocking] " + message);
        }
    }
}
