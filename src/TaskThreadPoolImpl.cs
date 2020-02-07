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
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Dispatch
{
    /// <summary>Implementation of IThreadPool which uses Task.Run to schedule async actions</summary>
    public class TaskThreadPool : IThreadPool
    {
        /// <summary>A default singleton instance of TaskThreadPool</summary>
        public static TaskThreadPool Default { get; } = new TaskThreadPool();

        /// <summary>Calls Task.Run to schedule the action into the threadpool</summary>
        /// <param name="action">Function to run</param>
        public void QueueWorkItem(Action action) => Task.Run(action);

        /// <summary>Creates a new System.Threading.Timer</summary>
        /// <param name="dueTime">When the timer will fire</param>
        /// <param name="action">Function to run when the timer fires</param>
        /// <returns>The timer</returns>
        public IDisposable Schedule(TimeSpan dueTime, Action action) => new Timer(_ => action(), null, dueTime, TimeSpan.FromMilliseconds(-1));
    }
}
