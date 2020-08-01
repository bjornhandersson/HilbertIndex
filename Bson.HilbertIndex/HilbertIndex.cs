using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Bson.HilbertIndex
{
    public class HilbertIndex<T> where T : IHilbertIndexable
    {
        // List of items assumed to be sorted according to IHilbertSearchable.Hid
        // Cannot use List<T> due to how binary search is implemented and lack of contravariance in List<T>
        private readonly List<IHilbertIndexable> _items;

        private readonly HilbertCode _hilbertCode;

        private static readonly HilbertComparer s_hilbertComparer = new HilbertComparer();

        private readonly ReaderWriterLockSlim _mutex;

        public HilbertIndex(IEnumerable<T> items)
        {
            // Important! IHilbertSearchable are assumed to be sorted by poi.Hid
            //  we can easily populate the cache sorted so don't spend expensive time sorting here and lets assume it's sorted.
            _items = items.Cast<IHilbertIndexable>().ToList();
            _hilbertCode = new HilbertCode();
            _mutex = new ReaderWriterLockSlim();
        }

        public IEnumerable<T> Within(Coordinate coordinate, int meters)
        {
            var searchEnvelop = GeoUtils.Wgs84.Buffer(coordinate, meters);
            var ranges = _hilbertCode.GetRanges(searchEnvelop);

            var extracted = LockForRead(() => 
                ExtractItems(_items, ranges)
                    .ToList() // Evaluate the Linq expression within the lock!
            );
            return extracted
                .Select(item => new { Item = item, Distance = GeoUtils.Wgs84.Distance(new Coordinate(item.X, item.Y), coordinate) })
                .Where(item => item.Distance <= meters)
                .OrderBy(item => item.Distance)
                .Select(item => item.Item)
                .Cast<T>();
        }

        public IEnumerable<T> NearestNeighbours(Coordinate coordinate)
        {
            ulong searchId = _hilbertCode.Encode(coordinate);
            var extracted = LockForRead(() =>
            {
                ulong neighbourId = FindNeighbourId(_items, searchId);
                var ranges = _hilbertCode.GetRanges(searchId, neighbourId);
                return ExtractItems(_items, ranges)
                    .ToList(); // evalute the Linq within the lock!
            });

            return extracted
                .Select(item => new { Item = item, Distance = GeoUtils.Wgs84.Distance(new Coordinate(item.X, item.Y), coordinate) })
                .OrderBy(item => item.Distance)
                .Select(item => item.Item)
                .Cast<T>();
        }

        private static ulong FindNeighbourId(List<IHilbertIndexable> items, ulong searchHid)
        {
            int index = items.BinarySearch(new Searchable(searchHid), s_hilbertComparer);
            if (index > -1)
            {
                return items[index].Hid;
            }
            else if (~index == items.Count - 1)
            {
                // Matched last
                return items[~index].Hid;
            }
            else
            {
                ulong min = items[~index].Hid;
                ulong max = items[~index + 1].Hid;
                return searchHid - max < min - searchHid ? max : min;
            }
        }

        public void Add(IHilbertIndexable item)
        {
            LockForWrite(() =>
            {
                int index = ~_items.BinarySearch(item, s_hilbertComparer);
                if (index >= _items.Count)
                    _items.Add(item);
                else
                    _items.Insert(index, item);
            });
        }

        public void Remove(T item)
            => LockForWrite(() => _items.Remove(item));

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
                index = index < 0 ? ~index : index;

                // Take items while Hilbert number is less than the range end
                while (index < items.Count && items[index].Hid <= range[1])
                    yield return items[index++];

                // Store next search start (optimization)
                startIndex = index;
            }
        }

        private T1 LockForRead<T1>(Func<T1> read)
        {
            _mutex.EnterReadLock();
            try
            {
                return read();
            }
            finally
            {
                _mutex.ExitReadLock();
            }
        }

        private void LockForWrite(Action write)
        {
            _mutex.EnterWriteLock();
            try
            {
                write();
            }
            finally
            {
                _mutex.ExitWriteLock();
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