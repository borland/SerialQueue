using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dispatch
{
    /// <summary>Implementation of SerialQueue which supports queue.Verify.
    /// The default queue does not support it because there is some performance cost 
    /// and code complexity associated with being able to verify if you're on the correct queue.
    /// Apple's GCD implementation doesn't support "check which queue I'm on", perhaps this is why</summary>
    public class SerialQueueV : SerialQueue, IDispatchQueueV
    {
        [ThreadStatic]
        static IDispatchQueue s_currentQueue;

        /// <summary>Checks whether the currently-executing function is
        /// on this queue, and throw an OperationInvalidException if it is not</summary>
        public void VerifyQueue()
        {
            if (this != s_currentQueue)
                throw new InvalidOperationException("On the wrong queue");
        }

        /// <summary>Wraps a call to ProcessAsync while setting s_currentQueue</summary>
        protected override void ProcessAsync()
        {
            s_currentQueue = this;
            try {
                base.ProcessAsync();
            }
            finally {
                s_currentQueue = null; // DispatchAsync can't run "within" anything else so no need to capture previous queue
            }
        }

        /// <summary>Runs the given action on the queue.
        /// Blocks until the action is fully complete.
        /// This implementation will not switch threads to run the function</summary>
        /// <param name="action">The function to run.</param>
        public override void DispatchSync(Action action)
        {
            var previous = s_currentQueue;
            s_currentQueue = this;
            try {
                base.DispatchSync(action);
            }
            finally {
                s_currentQueue = previous;
            }
        }
    }
}
