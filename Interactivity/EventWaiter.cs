﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.EventArgs;
using sisbase.Utils;

namespace sisbase.Interactivity {
    public class EventWaiter<T> : IDisposable {
        private readonly Func<T, bool> pred;
        private readonly TaskCompletionSource<T> taskSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource token;

        public Task<T> Task => taskSource.Task;

        public EventWaiter(Func<T, bool> pred, TimeSpan timeout = default, CancellationToken token = default) {
            this.token = ConcurrencyUtils.PrepareTimeoutToken(timeout, token);
            this.pred = pred;
        }

        public bool Offer(T args) {
            if (token.IsCancellationRequested) {
                taskSource.SetCanceled();
                return true;
            }
            if (pred(args))
                return taskSource.TrySetResult(args);
            return false;
        }

        public void Dispose() {
            token?.Dispose();
        }
    }

    public class EventWaitHandler<T> {
        private List<EventWaiter<T>> waiters = new List<EventWaiter<T>>();

        public void Register(EventWaiter<T> waiter) {
            waiters.Add(waiter);
        }

        public async Task Offer(T args) {
            var toRemove = waiters.Where((waiter => !waiter.Offer(args))).ToList();
            waiters = waiters.Except(toRemove).ToList();
            toRemove.ForEach(waiter => waiter.Dispose());
        }
    }
}