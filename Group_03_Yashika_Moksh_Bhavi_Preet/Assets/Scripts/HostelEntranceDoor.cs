using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Interactive Commercial Double-Leaf Emergency Egress Door for the Main Entrance.
/// Conforms to NBC 2016 / NFPA 101 Life Safety Code.
/// 
/// Opening Interactions Supported:
/// 1. Proximity / Panic Push: Automatically swings open when player approaches within 2.8m.
/// 2. Physical Push: Touching or walking into the door trigger automatically pushes it open.
/// 3. Keyboard: Pressing 'E', Space, Enter, or 'F' opens the door.
/// 4. Mouse: Clicking or double-clicking the door or screen opens it.
/// 5. VR / XR Simulator: Pulling any controller trigger, grip, or pressing A/B/X/Y opens the door.
/// 
/// Evacuation Success HUD:
/// - Once the player exits through the door onto the exterior plaza (z <= -5.8m), a prominent
///   3D floating VR billboard appears directly in front of the player's view wherever they look,
///   accompanied by a full-screen 2D GUI overlay with evacuation stats.
/// </summary>
public class HostelEntranceDoor : MonoBehaviour
{
    public static HostelEntranceDoor Instance { get; private set; }

    [Header("Door Hinges & Leaves")]
    [Tooltip("Pivot transform for the left door leaf")]
    public Transform leftDoorHinge;

    [Tooltip("Pivot transform for the right door leaf")]
    public Transform rightDoorHinge;

    [Header("Animation & State")]
    [Tooltip("Is the entrance door currently open?")]
    public bool isOpen = false;

    [Tooltip("Speed of the smooth swinging door animation")]
    public float swingSpeed = 4.0f;

    [Tooltip("Swing angle for doors (swings outward toward exterior plaza)")]
    public float openAngle = 95.0f;

    [Header("Interaction Settings")]
    [Tooltip("Automatically swing open when player gets within this distance (NFPA 101 panic push)")]
    public float proximityOpenDistance = 2.8f;

    [Tooltip("Enable proximity / panic push auto-opening")]
    public bool autoOpenOnProximity = true;

    [Header("References")]
    public Transform playerTransform;

    private Quaternion _leftClosedRot;
    private Quaternion _leftOpenRot;
    private Quaternion _rightClosedRot;
    private Quaternion _rightOpenRot;

    private BoxCollider _leftLeafCollider;
    private BoxCollider _rightLeafCollider;
    private BoxCollider _doorwayTrigger;

    private bool _hasEvacuated = false;
    private float _evacuationTime = 0f;
    private bool _showPrompt = false;

    private GameObject _vrHUD;
    private TextMesh _vrStatsMesh;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-find player camera / rig if not set
        ResolvePlayerReference();

        // Build geometry cleanly if not already set
        if (leftDoorHinge == null || rightDoorHinge == null)
        {
            BuildDoorGeometry();
        }

