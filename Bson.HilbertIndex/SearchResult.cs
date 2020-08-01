using System;
using System.Linq;
using System.Collections.Generic;

namespace Bson.HilbertIndex
{
    public class SearchResult
    {
        public SearchResult(ulong[][] ranges, IList<Envelope> bounds, IList<HilbertEnvelope> boxes)
        {
            Ranges = ranges;
            Bounds = bounds;
            Boxes = boxes;
        }
        public ulong[][] Ranges { get; }

        public IList<Envelope> Bounds { get; }

        public IList<HilbertEnvelope> Boxes { get; }
    }
}