using Nito.AsyncEx;
using Nito.AsyncEx.Synchronous;
using Nito.Collections;
using RateLimit.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RateLimit.Constraints
{
    public class StashIntervalConstraint : IResourceConstraint
    {
        AsyncMonitor _monitor = new();

        int _currentCount;

        TimeSpan _interval;

        Deque<(DateTime, int)> _stashed = new();

        public StashIntervalConstraint(int currentCount, TimeSpan interval)
        {
            _stashed = new Deque<(DateTime, int)>();

            _currentCount = currentCount;

            _interval = interval;
        }

        void OnUtilizeEnd(int points)
        {
            using var monitorLock = _monitor.Enter();

            var cooldown = DateTime.Now + _interval;

            _stashed.AddToBack((cooldown, points));

            _monitor.Pulse();
        }

        (DateTime, int) TakeStashedDeferred(int requiredAmount)
        {
            DateTime waitUntil = DateTime.UnixEpoch;

            int collectedAmount = 0;

            int passCount = 0;

            for (int i = 0; i < _stashed.Count; i++)
            {
                var (cooldown, worth) = _stashed[i];

                if (collectedAmount >= requiredAmount) // here we try to free as much as possible. If cannot, just leave.
                {
                    Debug.Assert(collectedAmount == requiredAmount);

                    if (cooldown > DateTime.Now)
                        break;

                    _currentCount += worth;

                    continue;
                }

                waitUntil = cooldown;

                if (collectedAmount + worth > requiredAmount)
                {
                    var neededAmount = requiredAmount - collectedAmount;

                    collectedAmount += neededAmount; //consume the part we need

                    _currentCount += worth - neededAmount; //contribute.
                }

                collectedAmount += worth; // consume this node.
                passCount++;
            }

            _stashed.RemoveRange(0, passCount);

            return (waitUntil, collectedAmount);
        }

        int TakeStashedReady(int requiredAmount)
        {
            int collectedAmount = 0;

            int passCount = 0;

            for (int i = 0; i < _stashed.Count; i++)
            {
                var (cooldown, worth) = _stashed[i];
                if (cooldown > DateTime.Now)
                    return 0;

                if (collectedAmount + worth > requiredAmount)
                {
                    var neededAmount = requiredAmount - collectedAmount;

                    collectedAmount += neededAmount;

                    _stashed[i].Item2 = worth - neededAmount;

                    break;
                }

                collectedAmount += worth; // consume this node.
                passCount++;

                if (collectedAmount == requiredAmount)
                    break;

                Debug.Assert(collectedAmount < requiredAmount);
            }

            _stashed.RemoveRange(0, passCount);

            return collectedAmount;
        }

        int TakeFree(int requiredAmount)
        {
            var available = Math.Min(requiredAmount, _currentCount);

            _currentCount -= available;

            return available;
        }

        Task WaitForAsync(TimeSpan time)
        {
            if (time <= TimeSpan.Zero)
                return Task.CompletedTask;

            var totalMs = Math.Max((int)time.TotalMilliseconds, 1);

            return Task.Delay(totalMs);
        }

        async ValueTask AsyncWaitLoop(int requiredTokens, CancellationToken cancellationToken)
        {
            int collectedTokens = 0;

            DateTime haveToWait = DateTime.UnixEpoch;

            var capturedMonitor = _monitor.Enter(cancellationToken);

            try
            {

                while (collectedTokens != requiredTokens)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Constraint", cancellationToken);
                    }

                    var immediate = TakeFree(requiredTokens - collectedTokens);

                    collectedTokens += immediate;

                    if (collectedTokens == requiredTokens)
                    {
                        break;
                    }

                    var (waitTo, awaitedTokens) = TakeStashedDeferred(requiredTokens - collectedTokens);
                    if (awaitedTokens > 0)
                    {
                        if (waitTo > haveToWait)
                        {
                            haveToWait = waitTo;
                        }

                        collectedTokens += awaitedTokens;
                    }

                    if (collectedTokens == requiredTokens)
                    {
                        break;
                    }

                    await _monitor.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                _currentCount += collectedTokens; // free the captured tokens.

                throw;
            }
            finally
            {
                capturedMonitor.Dispose();
            }

            await WaitForAsync(haveToWait - DateTime.Now).ConfigureAwait(false);
        }

        bool TryTake(int requiredTokens)
        {
            using var capturedMonitor = _monitor.Enter();

            int collectedTokens = 0;
            var immediate = TakeFree(requiredTokens);

            collectedTokens += immediate;

            if (collectedTokens == requiredTokens)
            {
                return true;
            }

            var awaitedTokens = TakeStashedReady(requiredTokens - collectedTokens);
            if (awaitedTokens == 0)
            {
                _currentCount += collectedTokens;

                return false;
            }

            return true;
        }

        public async Task<IDisposable> PassAsync(int count, CancellationToken cancellationToken)
        {
            await AsyncWaitLoop(count, cancellationToken).ConfigureAwait(false);

            return new DisposeAction(() => OnUtilizeEnd(count));
        }

        public IDisposable? Pass(int count, TimeSpan timeout)
        {
            var deadline = DateTime.Now + timeout;

            using (var cancellationTokenSource = new CancellationTokenSource(timeout))
            {
                try
                {
                    AsyncWaitLoop(count, cancellationTokenSource.Token).AsTask()
                                                                       .WaitAndUnwrapException();
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (TimeoutException)
                {
                    return null;
                }
            }

            return new DisposeAction(() => OnUtilizeEnd(count));
        }

        public IDisposable? TryPass(int count)
        {
            if (!TryTake(count))
            {
                return null;
            }

            return new DisposeAction(() => OnUtilizeEnd(count));
        }

        public bool IsOptimisic(int count) => _currentCount >= count;
    }
}
