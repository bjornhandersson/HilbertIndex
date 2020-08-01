using System;
namespace Bson.HilbertIndex
{
    /// <summary>
    /// Box in 2d space
    /// </summary>
    public struct HilbertEnvelope
    {
        private readonly int _x;
        private readonly int _y;
        private readonly int _p;
        private readonly int _q;


        /// <summary>
        /// Create a new box in hilbert coordinate system
        /// </summary>
        /// <param name="x">Lower left X</param>
        /// <param name="y">Lower left Y</param>
        /// <param name="p">Height</param>
        /// <param name="q">Width</param>
        public HilbertEnvelope(int x, int y, int p, int q)
        {
            _x = x;
            _y = y;
            _p = Math.Max(1, p);
            _q = Math.Max(1, q);
        }

        public int MaxX { get { return _x + _q; } }

        public int MaxY { get { return _y + _p; } }

        public int MinX { get { return _x; } }

        public int MinY { get { return _y; } }

        public int Height { get { return _p; } }

        public int Width { get { return _q; } }

        public override string ToString()
            => $"xy:{_x},{_y} p:{_p},q:{_q}";

        public override int GetHashCode()
            => (_x << _y) ^ (_p << _q);

        public override bool Equals(object obj)
            => obj is HilbertEnvelope && obj.GetHashCode() == this.GetHashCode();
    }
}