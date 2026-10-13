using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Scrap Mechanic's lift: a platform you put on the ground. Whatever is built on it becomes a
    /// vehicle, frozen in place while it's on the lift; the platform can be raised and lowered;
    /// a vehicle anywhere (even upside down) can be put back on it, upright. Taking the lift
    /// away drops the vehicle and lets it drive. As a build host it forwards to the vehicle it
    /// holds, or starts a new one with the first piece built on it.
    /// </summary>
    public class Lift : BuildHost, IRedirectingHost
    {
        /// <summary>Platform size (m): width (x) by length (z).</summary>
        public static readonly Vector2 PlatformSize = new Vector2(4f, 6f);
        public const float Thickness = 0.2f;
        public const float MinHeight = 0.5f, MaxHeight = 3f, HeightStep = 0.5f, DefaultHeight = 1f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        StructureManager look;
        Transform top, platform, column;
        Vector3 vehicleOffset;   // the vehicle's origin in the platform top's space

        /// <summary>Height of the platform's top above the ground (m).</summary>
        public float Height { get; private set; } = DefaultHeight;
        public Vehicle Vehicle { get; private set; }
        public Transform Top => top;

        public BuildHost Target => Vehicle;
        public override Transform Frame => Vehicle != null ? Vehicle.Frame : top;
        public override BuildSystem System => Vehicle != null ? Vehicle.System : null;
        public override PieceViews Views => Vehicle != null ? Vehicle.Views : null;

        public static Lift Create(StructureManager look, string name = "Lift")
        {
            var go = new GameObject(name);
            var lift = go.AddComponent<Lift>();
            lift.Init(look);
            return lift;
        }

        void Init(StructureManager structures)
        {
            look = structures;
            top = new GameObject("Top").transform;
            top.SetParent(transform, false);

            platform = Part("Platform", true);
            column = Part("Column", false);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, new Color(0.85f, 0.65f, 0.15f)); // hazard yellow
            platform.GetComponent<Renderer>().SetPropertyBlock(block);
            block.SetColor(BaseColor, new Color(0.3f, 0.3f, 0.32f));
            column.GetComponent<Renderer>().SetPropertyBlock(block);
            Layout();
        }

        Transform Part(string name, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = BuildMeshes.Box(Vector3.one);
            go.AddComponent<MeshRenderer>().sharedMaterial = look.MaterialFor(BuildMaterial.Iron);
            if (collider)
                go.AddComponent<BoxCollider>();
            return go.transform;
        }

        void Layout()
        {
            top.localPosition = new Vector3(0f, Height, 0f);
            platform.localPosition = new Vector3(0f, Height - Thickness / 2f, 0f);
            platform.localScale = new Vector3(PlatformSize.x, Thickness, PlatformSize.y);
            float columnHeight = Height - Thickness;
            column.localPosition = new Vector3(0f, columnHeight / 2f, 0f);
            column.localScale = new Vector3(0.4f, columnHeight, 0.4f);
            SyncVehicle();
            Physics.SyncTransforms();
        }

        /// <summary>Put the lift's base at a point on the ground, facing a yaw. A vehicle on it comes along.</summary>
        public void Place(Vector3 ground, float yaw)
        {
            transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
            Layout();
        }

        public void SetHeight(float height)
        {
            Height = Mathf.Clamp(Mathf.Round(height / HeightStep) * HeightStep, MinHeight, MaxHeight);
            Layout();
        }

        /// <summary>Put a vehicle on the lift, upright and centred, resting on its lowest point.</summary>
        public void Hold(Vehicle v)
        {
            if (Vehicle != null && Vehicle != v)
                Release();
            if (v.Lift != null && v.Lift != this)
                v.Lift.Release();
            Vehicle = v;
            Bounds b = v.LocalBounds();
            vehicleOffset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            v.AttachTo(this);
            SyncVehicle();
        }

        /// <summary>After building: raise the vehicle if something now hangs below the platform (e.g. wheels).</summary>
        internal void Fit()
        {
            if (Vehicle == null)
                return;
            float lowest = Vehicle.LocalBounds().min.y;
            if (vehicleOffset.y + lowest < 0f)
            {
                vehicleOffset.y = -lowest;
                SyncVehicle();
            }
        }

        /// <summary>Let the vehicle go: it drops off and becomes a physics object.</summary>
        public void Release()
        {
            if (Vehicle == null)
                return;
            Vehicle v = Vehicle;
            Vehicle = null;
            v.Detach();
        }

        /// <summary>Take the lift away. A vehicle on it drops.</summary>
        public void Remove()
        {
            Release();
            Destroy(gameObject);
            platform.GetComponent<Collider>().enabled = false; // out of the physics scene now, not at end of frame
        }

        /// <summary>The vehicle on it was emptied and is going away.</summary>
        internal void Forget(Vehicle v)
        {
            if (Vehicle == v)
                Vehicle = null;
        }

        void SyncVehicle()
        {
            if (Vehicle != null)
                Vehicle.SetPose(top.TransformPoint(vehicleOffset), top.rotation);
        }

        /// <summary>Does a box (in the world) rest on the platform's top?</summary>
        public bool Supports(Obb worldBox, float tolerance)
        {
            Quaternion inverse = Quaternion.Inverse(top.rotation);
            var local = new Obb
            {
                center = inverse * (worldBox.center - top.position),
                rotation = inverse * worldBox.rotation,
                halfExtents = worldBox.halfExtents,
                cut = worldBox.cut,
            };
            Bounds b = local.Bounds;
            Vector2 half = PlatformSize / 2f;
            return b.min.y >= -tolerance && b.min.y <= tolerance
                && b.max.x > -half.x && b.min.x < half.x
                && b.max.z > -half.y && b.min.z < half.y;
        }

        // ------------------------------------------------------------------ building

        public override PlacementCheck CheckPlace(int type, BuildMaterial material, Vector3 position, Quaternion rotation)
        {
            if (Vehicle != null)
                return Vehicle.CheckPlace(type, material, position, rotation);

            // An empty lift: the first piece of a new vehicle has to rest on the platform.
            PieceDef def = PieceCatalog.Get(type);
            if (def == null)
                return new PlacementCheck { failure = BuildFailure.UnknownPiece };
            if (!def.ComesIn(material))
                return new PlacementCheck { failure = BuildFailure.WrongMaterial };
            bool rests = false;
            foreach (Obb box in def.boxes)
            {
                Obb world = box.Transformed(top.TransformPoint(position), top.rotation * rotation);
                if (Supports(world, look.config.groundTolerance))
                    rests = true;
                foreach (Bounds actor in look.World.Actors)
                    if (Obb.Overlap(world, new Obb(actor.center, actor.size), 0f))
                        return new PlacementCheck { failure = BuildFailure.Blocked };
            }
            if (!rests)
                return new PlacementCheck { failure = BuildFailure.Unsupported };
            float max = look.config.Stats(material).maxSupport;
            return new PlacementCheck { support = max, grounded = true };
        }

        public override BuildResult Execute(in BuildCmd cmd)
        {
            if (Vehicle != null)
                return Vehicle.Execute(cmd);
            if (cmd.action != BuildAction.Place)
                return BuildResult.Fail(BuildFailure.NotFound);

            PlacementCheck check = CheckPlace(cmd.pieceType, cmd.material, cmd.position, cmd.rotation);
            if (!check.Valid)
                return BuildResult.Fail(check.failure);

            // The first piece: start a vehicle whose origin is the platform's top centre.
            Vehicle v = Vehicle.Create(look, top);
            Vehicle = v;
            vehicleOffset = Vector3.zero;
            v.AttachTo(this);
            SyncVehicle();
            BuildResult r = v.Execute(cmd);
            if (!r.Ok)
            {
                Vehicle = null;
                Destroy(v.gameObject);
            }
            return r;
        }
    }
}
