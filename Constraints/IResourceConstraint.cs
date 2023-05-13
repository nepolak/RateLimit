using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Constraints
{
    public interface IResourceConstraint
    {
        public Task<IDisposable> PassAsync(int count, CancellationToken cancellationToken);
        public IDisposable? Pass(int count, TimeSpan timeout);

        public IDisposable? TryPass(int count);

        /// <summary>
        /// Not guaranteed to work, it's no more than an optimistic check.
        /// Interface implementation is NOT obligated to implement this, so
        /// it can return false with no condition.
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public bool IsOptimisic(int count);
    }
}
