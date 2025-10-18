using System;
using System.Linq;

namespace Bson.HilbertIndex
{
     internal static class GeoUtils
    {
        private static double Min(params double[] values) => values.Min();
        private static double Max(params double[] values) => values.Max();

        internal static class Wgs84
        {
            // WGS84 Earth radius in meters (more precise value)
            private const double EARTH_RADIUS = 6378137.0; // Equatorial radius
            private const double EARTH_RADIUS_POLAR = 6356752.314245; // Polar radius
            private const double EARTH_RADIUS_MEAN = 6371000.0; // Mean radius for distance calculations
            
            // Mathematical constants
            private const double DEG_TO_RAD = Math.PI / 180.0;
            private const double RAD_TO_DEG = 180.0 / Math.PI;
            private const double POLE_THRESHOLD = 1e-10;

            // Create an envelope buffered around the given coordinate to the given distance in meter in true spheric distance
            public static Envelope Buffer(Coordinate coordinate, int meters)
            {
                return BufferOptimized(coordinate, meters);
            }

            /// <summary>
            /// Create an envelope buffered around the given coordinate with proper date line handling and optimized calculation
            /// </summary>
            /// <param name="coordinate">Center coordinate</param>
            /// <param name="meters">Buffer distance in meters</param>
            /// <returns>Envelope with proper longitude bounds [-180, +180]</returns>
            public static Envelope BufferOptimized(Coordinate coordinate, int meters)
            {
                if (coordinate == null)
                    throw new ArgumentNullException(nameof(coordinate));
                if (meters < 0)
                    throw new ArgumentOutOfRangeException(nameof(meters), "Buffer distance cannot be negative");
                
                double lat = coordinate.Y;
                double lon = coordinate.X;
                
                // Validate coordinate bounds
                if (lat < -90.0 || lat > 90.0)
                    throw new ArgumentOutOfRangeException(nameof(coordinate), $"Latitude {lat} is outside valid range [-90, 90]");
                if (lon < -180.0 || lon > 180.0)
                    lon = NormalizeLongitude(lon); // Auto-normalize longitude
                
                // Calculate latitude buffer (straightforward - no wraparound issues)
                double latRadians = lat * DEG_TO_RAD;
                
                // Convert meters to degrees: meters / (Earth radius in meters) * (180/π)
                double deltaLatDegrees = meters / EARTH_RADIUS_MEAN * RAD_TO_DEG;
                
                double minLat = Math.Max(-90.0, lat - deltaLatDegrees);
                double maxLat = Math.Min(90.0, lat + deltaLatDegrees);
                
                // Calculate longitude buffer (complex - must handle date line crossing)
                // At higher latitudes, longitude degrees represent shorter distances
                double cosLat = Math.Cos(latRadians);
                double deltaLonDegrees;
                
                if (Math.Abs(cosLat) < POLE_THRESHOLD) // Near poles
                {
                    // At poles, any longitude buffer covers all longitudes
                    return new Envelope(-180.0, 180.0, minLat, maxLat);
                }
                else
                {
                    // Convert meters to longitude degrees, accounting for latitude
                    deltaLonDegrees = meters / EARTH_RADIUS_MEAN * RAD_TO_DEG / cosLat;
                }
                
                // Special case: if buffer spans more than 180°, it covers the whole world
                if (deltaLonDegrees >= 180.0)
                {
                    return new Envelope(-180.0, 180.0, minLat, maxLat);
                }
                
                double minLon = lon - deltaLonDegrees;
                double maxLon = lon + deltaLonDegrees;
                
                // Check if we need to handle date line crossing BEFORE normalization
                bool crossesDateLine = (minLon < -180.0) || (maxLon > 180.0);
                
                if (crossesDateLine)
                {
                    if (minLon < -180.0 && maxLon > 180.0)
                    {
                        // Buffer is so large it wraps around completely
                        return new Envelope(-180.0, 180.0, minLat, maxLat);
                    }
                    else if (minLon < -180.0)
                    {
                        // Crosses westward past -180°
                        // Convert to: [minLon + 360, 180] ∪ [-180, maxLon]
                        // For simplicity, we'll use the continuous representation
                        minLon = minLon + 360.0; // Wrap to positive side
                        if (minLon > maxLon)
                        {
                            // This creates a crossing envelope, expand to full world for spatial queries
                            return new Envelope(-180.0, 180.0, minLat, maxLat);
                        }
                    }
                    else if (maxLon > 180.0)
                    {
                        // Crosses eastward past +180°
                        // Convert to: [minLon, 180] ∪ [-180, maxLon - 360]
                        maxLon = maxLon - 360.0; // Wrap to negative side
                        if (minLon > maxLon)
                        {
                            // This creates a crossing envelope, expand to full world for spatial queries
                            return new Envelope(-180.0, 180.0, minLat, maxLat);
                        }
                    }
                }
                
                // Ensure bounds are within valid range
                minLon = Math.Max(-180.0, Math.Min(180.0, minLon));
                maxLon = Math.Max(-180.0, Math.Min(180.0, maxLon));
                
                return new Envelope(minLon, maxLon, minLat, maxLat);
            }

