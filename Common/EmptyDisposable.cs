using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Common
{
    public sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
            
        }
    }
}
