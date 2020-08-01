using NUnit.Framework;
using Bson.HilbertIndex;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex.Test
{
    public class Poi : IHilbertSearchable
    {
        private static readonly HilbertCode s_hilbertCode = HilbertCode.Default();

        public Poi(ulong id, double longitude, double latitude, ulong hid, int categoryId)
        {
            Id = id;
            X = longitude;
            Y = latitude;
            Hid = hid;
            CategoryId = categoryId;
        }

        public ulong Id { get; }

        public double X { get; }

        public double Y { get; }

        public ulong Hid { get; }

        public int CategoryId { get; }

        public static Poi Create(ulong id, int categoryId, Coordinate coord)
            => new Poi(id, coord.X, coord.Y, s_hilbertCode.PositionToIndex(coord), categoryId);
    }
}