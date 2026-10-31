using System;
using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Player;
using RescueSim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Survivors
{
    // Carried / Released are appended so serialized values of the older states are unchanged.
    public enum SurvivorState { Unfound, Trapped, Following, Rescued, Lost, Carried, Released }

    /// <summary>
    /// A person to rescue.
    /// Carry flow (carryMode, default): Unfound -> (player approaches and looks) Trapped -> (hold GRIP to check) ->
    /// (keep holding) Carried -> (release GRIP inside the safe zone) Rescued, or (release outside) Released, which can be
    /// picked up again. Rescued is final. Legacy flow (carryMode off): Trapped -> Following -> Rescued on entering the zone.
    /// Survivors lose health in hazard zones and can be Lost. Only one survivor is carried at a time, by one interactor.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Survivor : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] string survivorName = "Survivor";

        [Header("References")]
        [SerializeField] PlayerVitals vitals;
        [SerializeField] Transform followTarget;
        [SerializeField] ExitZone exitZone;
        [SerializeField] Transform visual;
        [SerializeField] Renderer beaconRenderer;
        [SerializeField] Image progressRing;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] SpatialSound distressSound;
        [SerializeField] SpatialSound statusSound;
        [SerializeField] MissionController mission;

        [Header("Behaviour")]
        [SerializeField, Min(1f)] float findDistance = 7f;
        [SerializeField, Range(10f, 120f)] float findViewAngle = 70f;
        [Tooltip("Seconds a carried casualty stays held while the input switches between hands and controllers.")]
        [SerializeField, Min(0f)] float handoverGrace = 2.5f;
        [SerializeField, Min(0.5f)] float assessSeconds = 1.0f;
        [SerializeField, Min(0.2f)] float walkSpeed = 1.5f;
        [SerializeField, Min(0.5f)] float followDistance = 1.8f;
        [Tooltip("Objective that must be complete before this survivor can move (e.g. the passage is cleared).")]
        [SerializeField] string requiresObjective = "";
        [Tooltip("Shown when the player tries to assist before requiresObjective is complete.")]
        [SerializeField] string blockedMessage = "Path blocked. Clear the passage first.";
        [Tooltip("Health lost per second per unit of hazard exposure rate.")]
        [SerializeField, Min(0f)] float vulnerability = 0.6f;

        [Header("Carrying")]
        [Tooltip("On: hold GRIP to check, keep holding to lift and carry, release inside the safe zone to deliver. Off: legacy lead-them-out behaviour.")]
        [SerializeField] bool carryMode = true;
        [SerializeField, Min(0.2f)] float carryDistance = 0.6f;
        [SerializeField, Min(0.2f)] float carryDrop = 0.7f;
        [SerializeField, Min(0.02f)] float carrySmoothTime = 0.15f;
        [SerializeField, Min(0.05f)] float carryYawSmoothTime = 0.35f;
        [SerializeField, Min(0.1f)] float carryHalfLength = 0.75f;

        static readonly Color CarriedColor = new Color(0.4f, 0.9f, 1f, 0.8f);
        static readonly Color ReleasedColor = new Color(1f, 0.75f, 0.2f, 0.7f);
        static readonly Color UnfoundColor = new Color(1f, 0.55f, 0.1f, 0.6f);
        static readonly Color TrappedColor = new Color(1f, 0.9f, 0.2f, 0.7f);
        static readonly Color FollowColor = new Color(0.2f, 0.7f, 1f, 0.6f);
        static readonly Color SafeColor = new Color(0.2f, 1f, 0.4f, 0.6f);
        static readonly Color LostColor = new Color(0.9f, 0.1f, 0.1f, 0.5f);

        XRSimpleInteractable _interactable;
        NavMeshAgent _agent;
        bool _holding;
        float _assess;
        float _distressTimer;
        bool _blockedWarned;
        MaterialPropertyBlock _mpb;
        Vector3 _homePosition;
        Quaternion _lyingPose;
        Quaternion _standingPose;
        Quaternion _homeRotation;
        IXRSelectInteractor _owner;
        bool _suspendOwner;
        float _graceUntil = -1f;
        BoxCollider _col;
        Vector3 _centerLocal = new Vector3(0f, 0.55f, -0.7f);
        Vector3 _carryVel;
        float _yaw, _yawVel;
        float _groundY;
        bool _settling;
        int _lastInside = -1;
        SelectFilter _filter;
        static readonly RaycastHit[] Hits = new RaycastHit[16];
        static Survivor _carrying;

        /// <summary>The survivor currently being carried (null if none).</summary>
        public static Survivor Carrying => _carrying;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _carrying = null; }
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorLegacyId = Shader.PropertyToID("_Color");

        public string Name => survivorName;
        public SurvivorState State { get; private set; } = SurvivorState.Unfound;
        public float Health { get; private set; } = 1f;
        public float AssessProgress => _assess;
        /// <summary>Test hook: acts as if the grip is held (no interactor needed).</summary>
        public bool HoldOverride { get; set; }
        public bool CarryMode { get => carryMode; set => carryMode = value; }
        /// <summary>The interactor currently holding this casualty (null when none, or during an input hand-over).</summary>
        public IXRSelectInteractor Owner => _owner;
        /// <summary>True while a carry is waiting for a different input (hand to controller or back) to take over.</summary>
        public bool InHandover => State == SurvivorState.Carried && _holding && _owner == null && _graceUntil > 0f;
        public bool IsCarried => State == SurvivorState.Carried;
        public bool IsRescued => State == SurvivorState.Rescued;
        public ExitZone Zone => exitZone;
        /// <summary>World-space centre of the body (collider centre) used for delivery validation.</summary>
        public Vector3 BodyCenter => transform.TransformPoint(_centerLocal);

        public event Action<Survivor> Found;
        public event Action<Survivor> Rescued;
        public event Action<Survivor> Lost;
        /// <summary>Raised when lifting starts.</summary>
        public event Action<Survivor> CarryStarted;
        /// <summary>Raised when carrying ends; the bool is true when the drop delivered them to the safe zone.</summary>
        public event Action<Survivor, bool> CarryEnded;

        public void Configure(PlayerVitals v, Transform follow, ExitZone exit, MissionController m)
        {
            vitals = v; followTarget = follow; exitZone = exit; mission = m;
        }

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            _agent = GetComponent<NavMeshAgent>();
            _mpb = new MaterialPropertyBlock();
            if (progressRing != null && progressRing.sprite == null) progressRing.sprite = UIFactory.WhiteSprite;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _col = GetComponent<BoxCollider>();
            if (_col != null) _centerLocal = _col.center;
            _filter = new SelectFilter(this);
            _interactable.selectFilters.Add(_filter);
            if (visual != null)
            {
                _standingPose = Quaternion.identity;
                _lyingPose = Quaternion.Euler(-82f, 0f, 0f);
                visual.localRotation = _lyingPose;
            }
        }

        void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
            if (_carrying == this) { _carrying = null; ClearZoneIndicator(); }
            _owner = null; _holding = false; _suspendOwner = false; _graceUntil = -1f;
        }

        void OnDestroy()
        {
            if (_carrying == this) _carrying = null;
            if (_interactable != null && _filter != null) _interactable.selectFilters.Remove(_filter);
        }

        void Start()
        {
            RegisterGrabCollider();
            if (_agent != null)
            {
                _agent.speed = walkSpeed;
                _agent.stoppingDistance = followDistance;
                _agent.updateRotation = false;
                _agent.enabled = false; // enabled only when following, avoids snapping to a distant navmesh
            }
            Refresh();
        }

        /// <summary>
        /// XRI leaves trigger colliders out of an interactable's collider list unless they are serialized by hand, and the grab
        /// volume here is a trigger (so it never blocks the player or the NavMesh agent). Without this the casualty could not be
        /// hovered or grabbed by any interactor, controllers included. Registered again after the list changes.
        /// </summary>
        void RegisterGrabCollider()
        {
            if (_interactable == null || _col == null || _interactable.colliders.Contains(_col)) return;
            var mgr = _interactable.interactionManager;
            bool registered = mgr != null && mgr.IsRegistered((IXRInteractable)_interactable);
            if (registered) mgr.UnregisterInteractable((IXRInteractable)_interactable);
            _interactable.colliders.Add(_col);
            if (registered) mgr.RegisterInteractable((IXRInteractable)_interactable);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (_owner != null && args.interactorObject != _owner) return; // another hand cannot take over
            _owner = args.interactorObject;
            _holding = true;
            _suspendOwner = false;
            _graceUntil = -1f;                                                // a new owner during a hand-over keeps the carry
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (_owner != null && args.interactorObject != _owner) return;
            if (_suspendOwner && State == SurvivorState.Carried)
            {
                // The owning input (e.g. a tracked hand) went away without an intentional release: keep carrying for a short
                // grace period so controllers / the other hand can take over without dropping the casualty.
                _owner = null;
                _suspendOwner = false;
                _graceUntil = Time.time + handoverGrace;
                return;
            }
            _owner = null;
            _holding = false;
            _graceUntil = -1f;
        }

        /// <summary>
        /// Called by the input layer just before it ends a selection for a non-intentional reason (tracking lost, input mode
        /// switched). A carried casualty then stays held for <see cref="handoverGrace"/> seconds waiting for another owner.
        /// </summary>
        public void SuspendOwner(IXRSelectInteractor interactor)
        {
            if (State == SurvivorState.Carried && _owner != null && _owner == interactor) _suspendOwner = true;
        }

        /// <summary>Select filter: refuses hands other than the owner, rescued/lost survivors, and a second simultaneous carry.</summary>
        bool AllowSelect(IXRSelectInteractor interactor)
        {
            if (State == SurvivorState.Rescued || State == SurvivorState.Lost) return false;
            if (_carrying != null && _carrying != this) return false;
            if (_owner != null && interactor != _owner) return false;
            return true;
        }

        sealed class SelectFilter : IXRSelectFilter
        {
            readonly Survivor _s;
            public SelectFilter(Survivor s) { _s = s; }
            public bool canProcess => true;
            public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => _s != null && _s.AllowSelect(interactor);
        }

        void Update()
        {
            switch (State)
            {
                case SurvivorState.Unfound: TickUnfound(); break;
                case SurvivorState.Trapped: TickTrapped(); break;
                case SurvivorState.Following: TickFollowing(); break;
                case SurvivorState.Carried: TickCarried(); break;
                case SurvivorState.Released: TickReleased(); break;
            }
            if (_settling && State != SurvivorState.Carried) Settle();
            if (State == SurvivorState.Unfound || State == SurvivorState.Trapped || State == SurvivorState.Following
                || State == SurvivorState.Carried || State == SurvivorState.Released)
                TickHealth();
            TickVisual();
        }

        // ---- State ticks -----------------------------------------------------------------------

        void TickUnfound()
        {
            PulseDistress();
            if (followTarget == null) return;
            Vector3 to = transform.position - followTarget.position;
            if (to.sqrMagnitude > findDistance * findDistance) return;
            float angle = Vector3.Angle(followTarget.forward, to);
            if (angle > findViewAngle) return;
            SetState(SurvivorState.Trapped);
            Found?.Invoke(this);
        }

        void TickTrapped()
        {
            PulseDistress();
            bool canMove = CanMove();
            if (_holding)
            {
                if (!canMove)
                {
                    if (!_blockedWarned)
                    {
                        _blockedWarned = true;
                        if (statusLabel != null) statusLabel.text = blockedMessage;
                    }
                    return;
                }
                _assess = Mathf.Min(1f, _assess + Time.deltaTime / assessSeconds);
                if (_assess >= 1f) { if (carryMode) BeginCarry(); else BeginFollowing(); }
            }
            else
            {
                _assess = Mathf.Max(0f, _assess - Time.deltaTime * 0.5f);
                _blockedWarned = false;
            }
            Refresh();
        }

        void TickFollowing()
        {
            if (followTarget == null) return;
            Vector3 target = followTarget.position;
            target.y = transform.position.y;
            Vector3 toTarget = target - transform.position;
            float dist = toTarget.magnitude;

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.SetDestination(followTarget.position);
                Vector3 v = _agent.desiredVelocity; v.y = 0f;
                if (v.sqrMagnitude > 0.01f) Face(v);
            }
            else if (dist > followDistance)
            {
                // Fallback when no NavMesh is available: steer directly and keep to the ground.
                Vector3 step = toTarget.normalized * (walkSpeed * Time.deltaTime);
                Vector3 next = transform.position + step;
                if (Physics.Raycast(next + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                    next.y = hit.point.y;
                transform.position = next;
                Face(toTarget);
            }

            if (exitZone != null && exitZone.Contains(transform.position))
                SetState(SurvivorState.Rescued);
        }

        void TickHealth()
        {
            if (vitals == null) return;
            float rate = vitals.SampleRate(transform.position);
            if (rate <= 0f) return;
            Health = Mathf.Max(0f, Health - rate * vulnerability * Time.deltaTime);
            if (Health <= 0f) SetState(SurvivorState.Lost);
        }

        // ---- Carrying --------------------------------------------------------------------------

        void TickReleased()
        {
            PulseDistress();
            if ((_holding || HoldOverride) && CanMove()) BeginCarry();
        }

        void BeginCarry()
        {
            if (State == SurvivorState.Rescued || State == SurvivorState.Lost || State == SurvivorState.Carried) return;
            if (_carrying != null && _carrying != this) return; // one casualty at a time
            _carrying = this;
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _yaw = followTarget != null ? followTarget.eulerAngles.y : transform.eulerAngles.y;
            _yawVel = 0f; _carryVel = Vector3.zero; _settling = false; _lastInside = -1;
            _assess = 1f;
            SetState(SurvivorState.Carried);
            statusSound?.PlayOneShot(SpatialSound.Cue.Confirm);
            CarryStarted?.Invoke(this);
        }

        void TickCarried()
        {
            if (_holding && _owner == null && _graceUntil > 0f && Time.time > _graceUntil) { _holding = false; _graceUntil = -1f; }   // hand-over timed out: a normal release
            if (followTarget == null || !(_holding || HoldOverride)) { EndCarry(); return; }
            UpdateCarryPose();
            int inside = exitZone != null && exitZone.Contains(BodyCenter) ? 1 : 0;
            if (inside != _lastInside)
            {
                _lastInside = inside;
                if (exitZone != null) exitZone.SetCarrierInside(inside == 1);
                if (statusLabel != null) statusLabel.text = inside == 1 ? "Release to deliver" : "Carrying - take me to safety";
            }
        }

        /// <summary>
        /// Body is cradled across the front of the player at waist height. Position follows the head's horizontal position and a
        /// slowly smoothed yaw, so quick head turns do not whip the casualty around. Wall and ground checks keep it out of geometry.
        /// </summary>
        void UpdateCarryPose()
        {
            Vector3 head = followTarget.position;
            _yaw = Mathf.SmoothDampAngle(_yaw, followTarget.eulerAngles.y, ref _yawVel, carryYawSmoothTime);
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            float dist = carryDistance;
            Vector3 chestOrigin = new Vector3(head.x, head.y - carryDrop, head.z);
            if (TryCast(chestOrigin, fwd, carryDistance + 0.4f, out var wall)) dist = Mathf.Max(0.15f, wall.distance - 0.4f);
            Vector3 center = chestOrigin + fwd * dist;

            // Keep the ends of the body out of side walls.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 dir = right * side;
                if (TryCast(center, dir, carryHalfLength, out var end)) center -= dir * (carryHalfLength - end.distance);
            }

            float ground = GroundAt(new Vector3(center.x, head.y, center.z), head.y - 0.1f, out bool found);
            if (found) center.y = Mathf.Max(center.y, ground + 0.3f);
            if (center.y > head.y - 0.2f) center.y = head.y - 0.2f;

            Quaternion rot = Quaternion.Euler(0f, _yaw + 90f, 0f);
            Vector3 targetRoot = center - rot * _centerLocal;
            transform.position = Vector3.SmoothDamp(transform.position, targetRoot, ref _carryVel, carrySmoothTime, 8f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, 360f * Time.deltaTime);
        }

        /// <summary>Called when the grip is released (or the interactor goes away).</summary>
        void EndCarry()
        {
            if (State != SurvivorState.Carried) return;
            ClearZoneIndicator();
            _carrying = null;
            _carryVel = Vector3.zero;
            bool delivered = exitZone != null && exitZone.Contains(BodyCenter);
            _groundY = GroundAt(transform.position + Vector3.up * 1.0f, transform.position.y + 1.5f, out bool found);
            _settling = found;
            if (!found) _groundY = transform.position.y;
            if (delivered)
            {
                // Validated by the casualty's own position, not the player's: credit exactly once.
                SetState(SurvivorState.Rescued);
                CarryEnded?.Invoke(this, true);
            }
            else
            {
                SetState(SurvivorState.Released);
                CarryEnded?.Invoke(this, false);
            }
        }

        void Settle()
        {
            Vector3 p = transform.position;
            float vy = 0f;
            p.y = Mathf.SmoothDamp(p.y, _groundY, ref vy, 0.12f, 6f, Time.deltaTime);
            if (Mathf.Abs(p.y - _groundY) < 0.005f) { p.y = _groundY; _settling = false; }
            transform.position = p;
        }

        void ClearZoneIndicator()
        {
            _lastInside = -1;
            if (exitZone != null) exitZone.SetCarrierInside(false);
        }

        static bool TryCast(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best, Survivor ignore = null)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            best = default;
            float d = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.distance < d && IsSolid(h.collider)) { best = h; d = h.distance; }
            }
            return d < float.MaxValue;
        }

        static bool IsSolid(Collider c)
        {
            if (c == null || c.isTrigger) return false;
            if (c.GetComponentInParent<Survivor>() != null) return false;       // casualties never block each other
            if (c.GetComponentInParent<XROrigin>() != null) return false;       // the player rig
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) return false;                     // loose props are not ground or walls
            return true;
        }

        float GroundAt(Vector3 from, float originY, out bool found)
        {
            Vector3 o = new Vector3(from.x, originY, from.z);
            found = TryCast(o, Vector3.down, 6f, out var hit);
            return found ? hit.point.y : transform.position.y;
        }

        /// <summary>Back to the initial, untouched state (mission restart / in-scene reset). Does not fire events.</summary>
        public void ResetSurvivor()
        {
            if (_carrying == this) _carrying = null;
            ClearZoneIndicator();
            HoldOverride = false; _holding = false; _owner = null; _suspendOwner = false; _graceUntil = -1f; _settling = false; _carryVel = Vector3.zero;
            _assess = 0f; _blockedWarned = false; Health = 1f;
            if (_agent != null) { if (_agent.enabled) _agent.ResetPath(); _agent.enabled = false; }
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            State = SurvivorState.Unfound;
            if (_interactable != null) _interactable.enabled = true;
            if (visual != null) visual.localRotation = _lyingPose;
            Refresh();
        }

        // ---- Transitions -----------------------------------------------------------------------

        bool CanMove() => string.IsNullOrEmpty(requiresObjective) || mission == null || mission.Objectives == null || mission.Objectives.IsComplete(requiresObjective);

        void BeginFollowing()
        {
            if (_agent != null)
            {
                _agent.enabled = true;
                if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
                    _agent.Warp(hit.position);
            }
            SetState(SurvivorState.Following);
            if (statusSound != null) statusSound.PlayOneShot(SpatialSound.Cue.Confirm);
        }

        void SetState(SurvivorState s)
        {
            if (State == s) return;
            if (State == SurvivorState.Rescued) return; // final: never scored or changed twice
            bool wasCarried = State == SurvivorState.Carried;
            State = s;
            if (wasCarried && s == SurvivorState.Lost) CarryEnded?.Invoke(this, false);
            if (s != SurvivorState.Carried && _carrying == this) { _carrying = null; ClearZoneIndicator(); }
            if (s == SurvivorState.Rescued || s == SurvivorState.Lost)
            {
                _holding = false;
                if (_interactable != null) _interactable.enabled = false; // no more grabbing

                if (_agent != null && _agent.enabled) _agent.ResetPath();
                if (distressSound != null) distressSound.Stop();
            }
            Refresh();
            if (s == SurvivorState.Rescued) { statusSound?.PlayOneShot(SpatialSound.Cue.Success); Rescued?.Invoke(this); }
            else if (s == SurvivorState.Lost) { statusSound?.PlayOneShot(SpatialSound.Cue.Failure); Lost?.Invoke(this); }
        }

        // ---- Presentation ----------------------------------------------------------------------

        void PulseDistress()
        {
            if (distressSound == null) return;
            _distressTimer -= Time.deltaTime;
            if (_distressTimer > 0f) return;
            _distressTimer = UnityEngine.Random.Range(2.5f, 4f);
            distressSound.PlayOneShot(SpatialSound.Cue.DistressWhistle);
        }

        void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 6f * Time.deltaTime);
        }

        void TickVisual()
        {
            if (visual == null) return;
            Quaternion target = (State == SurvivorState.Following || State == SurvivorState.Rescued) ? _standingPose : _lyingPose;
            visual.localRotation = Quaternion.Slerp(visual.localRotation, target, 3f * Time.deltaTime);
        }

        void Refresh()
        {
            Color c = State switch
            {
                SurvivorState.Unfound => UnfoundColor,
                SurvivorState.Trapped => TrappedColor,
                SurvivorState.Following => FollowColor,
                SurvivorState.Rescued => SafeColor,
                SurvivorState.Carried => CarriedColor,
                SurvivorState.Released => ReleasedColor,
                _ => LostColor
            };
            if (beaconRenderer != null)
            {
                beaconRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, c);
                _mpb.SetColor(ColorLegacyId, c);
                beaconRenderer.SetPropertyBlock(_mpb);
                beaconRenderer.enabled = State != SurvivorState.Rescued || true;
            }
            if (progressRing != null)
            {
                progressRing.gameObject.SetActive(State == SurvivorState.Trapped);
                progressRing.fillAmount = _assess;
            }
            if (statusLabel != null)
            {
                statusLabel.text = State switch
                {
                    SurvivorState.Unfound => "",
                    SurvivorState.Trapped => _assess > 0f ? "Checking..." : (carryMode ? "Hold GRIP to carry" : "Hold GRIP to assist"),
                    SurvivorState.Following => "Follow me out",
                    SurvivorState.Carried => "Carrying - take me to safety",
                    SurvivorState.Released => "Hold GRIP to carry",
                    SurvivorState.Rescued => "Safe",
                    _ => "Lost"
                };
            }
        }

        /// <summary>Test hook: put the survivor straight into a state without playing the interaction.</summary>
        public void ForceState(SurvivorState s)
        {
            if (s == SurvivorState.Trapped && State == SurvivorState.Unfound) { SetState(s); Found?.Invoke(this); return; }
            if (s == SurvivorState.Carried) { if (State == SurvivorState.Unfound) ForceState(SurvivorState.Trapped); BeginCarry(); return; }
            SetState(s);
        }
    }
}