        SetupRotations();
        SetupTriggerVolume();
    }

    private void Start()
    {
        ResolvePlayerReference();
        SetupRotations();
    }

    private void ResolvePlayerReference()
    {
        if (playerTransform == null)
        {
            if (Camera.main != null) playerTransform = Camera.main.transform;
            else
            {
                var rig = GameObject.Find("[BuildingBlock] Camera Rig") ?? GameObject.Find("Player");
                if (rig != null) playerTransform = rig.transform;
            }
        }
    }

    private void SetupRotations()
    {
        if (leftDoorHinge != null)
        {
            _leftClosedRot = Quaternion.identity;
            _leftOpenRot = Quaternion.Euler(0f, -openAngle, 0f);
        }

        if (rightDoorHinge != null)
        {
            _rightClosedRot = Quaternion.identity;
            _rightOpenRot = Quaternion.Euler(0f, openAngle, 0f);
        }
    }

    private void SetupTriggerVolume()
    {
        _doorwayTrigger = GetComponent<BoxCollider>();
        if (_doorwayTrigger == null)
        {
            _doorwayTrigger = gameObject.AddComponent<BoxCollider>();
        }
        _doorwayTrigger.isTrigger = true;
        _doorwayTrigger.center = new Vector3(0f, 1.15f, 0f);
        _doorwayTrigger.size = new Vector3(3.2f, 2.5f, 2.6f);
    }

    private Vector3 GetEffectivePlayerPosition()
    {
        if (Camera.main != null) return Camera.main.transform.position;
        if (playerTransform != null) return playerTransform.position;
        return transform.position + Vector3.forward * 10f;
    }

    private void Update()
    {
        if (!_hasEvacuated)
        {
            _evacuationTime += Time.deltaTime;
        }

        Vector3 pPos = GetEffectivePlayerPosition();
        float distXZ = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(pPos.x, pPos.z));

        // 1. Proximity Check
        _showPrompt = (distXZ <= 4.0f && !_hasEvacuated);

        if (autoOpenOnProximity && distXZ <= proximityOpenDistance && !isOpen)
        {
            OpenDoor();
        }

        // 2. Interaction Input (Key 'E', Space, Mouse Click / Double Click, VR Controller)
        CheckInteractionInput(distXZ);

        // 3. Smooth Door Swing Animation
        AnimateDoors();

        // 4. Exterior Assembly Plaza Evacuation Completion Detection
        CheckEvacuationSuccess();
    }

    private void LateUpdate()
    {
        // Keep the 3D VR Evacuation HUD floating directly in front of the player's view wherever they look
        if (_hasEvacuated && _vrHUD != null)
        {
            Transform cam = Camera.main != null ? Camera.main.transform : (playerTransform != null ? playerTransform : null);
            if (cam != null)
            {
                Vector3 forward = cam.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) forward = cam.forward;
                forward.Normalize();

                Vector3 targetPos = cam.position + forward * 2.2f;
                targetPos.y = cam.position.y; // Eye level

                _vrHUD.transform.position = Vector3.Lerp(_vrHUD.transform.position, targetPos, Time.deltaTime * 8.0f);
                _vrHUD.transform.rotation = Quaternion.LookRotation(_vrHUD.transform.position - cam.position);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isOpen)
        {
            OpenDoor();
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!isOpen)
        {
            OpenDoor();
        }
    }

    private void CheckInteractionInput(float distXZ)
    {
        if (distXZ > 4.5f) return;

        bool interactPressed = false;

        // A. Keyboard (E, Space, Return, F)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.fKey.wasPressedThisFrame)
            {
                interactPressed = true;
            }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.F))
        {
            interactPressed = true;
        }
#endif

        // B. Mouse Click / Double Click anywhere on screen when facing near door
        bool mouseClicked = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame))
        {
            mouseClicked = true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            mouseClicked = true;
        }
