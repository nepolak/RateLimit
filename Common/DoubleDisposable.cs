using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Common
{
    public sealed class DoubleDisposable : IDisposable
    {
        readonly IDisposable _first, _second;

        public DoubleDisposable(IDisposable first, IDisposable second)
        {
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            _first.Dispose();
            _second.Dispose();
        }
    }
}
