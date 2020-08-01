using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;

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

    public class CoordinateSystems
    {
        private static double Min(params double[] values) => values.Min();
        private static double Max(params double[] values) => values.Max();

        public class Wgs84
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
                // meters as fractions of radious of earth is... tiny consider using decimla type (128 bit vs 64 for a double)
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

    public class SearchResult
    {
        public SearchResult(ulong[][] ranges, IList<Envelope> bounds, IList<HilbertCode.Box> boxes)
        {
            Ranges = ranges;
            Bounds = bounds;
            Boxes = boxes;
        }
        public ulong[][] Ranges { get; }

        public IList<Envelope> Bounds { get; }

        public IList<HilbertCode.Box> Boxes { get; }
    }

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

    public class HilbertCode
    {
        /// <summary>
        /// Box in 2d space
        /// </summary>
        public struct Box
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
            public Box(int x, int y, int p, int q)
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

            public int height { get { return _p; } }

            public int width { get { return _q; } }

            public override string ToString()
                => string.Format("xy:{0},{1} p:{2},q:{3}", _x, _y, _p, _q);

            public override int GetHashCode()
                => (_x << _y) ^ (_p << _q);

            public override bool Equals(object obj)
                => obj is Box && obj.GetHashCode() == this.GetHashCode();
        }

        public struct Resolution
        {
            public static int ULTRALOW = 4;

            public static int LOW = 10;

            /// <summary>
            /// Max resolution that still fitts in 32bit int.
            /// 650x650 grid
            /// </summary>
            public static int MEDIUM = 16;

            /// <summary>
            /// 40bit value
            ///  70 x 70 grid
            /// </summary>
            public static int HIGH = 19;

            /// <summary>
            /// Max allowed value (todo, 32 is max but alg does not support that now, overflows)
            /// </summary>
            public static int ULTRAHIGH = 30;
        }

        public static HilbertCode Create(int resolution, IProjection projection)
        {
            return new HilbertCode(resolution, projection);
        }

        public static HilbertCode Default()
        {
            return new HilbertCode(Resolution.HIGH, new LinearProjection());
        }


        // e = 16 (fit 32bit uint) => (~611m * 2)
        // e = 19 (fit 32bit uint) => (~79m * 2)
        // e = 22 (fit 32bit uint) => (~10m * 2)
        private readonly int _N;

        private readonly IProjection _projection;

        private const int DefaultRangeCompactation = 128;

        private HilbertCode(int resolution, IProjection projection)
        {
            if (resolution > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution cannot be above 30");
            }
            _N = (int)Math.Pow(2, resolution);
            _projection = projection;
        }

        /// <summary>
        /// Curve order
        /// </summary>
        public int CurveOrder
        {
            get { return (int)(Math.Log(_N) / Math.Log(2)); }
        }

        /// <summary>
        /// Height and width of grid
        /// </summary>
        public int GridSize
        {
            get { return _N - 1; }
        }

        /// <summary>
        /// Projection. Translate WGS84 position to 2D grid with 0,0 in lower left corner
        /// </summary>
        public IProjection Projection
        {
            get { return _projection; }
        }

        /// <summary>
        /// Get the hilbert number for the given position
        /// </summary>
        /// <param name="position">Position</param>
        /// <returns>number representing the position on a one-dimensional hilbert curve</returns>
        public ulong PositionToIndex(Coordinate position)
        {
            _projection.PositionToPoint(position, out int x, out int y, _N - 1);
            return PointToIndex(x, y, _N);
        }

        /// <summary>
        /// Get the positin for the given hilbert number.
        /// </summary>
        /// <param name="d">hilbert number</param>
        /// <returns>Position representing the hilbert number</returns>
        public Coordinate IndexToPosition(ulong d)
        {
            IndexToPoint(_N, d, out int x, out int y);
            _projection.PointToPosition(out var position, x, y, _N - 1);
            return position;
        }

        /// <summary>
        /// returns the hilbert number for the point int the hilbert coordinate systems.
        /// Coordinate system has 0.0 in lower left corner and ends with 2 ^ CurveOrder - 1 in upper left corner.
        /// </summary>
        /// <param name="x">x</param>
        /// <param name="y">y</param>
        /// <returns>number representing the position on a one-dimensional hilbert curve</returns>
        public ulong PointToIndex(int x, int y)
        {
            return PointToIndex(x, y, _N);
        }

        /// <summary>
        /// get a point in the hilbert coordinate system for the given hilbert number
        /// </summary>
        /// <param name="d"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void IndexToPoint(ulong d, out int x, out int y)
        {
            IndexToPoint(_N, d, out x, out y);
        }


        /// <summary>
        /// Get ranges and bounds for nearest neighbour search
        /// </summary>
        /// <param name="search1D">Key of search point</param>
        /// <param name="neighbour1D">Nearest one dimensional neighbour</param>
        /// <returns></returns>
        public SearchResult GetRanges(ulong search1D, ulong neighbour1D, int maxRanges)
        {
            var box = CreateBox1D(search1D, neighbour1D);
            return GetRanges(box, maxRanges);
        }

        /// <summary>
        /// Get ranges of hilbert indices within the specified bounding box.
        /// Those ranges can be used for constructing range queries.
        /// </summary>
        /// <param name="bounds">bounding box to get hilbert indices for</param>
        /// <param name="maxRanges">
        /// max number of returned ranges. 
        /// Number less than zero will return as many ranges required to cover the box with no overlapp.
        /// If greater than zero, the ranges closest to each other will be joined as one until the total number of ranges is less than the specifies maxRange parameter. 
        /// The smaller number, the larger will the bounding box be. 128 is a good number to keep good precision.
        /// </param>
        /// <returns>ranges of hilbert indices covering the bounding box.</returns>
        public SearchResult GetRanges(Envelope bounds, int maxRanges = DefaultRangeCompactation)
        {
            var box = BoundingBoxToHilbertBox(bounds);
            return GetRanges(box, maxRanges);
        }

        /// <summary>
        /// Get ranges of hilbert indices within the  given bounding box. 
        /// </summary>
        /// <param name="x">x coordinate of lower left corner</param>
        /// <param name="y">y coordinate of lower left corner</param>
        /// <param name="p">height of the box</param>
        /// <param name="q">width of the box</param>
        /// <returns></returns>
        public SearchResult GetRanges(Box box, int maxRanges = DefaultRangeCompactation)
        {
            return GetRange('A', box, maxRanges);
        }

        /// <summary>
        /// Get the bounding box for the given ranges of hilber indices.
        /// </summary>
        /// <param name="ranges">array of size [n][2]</param>
        /// <returns>bounding box</returns>
        public Envelope GetBoundingBoxForRanges(ulong[][] ranges)
        {
            Envelope box = null;
            for (int r = 0; r < ranges.Length; r++)
            {
                Coordinate first = IndexToPosition(ranges[r][0]);
                Coordinate last = IndexToPosition(ranges[r][1]);

                if (box == null)
                {
                    box = new Envelope(minX: first.X, maxX: first.X, minY: first.Y, maxY: first.Y)
                        .Expand(last);
                }
                else
                {
                    box = box.Expand(first).Expand(last);
                }
            }
            return box;
        }

        private Box CreateBox1D(ulong center, ulong other)
        {
            int x1, y1, x2, y2;
            IndexToPoint(center, out x1, out y1);
            IndexToPoint(other, out x2, out y2);

            // distance between p1 and p2
            int hw = (int)Math.Ceiling(Math.Sqrt(Math.Pow((x1 - x2), 2) + Math.Pow((y1 - y2), 2)));
            //int _hw = Math.Max((y1 > y2 ? y1 - y2 : y2 - y1), (x1 > x2 ? x1 - x2 : x2 - x1));

            hw = Math.Min(hw, (_N - 1)); // min height of 1 max width world size
            int x = x1 - hw;
            int y = y1 - hw;

            return new Box(x, y, Math.Min((hw * 2) + 1, _N - 1), Math.Min((hw * 2) + 1, _N - 1));
        }

        // 2 ^ 16 fits within uint32, higher n needs ulong
        private static ulong PointToIndex(int x, int y, int n)
        {
            // if x or y > n => overflow exception
            int rx, ry, s = 0;
            ulong d = 0;
            for (s = n / 2; s > 0; s /= 2)
            {
                rx = (x & s) > 0 ? 1 : 0;
                ry = (y & s) > 0 ? 1 : 0;
                d += ((ulong)s * (ulong)s * (ulong)((3 * rx) ^ ry));
                Rotate(s, ref x, ref y, rx, ry);
            }
            return d;
        }

        private static void IndexToPoint(int n, ulong d, out int x, out int y)
        {
            int rx, ry, s = 0;
            ulong t = d;
            x = y = 0;
            for (s = 1; s < n; s *= 2)
            {
                rx = (1 & (t / 2)) > 0 ? 1 : 0;
                ry = (1 & (t ^ (ulong)rx)) > 0 ? 1 : 0;
                Rotate(s, ref x, ref y, rx, ry);
                x += s * rx;
                y += s * ry;
                t /= 4;
            }
        }

        private static void Rotate(int n, ref int x, ref int y, int rx, int ry)
        {
            if (ry == 0)
            {
                if (rx == 1)
                {
                    x = n - 1 - x;
                    y = n - 1 - y;
                }
                int t = x;
                x = y;
                y = t;
            }
        }

        /// <summary>
        /// Create a hilbert box from a bounding box.
        /// The hilbert box is represenmteed by a point (x,y) in hilbert coordinate system placed at the lower left corner with height (p) and width (q)
        /// </summary> 
        /// <param name="bounds"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="p"></param>
        /// <param name="q"></param>
        private Box BoundingBoxToHilbertBox(Envelope bounds)
        {
            int x, y, p, q;
            int nwx, nwy;
            var posNWC = new Coordinate(bounds.MinX, bounds.MaxY);
            _projection.PositionToPoint(posNWC, out nwx, out nwy, _N - 1);

            int sex, sey;
            var posSEC = new Coordinate(bounds.MaxX, bounds.MinY);
            _projection.PositionToPoint(posSEC, out sex, out sey, _N - 1);

            var posSWC = new Coordinate(bounds.MinX, bounds.MinY);
            _projection.PositionToPoint(posSWC, out x, out y, _N - 1);
            p = nwy - y + 1;
            q = sex - x + 1;
            return new Box(x, y, p, q);
        }

        private Envelope HilbertBoxToBoundingBox(int x, int y, int p, int q)
        {
            Coordinate swc, nec;
            _projection.PointToPosition(out swc, x - 1, y - 1, _N - 1);
            _projection.PointToPosition(out nec, x + q + 2, y + p + 2, _N - 1);
            return new Envelope(swc.X, nec.X, swc.Y, nec.Y);
        }

        // todo: pass to make thread safe
        //private List<ulong[]> _ranges = null;

        private SearchResult GetRange(char rotation, Box bounds, int maxRanges)
        {
            if ((bounds.MaxX < 0 && bounds.MaxY < 0) || (bounds.MinX > _N - 1 && bounds.MinY > _N - 1))
                throw new ArgumentException("Bounds outside world");

            var ranges = new List<ulong[]>();
            var boxes = WrapOverlap(bounds);
            foreach (var box in boxes)
            {
                SplitQuad(rotation, (ulong)_N, (ulong)0, (ulong)box.MinX, (ulong)box.MinY, (ulong)box.height, (ulong)box.width, ranges);
            }

            ulong[][] output;

            if (maxRanges > 0)
            {
                output = CompactRanges(ranges.ToArray(), maxRanges);
            }
            else
            {
                output = ranges.ToArray();
            }

            return new SearchResult(
                output,
                boxes.ConvertAll<Envelope>(box => HilbertBoxToBoundingBox(box.MinX, box.MinY, box.height, box.width)),
                boxes
            );
        }

        // todo: make range truncating native in this method to avoid level of recursion and truncation method penalties
        private void SplitQuad(char rotation, ulong t, ulong mino, ulong x, ulong y, ulong p, ulong q, List<ulong[]> ranges)
        {
            if (t == p && t == q)
            {
                AddRange(mino, mino + t * t - 1L, ranges);
            }
            else if (t > 1)
            {
                switch (rotation)
                {
                    case 'A':
                        SplitQuadA(t, mino, x, y, p, q, ranges);
                        break;
                    case 'B':
                        SplitQuadB(t, mino, x, y, p, q, ranges);
                        break;
                    case 'C':
                        SplitQuadC(t, mino, x, y, p, q, ranges);
                        break;
                    case 'D':
                        SplitQuadD(t, mino, x, y, p, q, ranges);
                        break;
                }
            }
        }

        private void SplitQuadA(ulong t, ulong mino, ulong x, ulong y, ulong p, ulong q, List<ulong[]> ranges)
        {
            ulong N2 = t / 2L;
            ulong N4 = N2 * N2;
            int type = GetQuadType(t, x, y, p, q);
            switch (type)
            {
                case 0:
                    SplitQuad('B', N2, mino, x, y, N2 - y, N2 - x, ranges);
                    SplitQuad('A', N2, mino + N4, x, 0L, y + p - N2, N2 - x, ranges);
                    SplitQuad('A', N2, mino + N4 * 2L, 0L, 0L, y + p - N2, x + q - N2, ranges);
                    SplitQuad('D', N2, mino + N4 * 3L, 0L, y, N2 - y, x + q - N2, ranges);
                    break;
                case 1:
                    SplitQuad('A', N2, mino + N4, x, y - N2, p, N2 - x, ranges);
                    SplitQuad('A', N2, mino + N4 * 2L, 0L, y - N2, p, x + q - N2, ranges);
                    break;
                case 2:
                    SplitQuad('B', N2, mino, x, y, p, N2 - x, ranges);
                    SplitQuad('D', N2, mino + N4 * 3L, 0L, y, p, x + q - N2, ranges);
                    break;
                case 3:
                    SplitQuad('B', N2, mino, x, y, N2 - y, q, ranges);
                    SplitQuad('A', N2, mino + N4, x, 0L, y + p - N2, q, ranges);
                    break;
                case 4:
                    SplitQuad('A', N2, mino + N4 * 2L, x - N2, 0L, y + p - N2, q, ranges);
                    SplitQuad('D', N2, mino + N4 * 3L, x - N2, y, N2 - y, q, ranges);
                    break;
                case 5:
                    SplitQuad('A', N2, mino + N4, x, y - N2, p, q, ranges);
                    break;
                case 6:
                    SplitQuad('A', N2, mino + N4 * 2L, x - N2, y - N2, p, q, ranges);
                    break;
                case 7:
                    SplitQuad('B', N2, mino, x, y, p, q, ranges);
                    break;
                case 8:
                    SplitQuad('D', N2, mino + N4 * 3L, x - N2, y, p, q, ranges);
                    break;
            }
        }

        private void SplitQuadB(ulong t, ulong mino, ulong x, ulong y, ulong p, ulong q, List<ulong[]> ranges)
        {
            ulong N2 = t / 2L;
            ulong N4 = N2 * N2;
            int type = GetQuadType(t, x, y, p, q);
            switch (type)
            {
                case 0:
                    SplitQuad('A', N2, mino, x, y, N2 - y, N2 - x, ranges);
                    SplitQuad('B', N2, mino + N4, 0L, y, N2 - y, x + q - N2, ranges);
                    SplitQuad('B', N2, mino + N4 * 2L, 0L, 0L, y + p - N2, x + q - N2, ranges);
                    SplitQuad('C', N2, mino + N4 * 3L, x, 0L, y + p - N2, N2 - x, ranges);
                    break;
                case 1:
                    SplitQuad('B', N2, mino + N4 * 2L, 0L, y - N2, p, x + q - N2, ranges);
                    SplitQuad('C', N2, mino + N4 * 3L, x, y - N2, p, N2 - x, ranges);
                    break;
                case 2:
                    SplitQuad('A', N2, mino, x, y, p, N2 - x, ranges);
                    SplitQuad('B', N2, mino + N4 * 1L, 0L, y, p, x + q - N2, ranges);
                    break;
                case 3:
                    SplitQuad('A', N2, mino, x, y, N2 - y, q, ranges);
                    SplitQuad('C', N2, mino + N4 * 3L, x, 0L, y + p - N2, q, ranges);
                    break;
                case 4:
                    SplitQuad('B', N2, mino + N4, x - N2, y, N2 - y, q, ranges);
                    SplitQuad('B', N2, mino + N4 * 2L, x - N2, 0L, y + p - N2, q, ranges);
                    break;
                case 5:
                    SplitQuad('C', N2, mino + N4 * 3L, x, y - N2, p, q, ranges);
                    break;
                case 6:
                    SplitQuad('B', N2, mino + N4 * 2L, x - N2, y - N2, p, q, ranges);
                    break;
                case 7:
                    SplitQuad('A', N2, mino, x, y, p, q, ranges);
                    break;
                case 8:
                    SplitQuad('B', N2, mino + N4, x - N2, y, p, q, ranges);
                    break;
            }
        }

        private void SplitQuadC(ulong t, ulong mino, ulong x, ulong y, ulong p, ulong q, List<ulong[]> ranges)
        {
            ulong N2 = t / 2;
            ulong N4 = N2 * N2;
            int type = GetQuadType(t, x, y, p, q);
            switch (type)
            {
                case 0:
                    SplitQuad('D', N2, mino, 0L, 0L, y + p - N2, x + q - N2, ranges);
                    SplitQuad('C', N2, mino + N4, 0L, y, N2 - y, x + q - N2, ranges);
                    SplitQuad('C', N2, mino + N4 * 2L, x, y, N2 - y, N2 - x, ranges);
                    SplitQuad('B', N2, mino + N4 * 3L, x, 0L, y + p - N2, N2 - x, ranges);
                    break;
                case 1:
                    SplitQuad('D', N2, mino, 0L, y - N2, p, x + q - N2, ranges);
                    SplitQuad('B', N2, mino + N4 * 3L, x, y - N2, p, N2 - x, ranges);
                    break;
                case 2:
                    SplitQuad('C', N2, mino + N4, 0L, y, p, x + q - N2, ranges);
                    SplitQuad('C', N2, mino + N4 * 2L, x, y, p, N2 - x, ranges);
                    break;
                case 3:
                    SplitQuad('C', N2, mino + N4 * 2L, x, y, N2 - y, q, ranges);
                    SplitQuad('B', N2, mino + N4 * 3L, x, 0L, y + p - N2, q, ranges);
                    break;
                case 4:
                    SplitQuad('D', N2, mino, x - N2, 0L, y + p - N2, q, ranges);
                    SplitQuad('C', N2, mino + N4, x - N2, y, N2 - y, q, ranges);
                    break;
                case 5:
                    SplitQuad('B', N2, mino + N4 * 3L, x, y - N2, p, q, ranges);
                    break;
                case 6:
                    SplitQuad('D', N2, mino, x - N2, y - N2, p, q, ranges);
                    break;
                case 7:
                    SplitQuad('C', N2, mino + N4 * 2L, x, y, p, q, ranges);
                    break;
                case 8:
                    SplitQuad('C', N2, mino + N4, x - N2, y, p, q, ranges);
                    break;
            }
        }

        private void SplitQuadD(ulong t, ulong mino, ulong x, ulong y, ulong p, ulong q, List<ulong[]> ranges)
        {
            ulong N2 = t / 2;
            ulong N4 = N2 * N2;
            int type = GetQuadType(t, x, y, p, q);
            switch (type)
            {
                case 0:
                    SplitQuad('C', N2, mino, 0L, 0L, y + p - N2, x + q - N2, ranges);
                    SplitQuad('D', N2, mino + N4, x, 0L, y + p - N2, N2 - x, ranges);
                    SplitQuad('D', N2, mino + N4 * 2L, x, y, N2 - y, N2 - x, ranges);
                    SplitQuad('A', N2, mino + N4 * 3L, 0L, y, N2 - y, x + q - N2, ranges);
                    break;
                case 1:
                    SplitQuad('C', N2, mino, 0L, y - N2, p, x + q - N2, ranges);
                    SplitQuad('D', N2, mino + N4, x, y - N2, p, N2 - x, ranges);
                    break;
                case 2:
                    SplitQuad('D', N2, mino + N4 * 2L, x, y, p, N2 - x, ranges);
                    SplitQuad('A', N2, mino + N4 * 3L, 0L, y, p, x + q - N2, ranges);
                    break;
                case 3:
                    SplitQuad('D', N2, mino + N4, x, 0L, y + p - N2, q, ranges);
                    SplitQuad('D', N2, mino + N4 * 2L, x, y, N2 - y, q, ranges);
                    break;
                case 4:
                    SplitQuad('C', N2, mino, x - N2, 0L, y + p - N2, q, ranges);
                    SplitQuad('A', N2, mino + N4 * 3L, x - N2, y, N2 - y, q, ranges);
                    break;
                case 5:
                    SplitQuad('D', N2, mino + N4, x, y - N2, p, q, ranges);
                    break;
                case 6:
                    SplitQuad('C', N2, mino, x - N2, y - N2, p, q, ranges);
                    break;
                case 7:
                    SplitQuad('D', N2, mino + N4 * 2L, x, y, p, q, ranges);
                    break;
                case 8:
                    SplitQuad('A', N2, mino + N4 * 3L, x - N2, y, p, q, ranges);
                    break;
            }
        }

        private int GetQuadType(ulong t, ulong x, ulong y, ulong p, ulong q)
        {
            ulong T2 = t / 2;

            // each window can be derived to 9 types 
            // (windows intersect all 4 sub-quad, interset upper half 2 sub-quad, intersecting  bottom 2 sub-qaud, left 2 ..., right 2 ..., 4x single quad)
            if (x < T2 && y < T2 && (x + q) >= T2 && (y + p) >= T2)
                return 0;
            else if (x < T2 && y >= T2 && (x + q) >= T2 && (y + p) >= T2)
                return 1;
            else if (x < T2 && y < T2 && (x + q) >= T2 && (y + p) < T2)
                return 2;
            else if (x < T2 && y < T2 && (x + q) < T2 && (y + p) >= T2)
                return 3;
            else if (x >= T2 && y < T2 && (x + q) >= T2 && (y + p) >= T2)
                return 4;
            else if (x < T2 && y >= T2 && (x + q) < T2 && (y + p) >= T2)
                return 5;
            else if (x >= T2 && y >= T2 && (x + q) >= T2 && (y + p) >= T2)
                return 6;
            else if (x < T2 && y < T2 && (x + q) < T2 && (y + p) < T2)
                return 7;
            else if (x >= T2 && y < T2 && (x + q) >= T2 && (y + p) < T2)
                return 8;
            else
                throw new Exception("Unknown quad type"); // should not be possible

        }

        private void AddRange(ulong min, ulong max, List<ulong[]> ranges)
        {
            if (ranges.Count == 0)
            {
                ranges.Add(new[] { min, max });
            }
            else if (ranges[ranges.Count - 1][1] == min - 1)
            {
                ranges[ranges.Count - 1][1] = max;
            }
            else
            {
                ranges.Add(new[] { min, max });
            }
        }

        // join ranges which distance is less than the tolerance, out param is the next min intervall which was not joined
        private static ulong[][] CompactRanges(ulong[][] ranges, int length)
        {
            if (ranges.Length == 0)
                throw new ArgumentException("Ranges cannot have a length of 0");

            // store the value of next min intervall found in range which is greater than iMinTolerance
            ulong iMinTolerance = 1;
            ulong iNextMin = ulong.MaxValue;
            int iLength = ranges.Length;
            while (iLength > length)
            {
                int iComInx = 0;
                int iCount = 1;

                for (int i = 1; i < iLength; i++)
                {
                    // join range if less than tolerance
                    ulong diff = ranges[i][0] - ranges[iComInx][1];
                    if (diff <= iMinTolerance)
                    {
                        ranges[iCount - 1][1] = ranges[i][1];
                    }
                    else
                    {
                        if (diff < iNextMin)
                            iNextMin = diff;

                        ranges[iCount++] = ranges[i];
                        iComInx = i;
                    }
                }

                iMinTolerance = iNextMin;
                iNextMin = ulong.MaxValue;
                iLength = iCount;
            }

            Array.Resize<ulong[]>(ref ranges, iLength);
            return ranges;
        }

        private List<Box> WrapOverlap(Box box)
        {
            var boxes = new List<Box>();
            boxes.Add(box);

            if (!(box.MinX < 0 || box.MinY < 0 || box.MaxX > _N - 1 || box.MaxY > _N - 1))
                return boxes;

            var tmpboxes = new List<Box>();
            foreach (Box b in boxes)
            {
                if (b.MinX < 0)
                {
                    if (b.width + b.MinX > 0)
                    {
                        tmpboxes.Add(new Box(0, b.MinY, b.height, b.width + b.MinX));
                    }

                    tmpboxes.Add(new Box((_N - 1) + b.MinX, b.MinY, b.height, b.MinX * -1));
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            foreach (Box b in boxes)
            {
                if (b.MinY < 0)
                {

                    if (b.height + b.MinY > 0)
                    {
                        tmpboxes.Add(new Box(b.MinX, 0, b.height + b.MinY, b.width));
                    }
                    else
                    {
                        // dont warp from pole to pole.
                        // todo: spread sideways instead
                        tmpboxes.Add(new Box(b.MinX, (_N - 1) + b.MinY, b.MinY * -1, b.width));
                    }
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            foreach (Box b in boxes)
            {
                if (b.MaxX > _N - 1)
                {
                    tmpboxes.Add(new Box(b.MinX, b.MinY, b.height, (_N - 1) - b.MinX));
                    tmpboxes.Add(new Box(0, b.MinY, b.height, b.MaxX - (_N - 1)));
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            foreach (Box b in boxes)
            {
                if (b.MaxY > _N - 1)
                {
                    tmpboxes.Add(new Box(b.MinX, b.MinY, (_N - 1) - b.MinY, b.width));
                    // dont warp from pole to pole.
                    // todo: spread sideways instead
                    //tmpboxes.Add(new box(b.minX, 0, b.maxY - (N - 1), b.width));
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            boxes.Sort((box1, box2) => PointToIndex(box1.MinX, box1.MinY, _N).CompareTo(PointToIndex(box2.MinX, box2.MinY, _N)));
            return boxes;
        }
    }
}