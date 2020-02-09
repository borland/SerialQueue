// Copyright (c) 2020 Orion Edwards
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

#nullable enable

namespace Dispatch
{
    /// <summary>Implements a serial queue</summary>
    public class SerialQueue : IDispatchQueue
    {
        enum AsyncState
        {
            Idle = 0,
            Scheduled,
            Processing
        }

        static readonly ThreadLocal<Stack<IDispatchQueue>> s_queueStack = new ThreadLocal<Stack<IDispatchQueue>>(
            valueFactory: () => new Stack<IDispatchQueue>(), 
            trackAllValues: false);

        readonly IThreadPool m_threadPool;
        readonly SynchronizationContext? m_syncContext;

        // lock-order: We must never hold both these locks concurrently
        readonly object m_schedulerLock = new object(); // acquire this before adding any async/timer actions
        readonly object m_executionLock = new object(); // acquire this before doing dispatchSync

        readonly List<Action> m_asyncActions = new List<Action>(); // aqcuire m_schedulerLock 
        readonly HashSet<IDisposable> m_timers = new HashSet<IDisposable>(); // acquire m_schedulerLock
        volatile AsyncState m_asyncState = AsyncState.Idle; // acquire m_schedulerLock
        bool m_isDisposed = false; // acquire m_schedulerLock

        /// <summary>Constructs a new SerialQueue backed by the given ThreadPool</summary>
        /// <param name="threadpool">The threadpool to queue async actions to</param>
        public SerialQueue(IThreadPool threadpool, SerialQueueFeatures features)
        {
            m_threadPool = threadpool ?? throw new ArgumentNullException(nameof(threadpool));
            Features = features;

            if (features.HasFlag(SerialQueueFeatures.SynchronizationContext))
                m_syncContext = new DispatchQueueSynchronizationContext(this);
        }

        /// <summary>Constructs a new SerialQueue backed by the default TaskThreadPool</summary>
        public SerialQueue(SerialQueueFeatures features = SerialQueueFeatures.All) : this(TaskThreadPool.Default, features) { }

        /// <summary>Returns the enabled features this serial queue has</summary>
        public SerialQueueFeatures Features { get; }

        /// <summary>This event is raised whenever an asynchronous function (via DispatchAsync or DispatchAfter) 
        /// throws an unhandled exception</summary>
        public event EventHandler<UnhandledExceptionEventArgs>? UnhandledException;

        /// <summary>Checks whether the currently-executing function is
        /// on this queue, and throw an OperationInvalidException if it is not</summary>
        public void VerifyQueue()
        {
            if (s_queueStack == null || !s_queueStack.Value.Contains(this))
                throw new InvalidOperationException("On the wrong queue");
        }

        /// <summary>Schedules the given action to run asynchronously on the queue after dueTime.</summary>
        /// <remarks>The function is not guaranteed to run at dueTime as the queue may be busy, it will run when next able.</remarks>
        /// <param name="dueTime">Delay before running the action</param>
        /// <param name="action">The function to run</param>
        /// <returns>A disposable token which you can use to cancel the async action if it has not run yet.
        /// It is always safe to dispose this token, even if the async action has already run</returns>
        public virtual IDisposable DispatchAfter(TimeSpan dueTime, Action action)
        {
            IDisposable? cancel = null;
            IDisposable? timer = null;

            lock (m_schedulerLock)
            {
                if (m_isDisposed)
                    throw new ObjectDisposedException(nameof(SerialQueue), "Cannot call DispatchAfter on a disposed queue");

                timer = m_threadPool.Schedule(dueTime, () => {
                    lock(m_schedulerLock)
                    {
                        m_timers.Remove(timer!);
                        if (cancel == null || m_isDisposed) // we've been canceled OR the queue has been disposed
                            return;

                        // we must call DispatchAsync while still holding m_schedulerLock to prevent a window where we get disposed at this point
                        cancel = DispatchAsync(action); 
                    }
                });
                m_timers.Add(timer);
            }

            cancel = new AnonymousDisposable(() => {
                lock (m_schedulerLock)
                    m_timers.Remove(timer);

                timer.Dispose();
            });

            return new AnonymousDisposable(() => {
                lock (m_schedulerLock) {
                    if (cancel != null) {
                        cancel.Dispose(); // this will either cancel the timer or cancel the DispatchAsync depending on which stage it's in
                        cancel = null;
                    }
                }
            });
        }

        /// <summary>Schedules the given action to run asynchronously on the queue when it is available</summary>
        /// <param name="action">The function to run</param>
        /// <returns>A disposable token which you can use to cancel the async action if it has not run yet.
        /// It is always safe to dispose this token, even if the async action has already run</returns>
        public virtual IDisposable DispatchAsync(Action action)
        {
            lock (m_schedulerLock)
            {
                if (m_isDisposed)
                    throw new ObjectDisposedException(nameof(SerialQueue), "Cannot call DispatchAsync on a disposed queue");

                m_asyncActions.Add(action);

                if (m_asyncState == AsyncState.Idle)
                {
                    // even though we don't hold m_schedulerLock when asyncActionsAreProcessing is set to false
                    // that should be OK as the only "contention" happens up here while we do hold it
                    m_asyncState = AsyncState.Scheduled;
                    m_threadPool.QueueWorkItem(ProcessAsync);
                }
            }

            return new AnonymousDisposable(() => {
                // we can't "take it out" of the threadpool as not all threadpools support that
                lock (m_schedulerLock)
                    m_asyncActions.Remove(action);
            });
        }
        
