using Nito.AsyncEx;
using RateLimit.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Constraints
{
    public sealed class LeakingTokensIntervalConstraint : IResourceConstraint, IDisposable
    {
        readonly Timer _timer;

        readonly AsyncMonitor _monitor = new();

        int _currentTokens;
        readonly int _totalTokens;

        public LeakingTokensIntervalConstraint(int currentTokens, int totalTokens, TimeSpan interval)
        {
            int intervalMs = ((int)interval.TotalMilliseconds) / totalTokens;

            if(intervalMs <= 0 || 
                intervalMs > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval));

            _currentTokens = currentTokens;
            _totalTokens = totalTokens;

            _timer = new(TimerAction, null, intervalMs, intervalMs);
        }

        void TimerAction(object? nothing)
        {
            using var lk = _monitor.Enter();

            _currentTokens = Math.Min(_totalTokens, _currentTokens + 1);

            _monitor.Pulse();
        }

        public IDisposable? Pass(int count, TimeSpan timeout)
        {
            using var cancToken = new CancellationTokenSource(timeout);

            try
            {
                return PassAsync(count, cancToken.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<IDisposable> PassAsync(int count, CancellationToken cancellationToken)
        {
            int takenPoints = 0;

            using (var monLock = _monitor.Enter(cancellationToken))
            {
                try
                {
                    while (takenPoints != count)
                    {
                        var available = Math.Min(_currentTokens, count - takenPoints);
                        if (available > 0)
                        {
                            takenPoints += available;
                            _currentTokens -= available;

                            continue;
                        }

                        await _monitor.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    _currentTokens = Math.Min(_totalTokens, _currentTokens + takenPoints);

                    throw;
                }
            }

            return new EmptyDisposable();
        }

        public IDisposable? TryPass(int count)
        {
            using (var monLock = _monitor.Enter(CancellationToken.None))
            {
                if (_currentTokens < count)
                    return null;

                _currentTokens -= count;
            }

            return new EmptyDisposable();
        }
        public bool IsOptimisic(int count) => _currentTokens >= count;

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