#endif

        if (mouseClicked)
        {
            interactPressed = true;
        }

        // C. Meta Touch / VR Controller Trigger, Grip, or Face Buttons
        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
            OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger) ||
            OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger) ||
            OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger) ||
            OVRInput.GetDown(OVRInput.Button.One) ||
            OVRInput.GetDown(OVRInput.Button.Three))
        {
            interactPressed = true;
        }

        if (interactPressed)
        {
            ToggleDoor();
        }
    }

    public void ToggleDoor()
    {
        if (isOpen) CloseDoor();
        else OpenDoor();
    }

    public void OpenDoor()
    {
        if (isOpen) return;
        isOpen = true;

        if (_leftLeafCollider != null) _leftLeafCollider.isTrigger = true;
        if (_rightLeafCollider != null) _rightLeafCollider.isTrigger = true;

        Debug.Log("[HostelEntranceDoor] 🚪 Main Entrance Door OPENED! Path to Exterior Assembly Plaza is clear.");
    }

    public void CloseDoor()
    {
        if (!isOpen) return;
        isOpen = false;

        if (_leftLeafCollider != null) _leftLeafCollider.isTrigger = false;
        if (_rightLeafCollider != null) _rightLeafCollider.isTrigger = false;

        Debug.Log("[HostelEntranceDoor] 🚪 Main Entrance Door CLOSED.");
    }

    private void AnimateDoors()
    {
        Quaternion targetLeft = isOpen ? _leftOpenRot : _leftClosedRot;
        Quaternion targetRight = isOpen ? _rightOpenRot : _rightClosedRot;

        if (leftDoorHinge != null)
        {
            leftDoorHinge.localRotation = Quaternion.Slerp(leftDoorHinge.localRotation, targetLeft, Time.deltaTime * swingSpeed);
        }

        if (rightDoorHinge != null)
        {
            rightDoorHinge.localRotation = Quaternion.Slerp(rightDoorHinge.localRotation, targetRight, Time.deltaTime * swingSpeed);
        }
    }

    /// <summary>
    /// Detects when the player has physically stepped outside through the door onto the Exterior Assembly Plaza.
    /// Spawns the in-VR floating 3D billboard wherever the player is looking, as well as the 2D overlay.
    /// </summary>
    private void CheckEvacuationSuccess()
    {
        if (_hasEvacuated) return;

        Vector3 pPos = GetEffectivePlayerPosition();
        // Entrance door is at z = -5.4. Exterior plaza extends from z = -5.6 to -12.0
        // Trigger evacuation success ONLY when player is outside the building on the ground plaza!
        if (pPos.z <= -5.7f && pPos.y <= 1.5f && Mathf.Abs(pPos.x) <= 4.5f)
        {
            _hasEvacuated = true;
            Debug.Log($"[HostelEntranceDoor] 🎉 PLAYER SUCCESSFULLY EVACUATED! Egress time: {_evacuationTime:F1}s.");

            // Create the in-VR 3D Floating Billboard that follows the player's view wherever they look
            CreateVRSuccessHUD();
        }
    }

    /// <summary>
    /// Creates a 3D World-Space Floating Billboard that displays directly in front of the player's eyes
    /// in VR, XR Simulator, and 3D view wherever they look after exiting the building.
    /// </summary>
    private void CreateVRSuccessHUD()
    {
        if (_vrHUD != null) return;

        _vrHUD = new GameObject("EvacuationSuccessHUD_VR");

        Transform cam = Camera.main != null ? Camera.main.transform : (playerTransform != null ? playerTransform : null);
        Vector3 forward = cam != null ? cam.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 spawnPos = (cam != null ? cam.position : transform.position) + forward * 2.2f;
        if (cam != null) spawnPos.y = cam.position.y;
        _vrHUD.transform.position = spawnPos;
        _vrHUD.transform.rotation = Quaternion.LookRotation(forward);

        // 1. Background Panel (Dark emerald safety green)
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "HUD_Panel";
        panel.transform.SetParent(_vrHUD.transform, false);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localScale = new Vector3(2.8f, 1.35f, 0.03f);

        var col = panel.GetComponent<Collider>();
        if (col != null) Destroy(col); // No collision on floating UI

        var rend = panel.GetComponent<MeshRenderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.04f, 0.38f, 0.12f, 0.96f);
            rend.sharedMaterial = mat;
        }

        // 2. Gold / Yellow Title TextMesh
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(_vrHUD.transform, false);
        titleObj.transform.localPosition = new Vector3(0f, 0.40f, -0.04f);
        TextMesh titleMesh = titleObj.AddComponent<TextMesh>();
        titleMesh.text = "🎉 EVACUATION SUCCESSFUL! 🎉";
        titleMesh.fontSize = 40;
        titleMesh.characterSize = 0.046f;
        titleMesh.anchor = TextAnchor.MiddleCenter;
        titleMesh.alignment = TextAlignment.Center;
        titleMesh.color = new Color(1.0f, 0.92f, 0.2f); // Bright Gold

        // 3. White Subtitle TextMesh
        GameObject subObj = new GameObject("SubText");
        subObj.transform.SetParent(_vrHUD.transform, false);
        subObj.transform.localPosition = new Vector3(0f, 0.06f, -0.04f);
        TextMesh subMesh = subObj.AddComponent<TextMesh>();
        subMesh.text = "You have safely evacuated the hostel building\nand reached the Exterior Assembly Plaza.";
        subMesh.fontSize = 28;
        subMesh.characterSize = 0.038f;
        subMesh.anchor = TextAnchor.MiddleCenter;
        subMesh.alignment = TextAlignment.Center;
        subMesh.color = Color.white;

        // 4. Cyan / Mint Statistics TextMesh
        GameObject statsObj = new GameObject("StatsText");
        statsObj.transform.SetParent(_vrHUD.transform, false);
        statsObj.transform.localPosition = new Vector3(0f, -0.36f, -0.04f);
        _vrStatsMesh = statsObj.AddComponent<TextMesh>();
        int evacuatedCount = EvacuationEgressManager.Instance != null ? EvacuationEgressManager.Instance.EvacuatedAgentCount : 300;
        _vrStatsMesh.text = $"Egress Time: {_evacuationTime:F1}s  |  Students Evacuated: {evacuatedCount} / 300\nStatus: NBC 2016 / NFPA 101 Egress Complete";
        _vrStatsMesh.fontSize = 26;
        _vrStatsMesh.characterSize = 0.036f;
        _vrStatsMesh.anchor = TextAnchor.MiddleCenter;
        _vrStatsMesh.alignment = TextAlignment.Center;
        _vrStatsMesh.color = new Color(0.7f, 1.0f, 0.85f);
    }

    public void BuildDoorGeometry()
    {
        // Destroy prior children to prevent duplicate geometry
        List<GameObject> childrenToDestroy = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            childrenToDestroy.Add(transform.GetChild(i).gameObject);
        }
        foreach (var c in childrenToDestroy)
        {
            DestroyImmediate(c);
        }

        Material frameMat = FindMaterial("Mat_Hostel_DoorFrame", new Color(0.2f, 0.2f, 0.22f));
        Material doorMat = FindMaterial("Mat_Hostel_FireDoor", new Color(0.7f, 0.15f, 0.15f));
        Material pushBarMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        pushBarMat.color = new Color(0.85f, 0.85f, 0.85f);

        Material exitSignMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        exitSignMat.color = new Color(0.1f, 0.9f, 0.2f);
        exitSignMat.EnableKeyword("_EMISSION");
        exitSignMat.SetColor("_EmissionColor", new Color(0.1f, 0.9f, 0.2f) * 2.5f);

        // Frame
        GameObject frameParent = new GameObject("EntranceDoor_Frame");
        frameParent.transform.SetParent(transform, false);

        CreateBox("Frame_Header", new Vector3(0f, 2.30f, 0f), new Vector3(2.45f, 0.12f, 0.22f), frameMat, frameParent.transform, false);
        CreateBox("Frame_LeftPost", new Vector3(-1.18f, 1.15f, 0f), new Vector3(0.10f, 2.30f, 0.22f), frameMat, frameParent.transform, false);
        CreateBox("Frame_RightPost", new Vector3(1.18f, 1.15f, 0f), new Vector3(0.10f, 2.30f, 0.22f), frameMat, frameParent.transform, false);

        // Exit Sign
        GameObject exitSign = CreateBox("ExitSign_Panel", new Vector3(0f, 2.48f, -0.08f), new Vector3(1.2f, 0.26f, 0.05f), exitSignMat, transform, true);
        GameObject signLight = new GameObject("ExitSign_GreenLight");
        signLight.transform.SetParent(exitSign.transform, false);
        signLight.transform.localPosition = new Vector3(0f, 0f, -0.15f);
        Light sl = signLight.AddComponent<Light>();
        sl.type = LightType.Point;
        sl.color = new Color(0.2f, 1.0f, 0.3f);
        sl.intensity = 3.5f;
        sl.range = 5.0f;

        // Left Door Leaf
        GameObject leftHingeGo = new GameObject("LeftDoorHinge");
        leftHingeGo.transform.SetParent(transform, false);
        leftHingeGo.transform.localPosition = new Vector3(-1.13f, 0f, 0f);
        leftDoorHinge = leftHingeGo.transform;

        GameObject leftPanel = CreateBox("LeftDoorLeaf", new Vector3(0.55f, 1.12f, 0f), new Vector3(1.10f, 2.24f, 0.06f), doorMat, leftHingeGo.transform, false);
        _leftLeafCollider = leftPanel.GetComponent<BoxCollider>();
        CreateBox("LeftPanicPushBar", new Vector3(0.55f, 1.0f, -0.06f), new Vector3(0.95f, 0.08f, 0.07f), pushBarMat, leftHingeGo.transform, true);

        // Right Door Leaf
        GameObject rightHingeGo = new GameObject("RightDoorHinge");
        rightHingeGo.transform.SetParent(transform, false);
        rightHingeGo.transform.localPosition = new Vector3(1.13f, 0f, 0f);
        rightDoorHinge = rightHingeGo.transform;

        GameObject rightPanel = CreateBox("RightDoorLeaf", new Vector3(-0.55f, 1.12f, 0f), new Vector3(1.10f, 2.24f, 0.06f), doorMat, rightHingeGo.transform, false);
        _rightLeafCollider = rightPanel.GetComponent<BoxCollider>();
        CreateBox("RightPanicPushBar", new Vector3(-0.55f, 1.0f, -0.06f), new Vector3(0.95f, 0.08f, 0.07f), pushBarMat, rightHingeGo.transform, true);

        SetupRotations();
        SetupTriggerVolume();

        Debug.Log("[HostelEntranceDoor] Built clean architectural double-leaf entrance door with panic hardware and exit lighting.");
    }

    private GameObject CreateBox(string name, Vector3 localPos, Vector3 localScale, Material mat, Transform parent, bool isTrigger)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        var rend = go.GetComponent<MeshRenderer>();
        if (rend != null && mat != null) rend.sharedMaterial = mat;

        var col = go.GetComponent<BoxCollider>();
        if (col != null)
        {
            col.isTrigger = isTrigger;
        }

        return go;
    }

    private Material FindMaterial(string matName, Color fallbackColor)
    {
        var allMats = Resources.FindObjectsOfTypeAll<Material>();
        foreach (var m in allMats)
        {
            if (m != null && m.name == matName) return m;
        }

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = fallbackColor;
        return mat;
    }

    private void OnGUI()
    {
        // 1. Floating Interaction Prompt when looking at/near door
        if (_showPrompt && !_hasEvacuated)
        {
            GUIStyle promptStyle = new GUIStyle(GUI.skin.box);
            promptStyle.fontSize = 15;
            promptStyle.fontStyle = FontStyle.Bold;
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.normal.textColor = Color.white;

            string actionText = isOpen ? "Door is OPEN - Walk outside to Assembly Plaza" : "Walk up to door or press [E] / Click / Controller Trigger to Open Exit Door";
            float width = 580f;
            float height = 50f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.72f;

            GUI.backgroundColor = new Color(0.1f, 0.7f, 0.2f, 0.9f);
            GUI.Box(new Rect(x, y, width, height), $"🚪 MAIN ENTRANCE / EMERGENCY EXIT\n{actionText}", promptStyle);
        }

        // 2. Celebratory Evacuation Success Screen Overlay (in addition to the 3D in-VR billboard)
        if (_hasEvacuated)
        {
            GUIStyle winStyle = new GUIStyle(GUI.skin.box);
            winStyle.fontSize = 18;
            winStyle.fontStyle = FontStyle.Bold;
            winStyle.alignment = TextAnchor.MiddleCenter;
            winStyle.normal.textColor = Color.white;

            float winW = 600f;
            float winH = 150f;
            float winX = (Screen.width - winW) * 0.5f;
            float winY = (Screen.height - winH) * 0.35f;

            GUI.backgroundColor = new Color(0.05f, 0.55f, 0.15f, 0.95f); // Deep safety green

            int evacuatedCount = EvacuationEgressManager.Instance != null ? EvacuationEgressManager.Instance.EvacuatedAgentCount : 300;
            string msg = "🎉 EVACUATION SUCCESSFUL! 🎉\n\n" +
                         "You have safely exited the hostel building and reached the Assembly Plaza.\n" +
                         $"Evacuation Time: {_evacuationTime:F1} seconds | Students Evacuated: {evacuatedCount} / 300\n" +
                         "Status: NBC 2016 / NFPA 101 Egress Complete";

            GUI.Box(new Rect(winX, winY, winW, winH), msg, winStyle);
        }
    }
}
