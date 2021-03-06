using NUnit.Framework;
using Bson.HilbertIndex;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex.Test
{
    [TestFixture]
    public class Tests
    {
        [Test]
        public void GetRange_Should_Give_Ranges_For_Position()
        {
            var indexer = new HilbertCode();

            // Search target
            ulong hid = indexer.Encode(new Coordinate(18, 57));

            // Envelope arround search bound 
            var searchEnvelope = new Envelope(17.99999, 18.00009, 56.99999, 57.00001);

            // Find ranges to search for, providing the seach bounds
            var ranges = indexer.GetRanges(searchEnvelope);

            // Find if the search target is within the ranges of our search target
            bool found = ranges.Any(range => hid >= range[0] && hid <= range[1]);

            // Should be
            Assert.IsTrue(found);
        }

        [Test]
        public void Should_Find_Location_In_Real_World_Coord_System()
        {
            var indexer = new HilbertCode();

            var coordToFind = new Coordinate(18, 57);
            var indexToFind = indexer.Encode(coordToFind);

            // Create a coordinate 1000 meters away from the one we want to find,
            var searchCoord = GeoUtils.Wgs84.Move(coordToFind, meters: 1000, bearing: 0);

            // Create a search envelope (box) extending 1000 meters from the search point (tolerance)
            var hitEnvelope = GeoUtils.Wgs84.Buffer(searchCoord, meters: 1000);

            // Assert the Hilbert index provides ut with the ranges we need to search to find our coordinate when
            // given the envelop including the coorindate of our target.
            bool found = indexer.GetRanges(hitEnvelope).Any(range => indexToFind.Between(range));
            Assert.IsTrue(found);

            // Create a search envelope that should not include the coord we're looking for
            var missEnvelope = GeoUtils.Wgs84.Buffer(searchCoord, meters: 900);

            // Expect a miss
            bool notFound = indexer.GetRanges(missEnvelope).Any(range => indexToFind.Between(range));
            Assert.IsFalse(notFound);
        }

        [Test]
        public void Will_Hit_Cache()
        {
            //
            // Arange

            // Init search structure (cache/index/whatever)
            var index = new HilbertIndex<Poi>(new List<Poi>{
                Poi.Create(1, 1, new Coordinate(18, 57)),
                Poi.Create(2, 1, new Coordinate(18.2, 57)),
                Poi.Create(3, 1, new Coordinate(18.5, 57)),
            }.OrderBy(p => p.Hid));

            // Tolerance in meters
            var toleranceMeters = 100;

            // Coordinate to search from
            var search = new Coordinate(18.2001, 57.0001);

            //
            // Act
            int hits = index.Within(search, toleranceMeters).Count();
            var hit = index.Within(search, toleranceMeters).First();

            //
            // Assert
            var distance = GeoUtils.Wgs84.Distance(new Coordinate(hit.X, hit.Y), search);
            Assert.AreEqual(2, hit.Id);
            Assert.IsTrue(distance < toleranceMeters);
        }

        [Test]
        public void Should_Find_Exact_Match()
        {
            //
            // Arange

            // Init search structure (cache/index/whatever)

            var testData = new List<Poi>{
                Poi.Create(1, 1, new Coordinate(18, 57))
            }.OrderBy(p => p.Hid);

            var index = new HilbertIndex<Poi>(testData);
            var searchCoord = new Coordinate(18, 57);

            // Act
            var hit = index.Within(searchCoord, meters: 100).First();
        }

        [Test]
        public void Test_Index_Should_Find_Edges()
        {
            //
            // Arange

            // Init search structure (cache/index/whatever)

            var testData = new List<Poi>{
                Poi.Create(1, 1, new Coordinate(18, 57)),
                Poi.Create(2, 1, new Coordinate(18.2, 57)),
                Poi.Create(3, 1, new Coordinate(18.5, 57)),
            }.OrderBy(p => p.Hid);

            var index = new HilbertIndex<Poi>(testData);

            // Tolerance in meters
            var toleranceMeters = 100;

            var firstItem = testData.First();
            var firstSearch = GeoUtils.Wgs84.Move(new Coordinate(firstItem.X, firstItem.Y), meters: 20, bearing: 0);

            var lastItem = testData.Last();
            var lastSearch = GeoUtils.Wgs84.Move(new Coordinate(lastItem.X, lastItem.Y), meters: 20, bearing: 0);

            //
            // Act
            var firstHit = index.Within(firstSearch, toleranceMeters).First();
            Assert.AreEqual(firstItem.Id, firstHit.Id);

            var lastHit = index.Within(lastSearch, toleranceMeters).First();
            Assert.AreEqual(lastItem.Id, lastHit.Id);
        }

        [Test]
        public void Test_Index_Should_Support_Duplicated_HilbertIds()
        {
            //
            // Arange

            // Init search structure (cache/index/whatever)

            var testData = new List<Poi>{
                Poi.Create(1, 1, new Coordinate(18.000000001, 57.000000001)),
                Poi.Create(2, 1, new Coordinate(18.000000002, 57.000000002)),
                Poi.Create(3, 1, new Coordinate(18.000000003, 57.000000003)),
            }.OrderBy(p => p.Hid);

            var firstHid = new HilbertCode().Encode(new Coordinate(18.000000001, 57.000000001));

            Assert.IsTrue(testData.All(poi => poi.Hid == firstHid), "Expected same Hid");

            var index = new HilbertIndex<Poi>(testData);

            var firstItem = testData.First();
            var exactMatch = new Coordinate(firstItem.X, firstItem.Y);

            //
            // Act
            var hit = index.Within(exactMatch, meters: 10).First();
            Assert.AreEqual(firstItem.Id, hit.Id);
        }


        [Test]
        public void Index_can_Find_Nearest_Neighbour()
        {
            var testData = new List<Poi>{
                Poi.Create(1, 1, new Coordinate(18, 57)),
                Poi.Create(2, 1, new Coordinate(18.2, 57)),
                Poi.Create(3, 1, new Coordinate(18.5, 57)),
            }.OrderBy(p => p.Hid).ToList();

            var index = new HilbertIndex<Poi>(testData);

            var neareast = index.NearestNeighbours(new Coordinate(18.0001, 57.0001)).First();
            Assert.AreEqual(1, neareast.Id);

            neareast = index.NearestNeighbours(new Coordinate(18.2001, 57.0001)).First();
            Assert.AreEqual(2, neareast.Id);

            neareast = index.NearestNeighbours(new Coordinate(18.5001, 57.0001)).First();
            Assert.AreEqual(3, neareast.Id);

            // Test exact at edges
            neareast = index.NearestNeighbours(new Coordinate(18, 57)).First();
            Assert.AreEqual(1, neareast.Id);

            neareast = index.NearestNeighbours(new Coordinate(18.5, 57)).First();
            Assert.AreEqual(3, neareast.Id);

            var far_a_way = new Coordinate(-74, 41);

            // Coord in New Yourk should be closest to the most western
            neareast = index.NearestNeighbours(new Coordinate(-74, 41)).First();
            Assert.AreEqual(1, neareast.Id);
        }

        [Test]
        public void Index_Should_Performe_On_Large_Collections()
        {
            var random = new Random();
            var stopWatch = new System.Diagnostics.Stopwatch();

            //
            // Arange

            TestContext.WriteLine("Memory before: " + System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);

            // Generate 1 Million Pois
            var testData = Generate(1000000)
                .OrderBy(p => p.Hid)
                .ToList();

            stopWatch.Start();
            var index = new HilbertIndex<Poi>(testData);
            stopWatch.Stop();
            TestContext.WriteLine("Init in: " + stopWatch.ElapsedMilliseconds);
            TestContext.WriteLine("Memory used: " + System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);

            // Tolerance in meters
            var toleranceMeters = 100;

            // Coordinate to search from
            var poiToFind = testData[testData.Count / 2];
            var search = GeoUtils.Wgs84.Move(new Coordinate(poiToFind.X, poiToFind.Y), meters: 20, bearing: 0);

            // Should clock in on about 100 000 searches per second on single thread over 1M pois.
            stopWatch.Reset();
            stopWatch.Start();
            for (int i = 0; i < 100000; i++)
            {
                var hit = index.Within(search, toleranceMeters).FirstOrDefault();
            }
            stopWatch.Stop();
            TestContext.WriteLine("Searched in: " + stopWatch.ElapsedMilliseconds);
        }

        [Test]
        public void Index_Should_Handle_Searches_In_One_Item_Collection()
        {
            var index = new HilbertIndex<Poi>(new List<Poi> {
                Poi.Create(id:1, categoryId: 1, new Coordinate(18.108183, 59.255583))
            });

            var result = index.NearestNeighbours(new Coordinate(18.11139, 59.25455));
            Assert.AreEqual(1, result.Count());

            result = index.Within(new Coordinate(18.11139, 59.25455), meters: 1000);
            Assert.AreEqual(1, result.Count());
        }

        [Test]
        public void Index_Should_Handle_Searches_In_Empty_Collection()
        {
            var index = new HilbertIndex<Poi>(new List<Poi>());

            var result = index.NearestNeighbours(new Coordinate(18.11139, 59.25455));
            Assert.IsEmpty(result);

            result = index.Within(new Coordinate(18.11139, 59.25455), meters: 1000);
            Assert.IsEmpty(result);
        }

        private static IEnumerable<Poi> Generate(int number)
        {
            var hilbertCode = new HilbertCode();
            var random = new Random();
            for (uint i = 0; i < number; i++)
            {
                double x = (random.NextDouble() * 360) - 180;
                double y = (random.NextDouble() * 180) - 90;
                yield return Poi.Create(i, 1, new Coordinate(x, y));
            }
        }
    }

    public static class ComparableExtension
    {
        public static bool Between<TComp>(this TComp number, TComp[] range)
            where TComp : IComparable
                => number.CompareTo(range[0]) >= 0 && number.CompareTo(range[1]) <= 0;
    }
}