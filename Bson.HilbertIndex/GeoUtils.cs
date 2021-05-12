using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex
{
     internal static class GeoUtils
    {
        private static double Min(params double[] values) => values.Min();
        private static double Max(params double[] values) => values.Max();

        internal static class Wgs84
        {
            private const int EARTH_RADIOUS = 6366710;

            // Crete an envelope buffered around the given coordnate to the given distance in meter in true spheric distance
            public static Envelope Buffer(Coordinate coordinate, int meters)
            {
                // Todo: Do proper Mafs
                //  does not deal with datum line and can be more efficent without Min/Max functions
                var n = Move(coordinate, meters, 0);
                var e = Move(coordinate, meters, 90);
                var s = Move(coordinate, meters, 180);
                var w = Move(coordinate, meters, 270);
                return new Envelope(
                    Min(n.X, e.X, s.X, w.X),
                    Max(n.X, e.X, s.X, w.X),
                    Min(n.Y, e.Y, s.Y, w.Y),
                    Max(n.Y, e.Y, s.Y, w.Y)
                );
            }

            // Distance between two wgs84 coordinates 
            public static double Distance(Coordinate firs, Coordinate second)
                => DistanceRadians(firs, second) * EARTH_RADIOUS;

            public static Coordinate Move(Coordinate origin, double meters, double bearing)
            {
                // Todo: Improve precision here.
                // meters as fractions of radious of earth is... tiny.
                meters /= EARTH_RADIOUS;

                double e1 = origin.X * Math.PI / 180.0;
                double n1 = origin.Y * Math.PI / 180.0;

                bearing *= Math.PI / 180.0;

                double radY = Math.Asin(Math.Sin(n1) * Math.Cos(meters) + Math.Cos(n1) * Math.Sin(meters) * Math.Cos(bearing));
                double radX = Math.Atan2(Math.Sin(bearing) * Math.Sin(meters) * Math.Cos(n1),
                    Math.Cos(meters) - Math.Sin(n1) * Math.Sin(radY));

                double x = (e1 + radX) * 180.0 / Math.PI;
                double y = radY * 180.0 / Math.PI;

                return new Coordinate(x, y);
            }

            private static double DistanceRadians(Coordinate first, Coordinate second)
            {
                double e1 = first.X / 180.0 * Math.PI;
                double e2 = second.X / 180.0 * Math.PI;

                double n1 = first.Y / 180.0 * Math.PI;
                double n2 = second.Y / 180.0 * Math.PI;

                double sin_y = Math.Sin((n1 - n2) / 2.0);
                sin_y *= sin_y;

                double sin_x = Math.Sin((e1 - e2) / 2.0);
                sin_x *= sin_x;

                return 2.0 * Math.Asin(Math.Sqrt(sin_y + Math.Cos(n1) * Math.Cos(n2) * sin_x));
            }
        }
    }
}