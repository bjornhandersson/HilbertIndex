using System;

namespace Bson.HilbertIndex
{
    public class LinearProjection : IProjection
    {
        public void PointToPosition(out Coordinate position, int x, int y, int N)
        {
            x = Math.Max(Math.Min(x, N), 0);
            y = Math.Max(Math.Min(y, N), 0);
            double lon = ((double)x / (N / 360d)) - 180;
            double lat = ((double)y / (N / 180d)) - 90;
            position = new Coordinate(lon, lat);
        }

        public void PositionToPoint(Coordinate position, out int x, out int y, int N)
        {
            x = (int)Math.Truncate((180d + position.X) * N / 360d);
            y = (int)Math.Truncate((90d + position.Y) * N / 180d);
        }
    }

    public interface IProjection
    {
        /// <summary>
        /// Convert position to point in grid where lower left corner is (0, 0) and upper right corner is  (N -1, N -1)
        /// </summary>
        /// <param name="position">WGS84 position</param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="N"></param>
        void PositionToPoint(Coordinate position, out int x, out int y, int N);

        /// <summary>
        /// Convert point in grid where lower left corner is (0, 0) and upper right corner is  (N -1, N -1) to WGS84 position
        /// </summary>
        /// <param name="position">WGS84 position</param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="N"></param>
        void PointToPosition(out Coordinate position, int x, int y, int N);
    }
}