            /// <summary>
            /// Normalize longitude to [-180, +180] range using efficient modulo operation
            /// </summary>
            /// <param name="longitude">Longitude in degrees</param>
            /// <returns>Normalized longitude in [-180, +180] range</returns>
            private static double NormalizeLongitude(double longitude)
            {
                // More efficient than while loops for large values
                longitude = longitude % 360.0;
                if (longitude > 180.0)
                    longitude -= 360.0;
                else if (longitude < -180.0)
                    longitude += 360.0;
                return longitude;
            }

            /// <summary>
            /// Legacy buffer method - kept for backward compatibility but with proper bounds
            /// </summary>
            [Obsolete("Use BufferOptimized for better performance and date line handling")]
            public static Envelope BufferLegacy(Coordinate coordinate, int meters)
            {
                if (coordinate == null)
                    throw new ArgumentNullException(nameof(coordinate));
                if (meters < 0)
                    throw new ArgumentOutOfRangeException(nameof(meters), "Buffer distance cannot be negative");
                
                var n = Move(coordinate, meters, 0);
                var e = Move(coordinate, meters, 90);
                var s = Move(coordinate, meters, 180);
                var w = Move(coordinate, meters, 270);
                
                // Normalize all coordinates to proper bounds
                n = new Coordinate(NormalizeLongitude(n.X), Math.Max(-90, Math.Min(90, n.Y)));
                e = new Coordinate(NormalizeLongitude(e.X), Math.Max(-90, Math.Min(90, e.Y)));
                s = new Coordinate(NormalizeLongitude(s.X), Math.Max(-90, Math.Min(90, s.Y)));
                w = new Coordinate(NormalizeLongitude(w.X), Math.Max(-90, Math.Min(90, w.Y)));
                
                double minX = Min(n.X, e.X, s.X, w.X);
                double maxX = Max(n.X, e.X, s.X, w.X);
                double minY = Min(n.Y, e.Y, s.Y, w.Y);
                double maxY = Max(n.Y, e.Y, s.Y, w.Y);
                
                // Handle date line crossing in legacy method
                if (maxX - minX > 180.0)
                {
                    // Likely crossed date line, expand to full range
                    minX = -180.0;
                    maxX = 180.0;
                }
                
                return new Envelope(minX, maxX, minY, maxY);
            }

            /// <summary>
            /// Calculate the great circle distance between two WGS84 coordinates using Haversine formula
            /// </summary>
            /// <param name="first">First coordinate</param>
            /// <param name="second">Second coordinate</param>
            /// <returns>Distance in meters</returns>
            public static double Distance(Coordinate first, Coordinate second)
            {
                if (first == null)
                    throw new ArgumentNullException(nameof(first));
                if (second == null)
                    throw new ArgumentNullException(nameof(second));
                    
                return DistanceRadians(first, second) * EARTH_RADIUS_MEAN;
            }

            /// <summary>
            /// Move a coordinate by a given distance and bearing using spherical trigonometry
            /// </summary>
            /// <param name="origin">Starting coordinate</param>
            /// <param name="meters">Distance to move in meters</param>
            /// <param name="bearing">Bearing in degrees (0 = North, 90 = East)</param>
            /// <returns>New coordinate after movement</returns>
            public static Coordinate Move(Coordinate origin, double meters, double bearing)
            {
                if (origin == null)
                    throw new ArgumentNullException(nameof(origin));
                if (double.IsNaN(meters) || double.IsInfinity(meters))
                    throw new ArgumentException("Distance must be a valid number", nameof(meters));
                if (double.IsNaN(bearing) || double.IsInfinity(bearing))
                    throw new ArgumentException("Bearing must be a valid number", nameof(bearing));
                
                // Convert distance to angular distance (radians)
                double angularDistance = Math.Abs(meters) / EARTH_RADIUS_MEAN;

                double lon1 = origin.X * DEG_TO_RAD;
                double lat1 = origin.Y * DEG_TO_RAD;
                double bearingRad = bearing * DEG_TO_RAD;

                // Calculate new latitude
                double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angularDistance) +
                                       Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad));
                
                // Calculate new longitude
                double lon2 = lon1 + Math.Atan2(Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
                                               Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2));

                // Convert back to degrees and normalize longitude
                double newLon = NormalizeLongitude(lon2 * RAD_TO_DEG);
                double newLat = Math.Max(-90.0, Math.Min(90.0, lat2 * RAD_TO_DEG)); // Clamp latitude

                return new Coordinate(newLon, newLat);
            }

            /// <summary>
            /// Calculate the angular distance between two coordinates using Haversine formula
            /// </summary>
            /// <param name="first">First coordinate</param>
            /// <param name="second">Second coordinate</param>
            /// <returns>Angular distance in radians</returns>
            private static double DistanceRadians(Coordinate first, Coordinate second)
            {
                double lon1 = first.X * DEG_TO_RAD;
                double lon2 = second.X * DEG_TO_RAD;
                double lat1 = first.Y * DEG_TO_RAD;
                double lat2 = second.Y * DEG_TO_RAD;

                double deltaLat = lat1 - lat2;
                double deltaLon = lon1 - lon2;

                double sinHalfDeltaLat = Math.Sin(deltaLat / 2.0);
                double sinHalfDeltaLon = Math.Sin(deltaLon / 2.0);

                double a = sinHalfDeltaLat * sinHalfDeltaLat +
                          Math.Cos(lat1) * Math.Cos(lat2) * sinHalfDeltaLon * sinHalfDeltaLon;

                return 2.0 * Math.Asin(Math.Sqrt(a));
            }
        }
    }
}