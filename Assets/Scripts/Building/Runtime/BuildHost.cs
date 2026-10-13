using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Something pieces are built on: the world (StructureManager), a vehicle, or a lift waiting
    /// for one. The build tool aims at a collider and builds on the host it belongs to. Positions
    /// and rotations in its BuildCmds, snapping and piece records are local to its Frame.
    /// </summary>
    public abstract class BuildHost : MonoBehaviour
    {
        /// <summary>The space pieces are placed in.</summary>
        public virtual Transform Frame => transform;

        /// <summary>The build simulation, or null when nothing's built yet (an empty lift).</summary>
        public abstract BuildSystem System { get; }

        /// <summary>The pieces' GameObjects, or null when there are none.</summary>
        public abstract PieceViews Views { get; }

        /// <summary>Why nothing can be built or removed here right now, or None.</summary>
        public virtual BuildFailure CanBuild => BuildFailure.None;

        /// <summary>Would this placement (in Frame space) be accepted? For the ghost preview.</summary>
        public virtual PlacementCheck CheckPlace(int type, BuildMaterial material, Vector3 position, Quaternion rotation) =>
            CanBuild != BuildFailure.None
                ? new PlacementCheck { failure = CanBuild }
                : System.CheckPlace(type, material, position, rotation);

        public virtual BuildResult Execute(in BuildCmd cmd) =>
            CanBuild != BuildFailure.None ? BuildResult.Fail(CanBuild) : System.Execute(cmd);
    }

    /// <summary>A host that builds on another one: a lift builds on the vehicle it holds. Undo goes to the target.</summary>
    public interface IRedirectingHost
    {
        BuildHost Target { get; }
    }
}
