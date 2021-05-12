using System;
using System.Collections.Generic;
using System.Linq;

namespace Bson.HilbertIndex
{
    public class HilbertIndex<T> where T : IHilbertIndexable
    {
        // List of items assumed to be sorted according to IHilbertSearchable.Hid
        // Cannot use List<T> due to how binary search is implemented and lack of contravariance in List<T>
        private readonly List<IHilbertIndexable> _items;

        private readonly HilbertCode _hilbertCode;

        private static readonly HilbertComparer s_hilbertComparer = new HilbertComparer();

        /// <summary>
        /// Create a new index with a set of IHilbertIndexable
        /// </summary>
        /// <param name="items">A set of IHilbertIndexable sorted according to IHilbertIndexable.Hid</param>
        public HilbertIndex(IEnumerable<T> items)
        {
            // Important! IHilbertSearchable are assumed to be sorted by IHilbertIndexable.Hid
            //  we can easily populate the cache sorted so don't spend expensive time sorting here and lets assume it's sorted.
            _items = items.Cast<IHilbertIndexable>().ToList();
            _hilbertCode = new HilbertCode();
        }

        /// <summary>
        /// Find items within the given distance in meter from the given Coordinate
        /// </summary>
        /// <param name="coordinate">Coordinate center of the search</param>
        /// <param name="meters">Distance in meters from the given coordinate to search</param>
        /// <returns>A set of IHilbertIndexable matcing the search ordered by distance according to Wgs84</returns>
        public IEnumerable<T> Within(Coordinate coordinate, int meters)
        {
            var searchEnvelop = GeoUtils.Wgs84.Buffer(coordinate, meters);
            var ranges = _hilbertCode.GetRanges(searchEnvelop);

            return ExtractItems(_items, ranges)
                .Select(item => new { Item = item, Distance = GeoUtils.Wgs84.Distance(new Coordinate(item.X, item.Y), coordinate) })
                .Where(item => item.Distance <= meters)
                .OrderBy(item => item.Distance)
                .Select(item => item.Item)
                .Cast<T>();
        }

        /// <summary>
        /// Find the nearest neighbours to a given coordinate regardless of the distance.
        /// If more than one neighbour is found in the hilbet space, the result is sorted by Wgs84 distance, where the nearest is is first.
        /// </summary>
        /// <param name="coordinate">Coordinate of the search</param>
        /// <returns>A set of IHilbertIndexable matcing the search ordered by distance according to Wgs84</returns>
        public IEnumerable<T> NearestNeighbours(Coordinate coordinate)
        {
            if (!_items.Any())
            {
                return Enumerable.Empty<T>();
            }

            ulong search1D = _hilbertCode.Encode(coordinate);
            int index = _items.BinarySearch(new Searchable(search1D), s_hilbertComparer);

            ulong neighbour1D = 0;

            if (index > -1)
            {
                neighbour1D = _items[index].Hid;
            }
            // Matched last (or last + 1 meaning)
            else if (~index >= _items.Count - 1)
            {
                neighbour1D = _items[_items.Count - 1].Hid;
            }
            else
            {
                ulong min = _items[~index].Hid;
                ulong max = _items[~index + 1].Hid;
                neighbour1D = search1D - max < min - search1D ? max : min;
            }

            var ranges = _hilbertCode.GetRanges(search1D, neighbour1D);
            return ExtractItems(_items, ranges)
                .Select(item => new { Item = item, Distance = GeoUtils.Wgs84.Distance(new Coordinate(item.X, item.Y), coordinate) })
                .OrderBy(item => item.Distance)
                .Select(item => item.Item)
                .Cast<T>();
        }

        private static IEnumerable<IHilbertIndexable> ExtractItems(List<IHilbertIndexable> items, IEnumerable<ulong[]> ranges)
        {
            // Since we know that the ranges are sorted we don't have to search the whole list every time 
            //  -> continue from end of preious segment stored in startIndex
            int startIndex = 0;
            foreach (var range in ranges.OrderBy(pair => pair[0]))
            {
                // Can be optimized by using custom bin search algorithm taking a Func<T, int> to do the comparision
                // Will also get rid of the stupid casting from IHilbertSearchable -> T and we can work on type T all the way
                var searchItem = new Searchable(hid: range[0]);

                // Find index to start search.
                int index = items.BinarySearch(startIndex, items.Count - startIndex, searchItem, s_hilbertComparer);

                // Got exact match. 
                // To support items having duplicated hilbertIds, we "scan down" to find the first occurence of the matched hid
                if (index > -1)
                {
                    ulong hid = items[index].Hid;
                    int scanIndex = index - 1;
                    while (scanIndex >= 0 && items[scanIndex].Hid == hid)
                        index = scanIndex -= 1;
                }

                index = index < 0 ? ~index : index;

                // Take items while Hilbert number is less than the range end
                while (index < items.Count && items[index].Hid <= range[1])
                    yield return items[index++];

                // Store next search start (optimization)
                startIndex = index;
            }
        }

        private class Searchable : IHilbertIndexable
        {
            public Searchable(ulong hid) => Hid = hid;
            public ulong Hid { get; }
            public double X => 0;
            public double Y => 0;
        }

        private class HilbertComparer : IComparer<IHilbertIndexable>
        {
            public int Compare(IHilbertIndexable left, IHilbertIndexable right)
                => left.Hid.CompareTo(right.Hid);
        }
    }
}