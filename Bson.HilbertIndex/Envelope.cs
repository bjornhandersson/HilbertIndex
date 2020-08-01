using System;

namespace Bson.HilbertIndex
{
    public class Envelope
    {
        public Envelope(double minX, double maxX, double minY, double maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public double MinX { get; }

        public double MaxX { get; }

        public double MinY { get; }

        public double MaxY { get; }


        public Envelope Expand(Coordinate position)
            => Expand(position.X, position.Y);
            
        public Envelope Expand(double x, double y)
            => new Envelope(
                x < MinX ? x : MinX,
                x > MaxX ? x : MaxX,
                y < MinY ? y : MinY,
                y > MaxY ? y : MaxY
            );
    }
}