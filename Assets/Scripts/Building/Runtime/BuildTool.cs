using System.Collections.Generic;
using ShooterMashup.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterMashup.Building
{
    /// <summary>
    /// The player's build tool, Valheim's hammer. B takes it out. Shows a ghost of the selected
    /// piece where you aim, snaps it to nearby pieces, and sends BuildCmds to the StructureManager.
    /// Holding right mouse shows what would collapse; releasing removes the piece (or digs).
    /// Runs after PlayerView so the ghost follows this frame's camera.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlayerMotor))]
    public class BuildTool : MonoBehaviour
    {
        [Tooltip("Who this player is, for undo. Becomes the client id once there's networking.")]
        public int ownerId;
        [Tooltip("How far away you can build (m).")]
        public float reach = 8f;
        [Tooltip("How close (m) a snap point must be to the aim to snap.")]
        public float snapRadius = 1f;
        [Tooltip("Rotation snap settings that R cycles through (degrees), followed by free rotation.")]
        public float[] rotationSteps = { 90f, 45f, 22.5f, 15f };
        [Tooltip("Degrees per scroll notch with rotation snapping off.")]
        public float freeRotationStep = 5f;
        [Tooltip("How much of a piece may end up inside another when snapping picks the snap point (0..1).")]
        [Range(0f, 1f)] public float autoOverlapLimit = BuildSnapping.DefaultOverlapLimit;
        [Tooltip("The same, when you picked the snap point yourself with Q/E. Only near-duplicates are refused.")]
        [Range(0f, 1f)] public float manualOverlapLimit = 0.95f;
        [Tooltip("Found automatically if left empty.")]
        public StructureManager structures;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly string[] MaterialNames = { "Wood", "Stone", "Iron" };

        public bool Equipped { get; private set; }

        PlayerMotor motor;
        LocalPlayerInput input;
        Camera cam;
        MaterialPropertyBlock block;

        bool menuOpen;
        int selected;
        BuildMaterial material;
        float yaw;
        int rotationMode = 2; // index into rotationSteps; rotationSteps.Length means free
        int heldSnap = -1;    // -1: pick the best snap point automatically
        PieceThumbnails thumbnails;

        GameObject ghost;
        Renderer[] ghostRenderers;
        int ghostType = -1;
        GameObject marker;

        bool hasPlacement;
        Vector3 placePosition;
        Quaternion placeRotation;
        PlacementCheck check;
        SnapResult snap;

        bool removing;
        int removeTarget = -1;
        DiggableBlock digTarget;
        readonly List<int> wouldCollapse = new List<int>();

        bool cursorWasLocked;
        string message = "";
        float messageTime = -10f;
        GUIStyle style, bigStyle, buttonStyle, selectedStyle;

        BuildSystem Sim => structures.System;
        PieceDef Selected => PieceCatalog.All[selected];
        bool RotationSnapped => rotationMode < rotationSteps.Length;
        float RotationStep => RotationSnapped ? rotationSteps[rotationMode] : freeRotationStep;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<LocalPlayerInput>();
            cam = GetComponentInChildren<Camera>();
            block = new MaterialPropertyBlock();
        }

        void Start()
        {
            if (structures == null)
                structures = FindAnyObjectByType<StructureManager>();
            if (structures == null)
                structures = new GameObject("Structures").AddComponent<StructureManager>();
            thumbnails = new PieceThumbnails(structures);
            rotationMode = Mathf.Clamp(rotationMode, 0, rotationSteps.Length);
        }

        void OnDisable() => SetEquipped(false);

        void OnDestroy() => thumbnails?.Dispose();

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || structures == null)
                return;

            if (kb.bKey.wasPressedThisFrame && !menuOpen)
                SetEquipped(!Equipped);
            if (!Equipped)
                return;

            UpdateActors();

            // Build menu.
            if (menuOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
                    CloseMenu();
                else
                    thumbnails.Prepare(material); // the material can change in the menu
                return;
            }
            bool locked = LocalPlayerInput.CursorLocked;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool hit = Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit aim, reach,
                structures.solidMask, QueryTriggerInteraction.Ignore);

            if (locked && mouse.middleButton.wasPressedThisFrame)
            {
                if (shift)
                {
                    PickAimedPiece(hit, aim); // Shift + middle click: copy the piece you look at
                }
                else
                {
                    OpenMenu();
                    return;
                }
            }

            if (locked)
                HandleKeys(kb, mouse);

            // Removing: hold right mouse to see what would fall, release to remove.
            if (locked && mouse.rightButton.isPressed)
            {
                removing = true;
                HideGhost();
                UpdateRemoveTarget(hit, aim);
            }
            else
            {
                if (removing)
                {
                    if (locked) CommitRemove();
                    removing = false;
                    removeTarget = -1;
                    digTarget = null;
                    structures.ClearRemovePreview();
                }
                UpdateGhost(hit, aim, shift);

                // A click that recaptured the cursor shouldn't also place a piece.
                if (locked && cursorWasLocked && mouse.leftButton.wasPressedThisFrame)
                    Place();
            }

            cursorWasLocked = locked;
        }

        // ------------------------------------------------------------------ input

        void HandleKeys(Keyboard kb, Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                yaw = Mathf.Repeat(yaw + Mathf.Sign(scroll) * RotationStep, 360f);

            if (kb.rKey.wasPressedThisFrame)
            {
                rotationMode = (rotationMode + 1) % (rotationSteps.Length + 1);
                if (RotationSnapped)
                    yaw = Mathf.Repeat(Mathf.Round(yaw / RotationStep) * RotationStep, 360f);
                Say(RotationSnapped ? $"rotation snaps to {RotationStep}°" : $"free rotation ({freeRotationStep}° per notch)");
            }

            int snapCount = Selected.snapPoints.Length;
            if (kb.eKey.wasPressedThisFrame)
                heldSnap = heldSnap + 1 >= snapCount ? -1 : heldSnap + 1;
            if (kb.qKey.wasPressedThisFrame)
                heldSnap = heldSnap - 1 < -1 ? snapCount - 1 : heldSnap - 1;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame)
                Report(Sim.Execute(shift ? BuildCmd.Redo(ownerId, Time.timeAsDouble) : BuildCmd.Undo(ownerId, Time.timeAsDouble)), shift ? "redone" : "undone");
            else if (ctrl && kb.yKey.wasPressedThisFrame)
                Report(Sim.Execute(BuildCmd.Redo(ownerId, Time.timeAsDouble)), "redone");
        }

        void SetEquipped(bool on)
        {
            if (Equipped == on)
                return;
            Equipped = on;
            if (input != null) input.leanEnabled = !on;
            if (structures != null)
            {
                structures.ShowSupport = on;
                structures.ClearRemovePreview();
            }
            if (!on)
            {
                if (menuOpen) CloseMenu();
                HideGhost();
                removing = false;
                removeTarget = -1;
            }
        }

        void OpenMenu()
        {
            menuOpen = true;
            input.uiOpen = true;
            HideGhost();
            thumbnails.Prepare(material);
            LocalPlayerInput.SetCursorLocked(false);
        }

        void CloseMenu()
        {
            menuOpen = false;
            input.uiOpen = false;
            LocalPlayerInput.SetCursorLocked(true);
        }

        void Select(int type)
        {
            if (type != selected)
                heldSnap = -1;
            selected = type;
            CloseMenu();
        }

        /// <summary>Take the shape, material and world orientation of the piece you're looking at.</summary>
        void PickAimedPiece(bool hit, RaycastHit aim)
        {
            BuildPieceTag tag = hit ? aim.collider.GetComponentInParent<BuildPieceTag>() : null;
            Piece p = tag != null ? Sim.Get(tag.id) : null;
            if (p == null)
            {
                Say("aim at a piece to copy it");
                return;
            }
            if (p.record.type != selected)
                heldSnap = -1;
            selected = p.record.type;
            material = p.record.material;
            yaw = Mathf.Repeat(Mathf.Round(p.record.rotation.eulerAngles.y * 100f) / 100f, 360f);
            Say($"copied {MaterialNames[(int)material]} {Selected.name}, rotation {yaw:0.#}°");
        }

        /// <summary>Players block placement. Their hull, a touch smaller so building right next to you works.</summary>
        void UpdateActors()
        {
            List<Bounds> actors = structures.World.Actors;
            actors.Clear();
            PlayerState s = motor.State;
            MoveConfig m = motor.movement;
            Vector3 maxs = m.maxs;
            if (s.IsProne) maxs.y = m.ProneMaxY;
            else if (s.IsDucked) maxs.y = m.CrouchMaxY;
            var hull = new Bounds();
            hull.SetMinMax(s.origin + m.mins, s.origin + maxs);
            hull.Expand(-0.04f);
            actors.Add(hull);
        }

        // ------------------------------------------------------------------ placing

        void UpdateGhost(bool hit, RaycastHit aim, bool free)
        {
            hasPlacement = false;
            if (!hit)
            {
                HideGhost();
                return;
            }

            PieceDef def = Selected;
            Quaternion userRotation = Quaternion.Euler(0f, yaw, 0f);
            placePosition = aim.point;
            placeRotation = userRotation;
            snap = default;
            if (!free)
            {
                snap = BuildSnapping.Snap(Sim, def, aim.point, aim.normal, userRotation, heldSnap, snapRadius,
                    heldSnap >= 0 ? manualOverlapLimit : autoOverlapLimit);
                if (snap.snapped)
                {
                    placePosition = snap.position;
                    placeRotation = snap.rotation;
                }
                else if (heldSnap >= 0)
                {
                    // Nothing in reach: the chosen snap point follows the aim, like it will when it snaps.
                    placePosition = aim.point - userRotation * def.snapPoints[heldSnap];
                }
            }
            check = Sim.CheckPlace(selected, material, placePosition, placeRotation);
            hasPlacement = true;

            EnsureGhost();
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(placePosition, placeRotation);
            // The material's look, leaning green when it can go there and red when it can't.
            Color tint = Color.Lerp(structures.ColorFor(material), check.Valid ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.2f, 0.15f),
                check.Valid ? 0.35f : 0.7f);
            tint.a = 0.55f;
            Tint(ghostRenderers, tint, structures.TextureFor(material));

            // Show which snap point is in use.
            Vector3? point = snap.snapped ? snap.point
                : heldSnap >= 0 ? placePosition + placeRotation * def.snapPoints[heldSnap]
                : (Vector3?)null;
            marker.SetActive(point.HasValue);
            if (point.HasValue)
                marker.transform.position = point.Value;
        }

        void Place()
        {
            if (!hasPlacement)
                return;
            if (!check.Valid)
            {
                Say("can't place: " + Describe(check.failure));
                return;
            }
            BuildResult result = Sim.Execute(BuildCmd.Place(ownerId, selected, material, placePosition, placeRotation, Time.timeAsDouble));
            if (!result.Ok)
                Say("can't place: " + Describe(result.failure));
        }

        void EnsureGhost()
        {
            if (ghost != null && ghostType == selected)
                return;
            if (ghost != null)
                Destroy(ghost);
            ghost = structures.CreatePieceObject(Selected, structures.GhostMaterial, false, "Build ghost");
            ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in ghostRenderers)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghostType = selected;

            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Snap point";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.localScale = Vector3.one * 0.1f;
                var r = marker.GetComponent<Renderer>();
                r.sharedMaterial = structures.GhostMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Tint(new[] { r }, new Color(1f, 0.9f, 0.1f, 0.9f));
            }
        }

        void HideGhost()
        {
            hasPlacement = false;
            if (ghost != null) ghost.SetActive(false);
            if (marker != null) marker.SetActive(false);
        }

        void Tint(Renderer[] renderers, Color color, Texture texture = null)
        {
            block.SetColor(BaseColor, color);
            block.SetTexture(BaseMap, texture != null ? texture : Texture2D.whiteTexture);
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ removing

        void UpdateRemoveTarget(bool hit, RaycastHit aim)
        {
            BuildPieceTag piece = hit ? aim.collider.GetComponentInParent<BuildPieceTag>() : null;
            digTarget = hit && piece == null ? aim.collider.GetComponentInParent<DiggableBlock>() : null;

            int id = piece != null ? piece.id : -1;
            if (id == removeTarget)
                return;
            removeTarget = id;
            if (id >= 0)
            {
                Sim.PreviewRemove(id, wouldCollapse);
                structures.SetRemovePreview(id, wouldCollapse);
            }
            else
            {
                wouldCollapse.Clear();
                structures.ClearRemovePreview();
            }
        }

        void CommitRemove()
        {
            if (removeTarget >= 0)
            {
                BuildResult result = Sim.Execute(BuildCmd.Remove(ownerId, removeTarget, Time.timeAsDouble));
                Report(result, "removed");
            }
            else if (digTarget != null)
            {
                BuildResult result = structures.Dig(digTarget);
                Say(result.collapsed.Count > 0 ? $"dug; {result.collapsed.Count} pieces collapsed" : "dug");
            }
        }

        // ------------------------------------------------------------------ feedback

        void Report(BuildResult result, string what)
        {
            if (!result.Ok)
                Say(Describe(result.failure));
            else if (result.collapsed.Count > 0)
                Say($"{what}; {result.collapsed.Count} pieces collapsed");
            else
                Say(what);
        }

        void Say(string text)
        {
            message = text;
            messageTime = Time.time;
        }

        static string Describe(BuildFailure failure) => failure switch
        {
            BuildFailure.Blocked => "something is in the way",
            BuildFailure.Unsupported => "not enough support",
            BuildFailure.NotFound => "nothing there",
            BuildFailure.NothingToUndo => "nothing to undo",
            BuildFailure.NothingToRedo => "nothing to redo",
            BuildFailure.UndoExpired => "too long ago to undo",
            BuildFailure.StructureChanged => "can't undo: the structure has changed since",
            BuildFailure.Disabled => "undo is turned off",
            _ => failure.ToString(),
        };

        void OnGUI()
        {
            if (!Equipped)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.LowerCenter };
                style.normal.textColor = Color.white;
                bigStyle = new GUIStyle(style) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, wordWrap = true, imagePosition = ImagePosition.ImageAbove };
                selectedStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold };
                selectedStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                selectedStyle.hover.textColor = selectedStyle.normal.textColor;
                // Selected tile: the pressed look, so it stands out.
                selectedStyle.normal.background = GUI.skin.button.active.background;
            }

            if (menuOpen)
            {
                DrawMenu();
                return;
            }

            PieceDef def = Selected;
            string snapText = heldSnap < 0 ? "auto" : $"{heldSnap + 1}/{def.snapPoints.Length}";
            string status;
            if (removing)
            {
                Piece p = removeTarget >= 0 ? Sim.Get(removeTarget) : null;
                status = p != null ? $"release to remove {p.record.material} {p.def.name}" +
                                     (wouldCollapse.Count > 0 ? $"  <color=#f6a>({wouldCollapse.Count} more would collapse)</color>" : "")
                    : digTarget != null ? "release to dig"
                    : "aim at a piece to remove it";
            }
            else if (!hasPlacement)
                status = "aim at a surface";
            else if (!check.Valid)
                status = $"<color=#f66>{Describe(check.failure)}</color>";
            else
            {
                MaterialStats stats = Sim.Config.Stats(material);
                status = check.grounded ? "grounded" : $"support {Mathf.Max(0f, check.support):0} / {stats.maxSupport:0}";
            }

            string text =
                $"<b>{MaterialNames[(int)material]} {def.name}</b>   snap point {snapText}   " +
                $"rotation {yaw:0.#}° ({(RotationSnapped ? $"{RotationStep}° steps" : "free")})\n" +
                status + "\n" +
                (Time.time - messageTime < 2.5f ? $"<color=#ffd966>{message}</color>" : "") + "\n" +
                "<color=#aaa>LMB place, hold RMB remove/dig, MMB menu, scroll rotate, R rotation steps, Q/E snap point,\n" +
                "Shift free placement, Shift+MMB copy the piece you aim at, Ctrl+Z undo, Ctrl+Y redo, B put away</color>";
            GUI.Label(new Rect(0, Screen.height - 250, Screen.width, 120), text, style);
        }

        void DrawMenu()
        {
            const int columns = 6;
            const float tile = 132f, tileHeight = 158f;
            int rows = (PieceCatalog.All.Length + columns - 1) / columns;
            float width = columns * (tile + 4f) + 32f, height = rows * (tileHeight + 4f) + 130f;
            var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(area, "");
            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, width - 32, height - 24));
            GUILayout.Label("<b>Build</b>", bigStyle);
            GUILayout.Space(6);
            material = (BuildMaterial)GUILayout.Toolbar((int)material, MaterialNames, buttonStyle, GUILayout.Height(32));
            GUILayout.Space(10);

            for (int i = 0; i < PieceCatalog.All.Length; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + columns, PieceCatalog.All.Length); j++)
                {
                    var content = new GUIContent(PieceCatalog.All[j].name, thumbnails.Get(j, material));
                    if (GUILayout.Button(content, j == selected ? selectedStyle : buttonStyle, GUILayout.Width(tile), GUILayout.Height(tileHeight)))
                        Select(j);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#aaa>Middle click or Esc to close</color>", style);
            GUILayout.EndArea();
        }
    }
}
