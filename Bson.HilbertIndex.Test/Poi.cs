using NUnit.Framework;
using Bson.HilbertIndex;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex.Test
{
    public class Poi : IHilbertIndexable
    {
        private static readonly HilbertCode s_hilbertCode = new HilbertCode();

        public Poi(uint id, double longitude, double latitude, ulong hid, int categoryId)
        {
            Id = id;
            X = longitude;
            Y = latitude;
            Hid = hid;
            CategoryId = categoryId;
        }

        public uint Id { get; }

        public double X { get; }

        public double Y { get; }

        public ulong Hid { get; }

        public int CategoryId { get; }

        public override int GetHashCode() => Id.GetHashCode();

        public override bool Equals(object obj) => obj is Poi _poi && _poi.Id == Id;

        public static Poi Create(uint id, int categoryId, Coordinate coord)
            => new Poi(id, coord.X, coord.Y, s_hilbertCode.Encode(coord), categoryId);

    }
}