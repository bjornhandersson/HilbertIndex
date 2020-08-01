using System;
using System.Collections.Generic;
using System.Linq;

namespace Bson.HilbertIndex
{
    // Todo: rename to indecies
    public interface IHilbertIndexable
    {
        ulong Hid { get; }

        double X { get; }

        double Y { get; }
    }
}