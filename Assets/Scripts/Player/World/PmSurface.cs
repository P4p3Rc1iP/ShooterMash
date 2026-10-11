using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Marks a collider's surface for player movement: ladder, slick, no fall damage, footstep material.
    /// Applies to colliders on this GameObject and its children.
    /// </summary>
    [DisallowMultipleComponent]
    public class PmSurface : MonoBehaviour
    {
        public SurfaceFlags flags;
        public SurfaceType surfaceType;
    }
}
