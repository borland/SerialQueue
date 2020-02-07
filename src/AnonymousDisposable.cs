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
using System.Diagnostics;
using System.Threading;

#nullable enable

namespace Dispatch
{
    /// <summary>An object which wraps a lambda function and calls it when disposed</summary>
    public sealed class AnonymousDisposable : IDisposable
    {
        Action? m_action;

        /// <summary>Constructor</summary>
        /// <param name="action">The action to run on dispose</param>
        public AnonymousDisposable(Action action) => m_action = action;

        /// <summary>Runs the given action. 
        /// If Dispose is called multiple times, it will only run the action the first time and silently do nothing thereafter</summary>
        public void Dispose()
            => Interlocked.Exchange(ref m_action, null)?.Invoke();
    }
}
