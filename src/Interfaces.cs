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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#nullable enable

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
        
        /// <summary>Checks whether the currently-executing function is
        /// on this queue, and throw an OperationInvalidException if it is not</summary>
        void VerifyQueue();

        /// <summary>Returns the display-name of the queue (if one is set) for diagnostic purposes</summary>
        string? Name { get; }
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

    /// <summary>Useful extension methods for queues</summary>
    public static class IDispatchQueueExtensions
    {
        /// <summary>A wrapper over DispatchSync that calls a value-producing function and returns it's result</summary>
        /// <typeparam name="T">Result type</typeparam>
        /// <param name="queue">The queue to execute the function on</param>
        /// <param name="func">The function to execute</param>
        /// <returns>The return value of func</returns>
        public static T DispatchSync<T>(this IDispatchQueue queue, Func<T> func)
        {
#pragma warning disable CS8653 // The value will be assigned by the lambda but the compiler can't infer that
            T result = default;
#pragma warning restore CS8653
            queue.DispatchSync(() => { result = func(); });
            return result;
        }

        /// <summary>A wrapper over DispatchAsync that calls a value-producing function on the queue and returns it's result via a Task</summary>
        /// <remarks>Note: the task "completes" on the dispatch queue, so if you use ConfigureAwait(false) or
        /// TaskContinuationOptions.ExecuteSychronously, you will still be running on the dispatch queue</remarks>
        /// <typeparam name="T">Result type</typeparam>
        /// <param name="queue">The queue to execute the function on</param>
        /// <param name="func">The function to execute</param>
        /// <returns>A task wrapping the return value (or exception) the func produced.</returns>
        public static Task<T> DispatchAsync<T>(this IDispatchQueue queue, Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            queue.DispatchAsync(() => {
                try {
                    tcs.SetResult(func());
                }
                catch (Exception e) {
                    tcs.TrySetException(e);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// This allows you await directly on a queue, which is handy for queue-jumping if you are already in the middle of an async method.
        /// </summary>
        /// <param name="queue">The queue to jump to with your await statement</param>
        public static DispatchQueueAwaiter GetAwaiter(this IDispatchQueue queue) => new DispatchQueueAwaiter(queue);

        public struct DispatchQueueAwaiter : INotifyCompletion
        {
            readonly IDispatchQueue m_queue;

            public DispatchQueueAwaiter(IDispatchQueue queue)
            {
                m_queue = queue;
                IsCompleted = false;
            }

            public bool IsCompleted { get; }

            public void OnCompleted(Action continuation) => m_queue.DispatchAsync(continuation); // can't cancel here

            public void GetResult() { }

        }
    }
}
