using Nito.AsyncEx.Synchronous;
using RateLimit.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Constraints
{
    public class ComposedConstraint : IResourceConstraint
    {
        IResourceConstraint _first, _second;

        public ComposedConstraint(IResourceConstraint first, IResourceConstraint second)
        {
            _first = first;
            _second = second;
        }

        public bool IsOptimisic(int count)
        {
            return _first.IsOptimisic(count) && _second.IsOptimisic(count);
        }

        public IDisposable? Pass(int count, TimeSpan timeout)
        {
            using var cancToken = new CancellationTokenSource(timeout);

            return PassAsync(count, cancToken.Token).WaitAndUnwrapException();
        }

        public async Task<IDisposable> PassAsync(int count, CancellationToken cancellationToken)
        {
            Task<IDisposable>? firstTask = null, secondTask = null;

            try
            {
                firstTask = _first.PassAsync(count, cancellationToken);
                secondTask = _second.PassAsync(count, cancellationToken);

                await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);

                return new DoubleDisposable(firstTask.Result, secondTask.Result);
            }
            catch
            {
                if(firstTask?.IsCompletedSuccessfully ?? false)
                {
                    firstTask.Result.Dispose();
                }

                if (secondTask?.IsCompletedSuccessfully ?? false)
                {
                    secondTask.Result.Dispose();
                }

                throw;
            }
        }

        public IDisposable? TryPass(int count)
        {
            IDisposable? first = null, second = null;

            try
            {
                first = _first.TryPass(count);
                second = _second.TryPass(count);

                if (first is null || second is null)
                {
                    first?.Dispose();
                    second?.Dispose();

                    return null;
                }

                return new DoubleDisposable(first, second);
            }
            catch
            {
                first?.Dispose();
                second?.Dispose();

                throw;
            }
        }
    }
}
