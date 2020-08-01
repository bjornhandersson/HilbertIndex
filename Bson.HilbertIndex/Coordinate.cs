using System;

namespace Bson.HilbertIndex
{
    public class Coordinate
    {
        public Coordinate(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }
}