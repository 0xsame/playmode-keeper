using UnityEngine;

namespace UnityEngine.Rendering
{
    // Stand-in for the render pipelines' interface, which the package looks up by name.
    public interface IAdditionalData
    {
    }
}

namespace PlayModeKeeper.Tests
{
    [RequireComponent(typeof(Light))]
    public class KeeperTestAdditionalData : MonoBehaviour, UnityEngine.Rendering.IAdditionalData
    {
        public int Quality;
    }
}
