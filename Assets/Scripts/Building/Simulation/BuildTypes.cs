using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    public enum BuildMaterial : byte
    {
        Wood,
        Stone,
        Iron,
    }

    /// <summary>
    /// How a material carries load, Valheim style. A piece touching the ground gets maxSupport.
    /// Every other piece gets its best neighbour's support (capped at its own maxSupport), minus
    /// maxSupport * loss, where the loss blends between horizontal and vertical by the angle of
    /// the connection. Below minSupport the piece breaks.
    /// </summary>
    [Serializable]
    public struct MaterialStats
    {
        public float maxSupport;
        public float minSupport;
        [Tooltip("Fraction of maxSupport lost per piece when stacking up (or hanging down).")]
        public float verticalLoss;
        [Tooltip("Fraction of maxSupport lost per piece when building sideways.")]
        public float horizontalLoss;

        public MaterialStats(float maxSupport, float minSupport, float verticalLoss, float horizontalLoss)
        {
            this.maxSupport = maxSupport;
            this.minSupport = minSupport;
            this.verticalLoss = verticalLoss;
            this.horizontalLoss = horizontalLoss;
        }
    }

    [Serializable]
    public class BuildConfig
    {
        [Header("Features")]
        [Tooltip("Pieces need support and collapse without it. Off: everything floats.")]
        public bool structuralIntegrity = true;
        [Tooltip("Players can undo and redo their own recent build actions.")]
        public bool undo = true;

        [Header("Materials (Valheim values)")]
        public MaterialStats wood = new MaterialStats(100f, 10f, 0.125f, 0.2f);
        public MaterialStats stone = new MaterialStats(1000f, 100f, 0.125f, 1f);
        public MaterialStats iron = new MaterialStats(1500f, 20f, 0.077f, 0.077f);

        [Header("Geometry (m)")]
        [Tooltip("Pieces closer than this count as touching each other.")]
        public float connectTolerance = 0.02f;
        [Tooltip("Pieces closer than this to the terrain or the static world count as grounded.")]
        public float groundTolerance = 0.05f;

        [Header("Undo")]
        [Tooltip("Seconds an action stays undoable.")]
        public float undoWindow = 60f;
        public int undoDepth = 50;

        public MaterialStats Stats(BuildMaterial m) => m switch
        {
            BuildMaterial.Stone => stone,
            BuildMaterial.Iron => iron,
            _ => wood,
        };
    }

    /// <summary>The persistent, networkable description of one placed piece.</summary>
    [Serializable]
    public struct PieceRecord
    {
        public int id;
        public int type;              // index into PieceCatalog.All
        public BuildMaterial material;
        public Vector3 position;      // pivot: bottom centre of the piece
        public Quaternion rotation;
        public int owner;
    }

    public enum BuildAction : byte
    {
        Place,
        Remove,
        Undo,
        Redo,
    }

    /// <summary>A build request, like PlayerCmd is for movement. The client sends it, the server runs it.</summary>
    public struct BuildCmd
    {
        public BuildAction action;
        public int owner;
        public int pieceType;
        public BuildMaterial material;
        public Vector3 position;
        public Quaternion rotation;
        public int targetId;          // Remove
        public double time;           // seconds, for the undo window

        public static BuildCmd Place(int owner, int type, BuildMaterial material, Vector3 position, Quaternion rotation, double time) =>
            new BuildCmd { action = BuildAction.Place, owner = owner, pieceType = type, material = material, position = position, rotation = rotation, time = time };

        public static BuildCmd Remove(int owner, int targetId, double time) =>
            new BuildCmd { action = BuildAction.Remove, owner = owner, targetId = targetId, time = time };

        public static BuildCmd Undo(int owner, double time) => new BuildCmd { action = BuildAction.Undo, owner = owner, time = time };
        public static BuildCmd Redo(int owner, double time) => new BuildCmd { action = BuildAction.Redo, owner = owner, time = time };
    }

    public enum BuildFailure : byte
    {
        None,
        UnknownPiece,
        Blocked,          // would overlap a player
        Unsupported,      // would collapse straight away
        NotFound,
        NothingToUndo,
        NothingToRedo,
        UndoExpired,
        StructureChanged, // someone else changed it since, or undoing would knock other pieces down
        Disabled,
    }

    /// <summary>What a command (or a terrain change) did to the world.</summary>
    public sealed class BuildResult
    {
        public BuildFailure failure;
        public readonly List<int> added = new List<int>();
        public readonly List<int> removed = new List<int>();   // removed on purpose
        public readonly List<int> collapsed = new List<int>(); // lost support and broke

        public bool Ok => failure == BuildFailure.None;
        public bool Changed => added.Count > 0 || removed.Count > 0 || collapsed.Count > 0;

        public static BuildResult Fail(BuildFailure failure) => new BuildResult { failure = failure };
    }

    public struct PlacementCheck
    {
        public BuildFailure failure;
        public float support;
        public bool grounded;

        public bool Valid => failure == BuildFailure.None;
    }

    /// <summary>
    /// What the build simulation needs from the world: is a piece resting on terrain (or other
    /// static, non-buildable geometry), and is the space taken by a player. The voxel world can
    /// implement this directly.
    /// </summary>
    public interface IBuildWorld
    {
        bool IsGrounded(Obb[] boxes, float tolerance);
        bool IsBlocked(Obb[] boxes);
    }
}
