using System;
using System.Collections.Generic;
using System.Linq;

namespace Bson.HilbertIndex
{
    public interface IHilbertSearchable
    {
        ulong Hid { get; }

        double X { get; }

        double Y { get; }
    }

    public class HilbertIndex<T> where T : IHilbertSearchable
    {
        // List of items assumed to be sorted according to IHilbertSearchable.Hid
        // Cannot use List<T> due to how binary search is implemented and lack of contravariance in List<T>
        private readonly List<IHilbertSearchable> _items;

        private readonly HilbertCode _hilbertCode;

        public HilbertIndex(IEnumerable<T> items)
        {
            // Important! IHilbertSearchable are assumed to be sorted by poi.Hid
            //  we can easily populate the cache sorted so don't spend expensive time sorting here and lets assume it's sorted.
            _items = items.Cast<IHilbertSearchable>().ToList();
            _hilbertCode = HilbertCode.Default();
        }

        public IEnumerable<T> Within(Coordinate coordinate, int meters)
        {
            var searchBox = CoordinateSystems.Wgs84.Buffer(coordinate, meters);
            var ranges = _hilbertCode.GetRanges(searchBox)
                .Ranges.OrderBy(pair => pair[0]); // already ordered?

            return ExtractItems(_items, ranges)
                .Select(item => (Item: item, distance: CoordinateSystems.Wgs84.Distance(new Coordinate(item.X, item.Y), coordinate)))
                .Where(item => item.distance <= meters)
                .OrderBy(item => item.distance)
                .Select(item => item.Item)
                .Cast<T>();

            // // Super naive aproach which totallly exploaded when having 1M + items
            // return ranges
            //     .SelectMany(range => _pois.SkipWhile(p => p.Hid <= range.First()).TakeWhile(p => p.Hid <= range.Last()))
            //     .Where(p => categories.Any(c => c == p.CategoryId))
            //     .Select(p => (Poi: p, distance: CoordinateSystems.Wgs84.Distance(new Coordinate(p.Longitude, p.Latitude), coordinate)))
            //     .Where(p => p.distance <= meters)
            //     .OrderBy(p => p.distance)
            //     .Select(p => p.Poi);
        }

        private static IEnumerable<IHilbertSearchable> ExtractItems(List<IHilbertSearchable> items, IEnumerable<ulong[]> ranges)
        {
            var hidComparer = new HilbertComparer();
            // Since we know that the ranges are sorted we don't have to search the whole list every time 
            //  -> continue from end of preious segment stored in startIndex
            int startIndex = 0;
            foreach (var range in ranges)
            {
                // Can be optimized by using custom bin search algorithm taking a Func<T, int> to do the comparision
                // Will also get rid of the stupid casting from IHilbertSearchable -> T and we can work on type T all the way
                var searchItem = new Searchable(hid: range[0], x: 0, y: 0);

                // Find index to start search.
                int index = items.BinarySearch(startIndex, items.Count - startIndex, searchItem, hidComparer);
                index = index < 0 ? ~index : index;

                // Take items while Hilbert number is less than the range end
                while (index < items.Count && items[index].Hid <= range[1])
                    yield return items[index++];

                // Store next search start (optimization)
                startIndex = index;
            }
        }

        internal class Searchable : IHilbertSearchable
        {
            public Searchable(ulong hid, double x, double y)
            {
                Hid = hid;
                X = x;
                Y = y;
            }
            public ulong Hid { get; }
            public double X { get; }
            public double Y { get; }
        }

        internal class HilbertComparer : IComparer<IHilbertSearchable>
        {
            public int Compare(IHilbertSearchable left, IHilbertSearchable right)
                => left.Hid.CompareTo(right.Hid);
        }
    }
}