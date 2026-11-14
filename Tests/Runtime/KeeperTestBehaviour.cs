using System;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    [Flags]
    public enum KeeperTestFlags
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    public interface IKeeperTestShape
    {
    }

    [Serializable]
    public class KeeperTestCircle : IKeeperTestShape
    {
        public float Radius;
    }

    [Serializable]
    public class KeeperTestBox : IKeeperTestShape
    {
        public Vector2 Size;
    }

    // In its own assembly: Unity does not add a script from an editor assembly to a GameObject.
    public class KeeperTestBehaviour : MonoBehaviour
    {
        public float Visible;

        [HideInInspector]
        public float Hidden;

        public KeeperTestFlags Flags;
        public AnimationCurve Curve = new AnimationCurve();
        public Gradient Gradient = new Gradient();
        public int[] Numbers = new int[0];
        public ulong Big;
        public UnityEngine.Object Reference;
        public UnityEngine.Object[] References = new UnityEngine.Object[0];

        [SerializeReference]
        public IKeeperTestShape Shape;
    }
}
