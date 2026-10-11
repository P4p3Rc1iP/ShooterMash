using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>Marks the GameObject of a placed piece, so raycasts and ground checks can tell pieces from terrain.</summary>
    [DisallowMultipleComponent]
    public class BuildPieceTag : MonoBehaviour
    {
        public int id;
    }
}
