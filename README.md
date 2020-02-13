# SerialQueue
C# Implementation of a SerialQueue in the style of Apple's Grand Central Dispatch queues.

Provided as a Nuget package built for Net462 and NetStandard20

## What is a Serial Queue?

[Apple's Grand Central Dispatch library](https://developer.apple.com/library/ios/documentation/General/Conceptual/ConcurrencyProgrammingGuide/OperationQueues/OperationQueues.html)
provides three kinds of "operation queues".

An operation queue is a lightweight abstraction over a thread, and can be used for concurrency and thread safety

Concurrent, "Main" and Serial.

Neither Concurrent nor Main queues are neccessary in .NET as a concurrent queue is basically the same as `Task.Run` or `ThreadPool.QueueUserWorkItem`, and the Main queue is basically the same as `Dispatcher.BeginInvoke` (for WPF) or whatever the equivalent for your particular UI framework happens to be.

Serial queues on the other hand have no built-in equivalent. Apple's documentation describes them:

 > Serial queues (also known as private dispatch queues) execute one task at a time in the order in which they are added to the queue. The currently executing task runs on a distinct thread (which can vary from task to task) that is managed by the dispatch queue. Serial queues are often used to synchronize access to a specific resource. 
 > You can create as many serial queues as you need, and each queue operates concurrently with respect to all other queues. In other words, if you create four serial queues, each queue executes only one task at a time but up to four tasks could still execute concurrently, one from each queue.

## Why would I want to use one?

There are quite a few scenarios where one might like to use a thread to ensure thread-safety of some object (e.g. "All accesses to the private data of X object must be performed on thread Y"), however threads themselves are often too resource-intensive to be able to do that at a smaller scale (e.g. where you have thousands of such objects.) 

To get around this in the past I have seen (and used) things like thread sharing, or fallen back to "full blown" concurrency where things just run randomly on a threadpool and locking must be used everywhere. Both of these options are more complex and dangerous, particularly full blown concurrency where all methods must all lock correctly.

Serial queues offer a very interesting option in the "middle" of these two spaces.

 - They are very lightweight (each queue is literally not much more than a List and a few lock objects), so you can easily have thousands of them
 - They have a usage model which is similar to threads and is easier to reason about

# Example

```csharp
var q = new SerialQueue();
q.DispatchAsync(() => {
    Console.WriteLine("a");
});
q.DispatchAsync(() => {
    Console.WriteLine("b");
});
```

In the above example, both operations are guaranteed to execute in-order and guaranteed not to execute at the same time.
Thus, the actions are thread-safe and easy to reason about.

The actual execution of the functions is managed by the built-in .NET ThreadPool (the default implementation just uses `Task.Run`) so many thousands of queues will be backed by perhaps 8 or so underlying OS threads in the threadpool

## Enhancements for .NET

This serial queue supports async/await - i.e. if you are running within the context of a serial queue, then it will be captured across Async/Await

```csharp
var q = new SerialQueue();
q.DispatchAsync(async () => {
    // we are on the queue
        
    var response = await SomeNetworkRequest();

    // we are still on the queue, it was captured by the await
});
```

You can also await the queue itself directly in order to "jump" to it if you are in an existing async method

```csharp
SerialQueue m_queue = new SerialQueue();

// imagine this is a WPF or winforms app
public void Button_Click()
{
    // here we are on the UI main thread
    var result = await DoBackgroundProcessing();

    // and we are still on the UI thread because 'await DoBackgroundProcessing' captured the sync context.
    MyTextBox.Text = result;
}

private async Task<string> DoBackgroundProcessing()
{
    // at this point we are still on the UI main thread
        
    await m_queue;

    // now we are OFF the main UI thread and onto the serial queue (behind the scenes we're on a threadpool thread)

    var response = await SendNetworkRequest();

    // still on the serial queue

    return response;
}
```

More Documentation is available on the [Github wiki](https://github.com/borland/SerialQueue/wiki)
