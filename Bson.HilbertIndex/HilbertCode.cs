using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex
{
    /// <summary>
    /// Class containing various utility methods for translating 2D coordinates to Hilbert indices
    /// </summary>
    public class HilbertCode
    {
        public enum Resolution : int
        {
            ULTRALOW = 4,

            LOW = 10,

            /// <summary>
            /// Max resolution that still fitts in 32bit int.
            /// 650x650 grid
            /// </summary>
            MEDIUM = 16,

            /// <summary>
            /// 40bit value
            ///  70 x 70 grid
            /// </summary>
            HIGH = 19,

            /// <summary>
            /// Max allowed value (todo, 32 is max but alg does not support that now, overflows)
            /// </summary>
            ULTRAHIGH = 30
        }

        // e = 16 (fit 32bit uint) => (~611m * 2)
        // e = 19 (fit 32bit uint) => (~79m * 2)
        // e = 22 (fit 32bit uint) => (~10m * 2)
        private readonly int _N;

        private readonly IProjection _projection;

        private const int DefaultRangeCompactation = 128;

        public HilbertCode(Resolution resolution = Resolution.HIGH, IProjection projection = null)
        {
            if ((int)resolution > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution cannot be above 30");
            }
            _N = (int)Math.Pow(2, (int)resolution);
            _projection = projection ?? new LinearProjection();
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
        public ulong Encode(Coordinate position)
        {
            _projection.PositionToPoint(position, out int x, out int y, _N - 1);
            return Encode(x, y, _N);
        }

        /// <summary>
        /// returns the hilbert number for the point int the hilbert coordinate systems.
        /// Coordinate system has 0.0 in lower left corner and ends with 2 ^ CurveOrder - 1 in upper left corner.
        /// </summary>
        /// <param name="x">x</param>
        /// <param name="y">y</param>
        /// <returns>number representing the position on a one-dimensional hilbert curve</returns>
        public ulong Encode(int x, int y)
            => Encode(x, y, _N);

        /// <summary>
        /// Get the coordinate for the given hilbert indices.
        /// </summary>
        /// <param name="d">hilbert number</param>
        /// <returns>Coordinate representing the hilbert number</returns>
        public Coordinate Decode(ulong d)
        {
            DecodeoPoint(_N, d, out int x, out int y);
            _projection.PointToPosition(out var position, x, y, _N - 1);
            return position;
        }

        /// <summary>
        /// get a point in the hilbert coordinate system for the given hilbert number
        /// </summary>
        /// <param name="d"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void Decode(ulong d, out int x, out int y)
            => DecodeoPoint(_N, d, out x, out y);
        
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
        public SearchResult GetRanges(HilbertEnvelope box, int maxRanges = DefaultRangeCompactation)
            => GetRange('A', box, maxRanges);
        

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
                Coordinate first = Decode(ranges[r][0]);
                Coordinate last = Decode(ranges[r][1]);

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

        private HilbertEnvelope CreateBox1D(ulong center, ulong other)
        {
            int x1, y1, x2, y2;
            Decode(center, out x1, out y1);
            Decode(other, out x2, out y2);

            // distance between p1 and p2
            int hw = (int)Math.Ceiling(Math.Sqrt(Math.Pow((x1 - x2), 2) + Math.Pow((y1 - y2), 2)));
            //int _hw = Math.Max((y1 > y2 ? y1 - y2 : y2 - y1), (x1 > x2 ? x1 - x2 : x2 - x1));

            hw = Math.Min(hw, (_N - 1)); // min height of 1 max width world size
            int x = x1 - hw;
            int y = y1 - hw;

            return new HilbertEnvelope(x, y, Math.Min((hw * 2) + 1, _N - 1), Math.Min((hw * 2) + 1, _N - 1));
        }

        // 2 ^ 16 fits within uint32, higher n needs ulong
        private static ulong Encode(int x, int y, int n)
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

        private static void DecodeoPoint(int n, ulong d, out int x, out int y)
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
        private HilbertEnvelope BoundingBoxToHilbertBox(Envelope bounds)
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
            return new HilbertEnvelope(x, y, p, q);
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

        private SearchResult GetRange(char rotation, HilbertEnvelope bounds, int maxRanges)
        {
            if ((bounds.MaxX < 0 && bounds.MaxY < 0) || (bounds.MinX > _N - 1 && bounds.MinY > _N - 1))
                throw new ArgumentException("Bounds outside world");

            var ranges = new List<ulong[]>();
            var boxes = WrapOverlap(bounds);
            foreach (var box in boxes)
            {
                SplitQuad(rotation, (ulong)_N, (ulong)0, (ulong)box.MinX, (ulong)box.MinY, (ulong)box.Height, (ulong)box.Width, ranges);
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
                boxes.Select(box => HilbertBoxToBoundingBox(box.MinX, box.MinY, box.Height, box.Width)).ToList(),
                boxes
            );
        }

        // todo: make range compactation native in this method to avoid level of recursion and CompactRanges method penalties
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
            ulong minTolerance = 1;
            ulong nextMin = ulong.MaxValue;
            int currentLength = ranges.Length;
            while (currentLength > length)
            {
                int index = 0;
                int count = 1;

                for (int i = 1; i < currentLength; i++)
                {
                    // join range if less than tolerance
                    ulong diff = ranges[i][0] - ranges[index][1];
                    if (diff <= minTolerance)
                    {
                        ranges[count - 1][1] = ranges[i][1];
                    }
                    else
                    {
                        if (diff < nextMin)
                            nextMin = diff;

                        ranges[count++] = ranges[i];
                        index = i;
                    }
                }

                minTolerance = nextMin;
                nextMin = ulong.MaxValue;
                currentLength = count;
            }

            Array.Resize<ulong[]>(ref ranges, currentLength);
            return ranges;
        }

        private List<HilbertEnvelope> WrapOverlap(HilbertEnvelope box)
        {
            var boxes = new List<HilbertEnvelope>();
            boxes.Add(box);

            if (!(box.MinX < 0 || box.MinY < 0 || box.MaxX > _N - 1 || box.MaxY > _N - 1))
                return boxes;

            var tmpboxes = new List<HilbertEnvelope>();
            foreach (HilbertEnvelope b in boxes)
            {
                if (b.MinX < 0)
                {
                    if (b.Width + b.MinX > 0)
                    {
                        tmpboxes.Add(new HilbertEnvelope(0, b.MinY, b.Height, b.Width + b.MinX));
                    }

                    tmpboxes.Add(new HilbertEnvelope((_N - 1) + b.MinX, b.MinY, b.Height, b.MinX * -1));
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            foreach (HilbertEnvelope b in boxes)
            {
                if (b.MinY < 0)
                {

                    if (b.Height + b.MinY > 0)
                    {
                        tmpboxes.Add(new HilbertEnvelope(b.MinX, 0, b.Height + b.MinY, b.Width));
                    }
                    else
                    {
                        // dont warp from pole to pole.
                        // todo: spread sideways instead
                        tmpboxes.Add(new HilbertEnvelope(b.MinX, (_N - 1) + b.MinY, b.MinY * -1, b.Width));
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

            foreach (HilbertEnvelope b in boxes)
            {
                if (b.MaxX > _N - 1)
                {
                    tmpboxes.Add(new HilbertEnvelope(b.MinX, b.MinY, b.Height, (_N - 1) - b.MinX));
                    tmpboxes.Add(new HilbertEnvelope(0, b.MinY, b.Height, b.MaxX - (_N - 1)));
                }
                else
                {
                    tmpboxes.Add(b);
                }
            }

            boxes.Clear();
            boxes.AddRange(tmpboxes);
            tmpboxes.Clear();

            foreach (HilbertEnvelope b in boxes)
            {
                if (b.MaxY > _N - 1)
                {
                    tmpboxes.Add(new HilbertEnvelope(b.MinX, b.MinY, (_N - 1) - b.MinY, b.Width));
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

            boxes.Sort((box1, box2) => Encode(box1.MinX, box1.MinY, _N).CompareTo(Encode(box2.MinX, box2.MinY, _N)));
            return boxes;
        }
    }
}