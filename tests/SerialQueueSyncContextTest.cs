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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dispatch.SerialQueueTest
{
    [TestClass]
    public class SerialQueueSyncContextTest
    {
        [TestMethod]
        public void ChainOfAwaitsFollowsTheQueue()
        {
            var q = new SerialQueue(null, SerialQueueFeatures.SynchronizationContext);

            var hits = new List<int>();

            var done = new ManualResetEvent(false);

            q.DispatchAsync(async () => {
                q.VerifyQueue();
                hits.Add(1);

                await Task.Yield();
                q.VerifyQueue();
                hits.Add(2);

                await Task.Yield();

                q.VerifyQueue();
                hits.Add(3);
                done.Set();
            });

            done.WaitOne();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, hits);
        }

        [TestMethod]
        public void ChainOfAwaitsDoesNotFollowQueueIfFeatureIsOff()
        {
            var q = new SerialQueue(null, SerialQueueFeatures.None);

            var hits = new List<int>();

            var done = new ManualResetEvent(false);

            q.DispatchAsync(async () => {
                q.VerifyQueue();
                hits.Add(1);

                await Task.Yield();

                try
                {
                    q.VerifyQueue();
                    hits.Add(2);

                }
                catch (InvalidOperationException e) when (e.Message == "On the wrong queue")
                {
                    hits.Add(99); // indicate this blew up
                }
                done.Set();
            });

            Assert.IsTrue(done.WaitOne(500));
            CollectionAssert.AreEqual(new[] { 1, 99 }, hits);
        }
    }

    [TestClass]
    public class SerialQueueAwaiterTest
    {
        [TestMethod]
        public async Task CanAwaitTheQueueItself()
        {
            var q = new SerialQueue("q1", SerialQueueFeatures.SynchronizationContext);

            Assert.IsNull(SerialQueue.Current);

            await q;

            Assert.AreSame(q, SerialQueue.Current);
        }

        [TestMethod]
        public async Task CanAwaitAcrossManyQueues()
        {
            var q1 = new SerialQueue("q1", SerialQueueFeatures.SynchronizationContext);
            var q2 = new SerialQueue("q2", SerialQueueFeatures.SynchronizationContext);
            var q3 = new SerialQueue("q3", SerialQueueFeatures.SynchronizationContext);

            Assert.IsNull(SerialQueue.Current);

            await q1;

            Assert.AreSame(q1, SerialQueue.Current);

            await q2;

            Assert.AreSame(q2, SerialQueue.Current);

            await q3;

            Assert.AreSame(q3, SerialQueue.Current);
        }

        [TestMethod]
        public async Task AwaitWorksEvenIfExplicitSyncContextIsSet()
        {
            var qFalse = new SerialQueue("q1", SerialQueueFeatures.SynchronizationContext);
            var q = new SerialQueue("q2", SerialQueueFeatures.SynchronizationContext);
            
            Assert.IsNull(SerialQueue.Current);

            SynchronizationContext.SetSynchronizationContext(new DispatchQueueSynchronizationContext(qFalse));

            await q;

            Assert.AreSame(q, SerialQueue.Current);
        }
    }
}