        /// <summary>Internal function which runs on the threadpool to execute the actual async actions</summary>
        protected virtual void ProcessAsync()
        {
            bool schedulerLockTaken = false;
            s_queueStack.Value.Push(this);
            try
            {
                Monitor.Enter(m_schedulerLock, ref schedulerLockTaken);
                Debug.Assert(schedulerLockTaken);

                m_asyncState = AsyncState.Processing;
                
                if (m_isDisposed)
                    return; // the actions will have been dumped, there's no point doing anything
                
                while (m_asyncActions.Count > 0)
                {
                    // get the head of the queue, then release the lock
                    var action = m_asyncActions[0];
                    m_asyncActions.RemoveAt(0);
                    Monitor.Exit(m_schedulerLock);
                    schedulerLockTaken = false;

                    // process the action
                    try
                    {
                        lock (m_executionLock) // we must lock here or a DispatchSync could run concurrently with the last thing in the queue
                        {
                            SynchronizationContext? prevContext = null;
                            if (m_syncContext != null)
                            {
                                prevContext = SynchronizationContext.Current;
                                SynchronizationContext.SetSynchronizationContext(m_syncContext);
                            }

                            try
                            {
                                action();
                            }
                            finally // if we set the sync context, we must restore it (even if restoring to null)
                            {
                                if (m_syncContext != null)
                                    SynchronizationContext.SetSynchronizationContext(prevContext);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        // need to execute this outside of lock scope
                        UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(exception));
                    }

                    // now re-acquire the lock for the next thing
                    Debug.Assert(!schedulerLockTaken);
                    Monitor.Enter(m_schedulerLock, ref schedulerLockTaken);
                    Debug.Assert(schedulerLockTaken);
                }
            }
            finally
            {
                m_asyncState = AsyncState.Idle;
                if (schedulerLockTaken)
                    Monitor.Exit(m_schedulerLock);

                s_queueStack.Value.Pop(); // technically we leak the queue stack threadlocal, but it's probably OK. Windows will free it when the thread exits
            }
        }

        /// <summary>Runs the given action on the queue.
        /// Blocks until the action is fully complete.
        /// This implementation will not switch threads to run the function</summary>
        /// <param name="action">The function to run.</param>
        public virtual void DispatchSync(Action action)
        {
            var prevStack = s_queueStack.Value.ToArray(); // there might be a more optimal way of doing this, it seems to be fast enough
            s_queueStack.Value.Push(this);

            bool schedulerLockTaken = false;
            try
            {
                Monitor.Enter(m_schedulerLock, ref schedulerLockTaken);
                Debug.Assert(schedulerLockTaken);

                if (m_isDisposed)
                    throw new ObjectDisposedException(nameof(SerialQueue), "Cannot call DispatchSync on a disposed queue");

                if(m_asyncState == AsyncState.Idle || prevStack.Contains(this)) // either queue is empty or it's a nested call
                {
                    Monitor.Exit(m_schedulerLock);
                    schedulerLockTaken = false;

                    // process the action
                    lock (m_executionLock)
                        action(); // DO NOT CATCH EXCEPTIONS. We're excuting synchronously so just let it throw 
                    return;
                }
                
                // if there is any async stuff scheduled we must also schedule
                // else m_asyncState == AsyncState.Scheduled, OR we fell through from Processing
                var asyncReady = new ManualResetEvent(false);
                var syncDone = new ManualResetEvent(false);
                DispatchAsync(() => {
                    asyncReady.Set();
                    syncDone.WaitOne();
                });
                Monitor.Exit(m_schedulerLock);
                schedulerLockTaken = false;

                try
                {
                    asyncReady.WaitOne();
                    action(); // DO NOT CATCH EXCEPTIONS. We're excuting synchronously so just let it throw
                }
                finally
                {
                    syncDone.Set(); // tell the dispatchAsync it can release the lock
                }
            }
            finally
            {
                if (schedulerLockTaken)
                    Monitor.Exit(m_schedulerLock);

                s_queueStack.Value.Pop(); // technically we leak the queue stack threadlocal, but it's probably OK. Windows will free it when the thread exits
            }
        }

        /// <summary>Shuts down the queue. All unstarted async actions will be dropped,
        /// and any future attempts to call one of the Dispatch functions will throw an
        /// ObjectDisposedException</summary>
        public void Dispose() => Dispose(true);

        /// <summary>Internal implementation of Dispose</summary>
        /// <remarks>We don't have a finalizer (and nor should we) but this method is just following the MS-recommended dispose pattern just in case someone wants to add one in a derived class</remarks>
        /// <param name="disposing">true if called via Dispose(), false if called via a Finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            IDisposable[] timers;
            lock (m_schedulerLock)
            {
                if (m_isDisposed)
                    return; // double-dispose

                m_isDisposed = true;
                m_asyncActions.Clear();

                timers = m_timers.ToArray();
                m_timers.Clear();
            }
            foreach (var t in timers)
                t.Dispose();
        }
    }

    public class DispatchQueueSynchronizationContext : SynchronizationContext
    {
        public DispatchQueueSynchronizationContext(IDispatchQueue queue)
            => Queue = queue;

        public IDispatchQueue Queue { get; }

        public override void Post(SendOrPostCallback d, object state)
            => Queue.DispatchAsync(() => d(state));

        public override void Send(SendOrPostCallback d, object state)
            => Queue.DispatchSync(() => d(state));
    }

    // Use these to turn on and off various features of the serial queue for performance reasons
    [Flags]
    public enum SerialQueueFeatures
    {
        None = 0,
        // Note: if there is a need for it, we could put the Verify/Re-entrant DispatchSync behaviour behind a feature
        // which could improve performance significantly
        SynchronizationContext = 1,

        All = SynchronizationContext
    }
}