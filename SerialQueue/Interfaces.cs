// Copyright (c) 2015 Orion Edwards
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

namespace Dispatch
{
    /// <summary>Represents a queue which we can use to post functions to</summary>
    public interface IDispatchQueue : IDisposable
    {
        /// <summary>Schedules the given action to run asynchronously on the queue when it is available</summary>
        /// <param name="action">The function to run</param>
        /// <returns>A disposable token which you can use to cancel the async action if it has not run yet.
        /// It is always safe to dispose this token, even if the async action has already run</returns>
        IDisposable DispatchAsync(Action action);

        /// <summary>Schedules the given action to run asynchronously on the queue after dueTime.</summary>
        /// <remarks>The function is not guaranteed to run at dueTime as the queue may be busy, it will run when next able.</remarks>
        /// <param name="dueTime">Delay before running the action</param>
        /// <param name="action">The function to run</param>
        /// <returns>A disposable token which you can use to cancel the async action if it has not run yet.
        /// It is always safe to dispose this token, even if the async action has already run</returns>
        IDisposable DispatchAfter(TimeSpan dueTime, Action action);

        /// <summary>Runs the given action on the queue.
        /// Blocks until the action is fully complete.
        /// Implementations reserve the right to run the action on a different thread (e.g WPF Dispatcher)</summary>
        /// <param name="action">The function to run.</param>
        void DispatchSync(Action action);
    }

    /// <summary>Queues can implement this interface to enable the VerifyQueue extension method which
    /// throws an exception if your code is not on the correct queue</summary>
    public interface IDispatchQueueV
    {
        /// <summary>Checks whether the currently-executing function is
        /// on this queue, and throw an OperationInvalidException if it is not</summary>
        void VerifyQueue();
    }

    /// <summary>A serial queue needs a threadpool to run tasks on. You can provide your own implementation if you want to have a custom threadpool with it's own limits (e.g. no more than X concurrent threads)</summary>
    public interface IThreadPool
    {
        /// <summary>The serial queue uses this to post an async action to the threadpool</summary>
        /// <param name="action">The function to run</param>
        void QueueWorkItem(Action action);

        /// <summary>The serial queue uses this to create a timer. Doesn't have to be related to the the threadpool, just needs to provide some form of timer</summary>
        /// <param name="dueTime">When the timer should fire</param>
        /// <param name="action">The function to run after the timer elapses</param>
        /// <returns>A disposable token to cancel the timer</returns>
        IDisposable Schedule(TimeSpan dueTime, Action action);
    }

}
