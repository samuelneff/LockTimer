# LockTimer

Replacement for C#' `lock()` statement that provides timing for lock metrics and tracking down lock contention

# Usage

To use `LockTimer` replace your `lock()` calls with an instantiation of `LockTimer` inside a `using` statement.

This old code:

```csharp
lock (sync) {
   // ...
}
```

becomes

```csharp
using (new LockTimer("Cache.Get", sync)) {
  // ...
}
```

And from the usage perspective it works exactly the same as `lock()` did.

Now when you want to enable lock timing, add this to your start-up routine somewhere:

```csharp
// Turn on logging
LockTimer.EnableLogging = true;

// Optional, change time to cache messages before logging, default is 10,000 milliseconds
LockTimer.CacheMilliseconds = 500;

// Optional, set where to log, default is temp directory
LockTimer.LogPath = Environment.CurrentDirectory;
```

# Minimum elapsed time

While internally all timing is done with a high-frequency timer, logging is all done in millisecond precision. For any lock where the entire time taken to enter the lock, perform the action inside the lock, and exit the lock is less than one millisecond, then that lock iteration is not logged. 

In my own usage, even in an application with a lot of locking and lock contention, more than 90% of the locks still fall into this non-logged category.

# Log file

When you've enabled logging and run through some code you'll get a log file named by the time, `LockTimer-2015-10-30--T--14-59-27.log` and the contents are CSV with a heading.

```
TimeStart,ThreadId,ThreadName,LockName,LockHash,LockTaken,EnterTotal,InsideTotal,GrandTotal,PreEnter,PostEnter,PreExit,PostExit
2015-10-30 14:59:26.5552,13,Thread  4,Index  2,139a378,1,0,633,633,104803299,104803299,104803932,104803932
2015-10-30 14:59:26.3472,11,Thread  0,Index  3,7a01c0,1,0,1400,1400,104803091,104803091,104804491,104804491
2015-10-30 14:59:26.5582,18,Thread  3,Index  2,139a378,1,630,633,1263,104803302,104803932,104804565,104804565
2015-10-30 14:59:26.5562,17,Thread  5,Index  2,139a378,1,1265,633,1898,104803300,104804565,104805198,104805198
2015-10-30 14:59:26.5602,16,Thread  7,Index  2,139a378,1,1894,633,2527,104803304,104805198,104805831,104805831
2015-10-30 14:59:26.9412,19,Thread  8,Index  0,3acffc2,1,0,2165,2165,104803685,104803685,104805850,104805850
2015-10-30 14:59:26.1952,12,Thread  1,Index  1,22ea309,1,0,3302,3302,104802940,104802940,104806242,104806242
2015-10-30 14:59:26.5612,14,Thread  6,Index  2,139a378,1,2526,633,3159,104803305,104805831,104806464,104806464
2015-10-30 14:59:28.3972,11,Thread  0,Index  3,7a01c0,1,0,1798,1798,104805141,104805141,104806939,104806939
2015-10-30 14:59:28.4672,18,Thread  3,Index  2,139a378,1,1253,902,2155,104805211,104806464,104807366,104807366
```

This data is all from a demo app with random locks and sleeps.

# Columns

**TimeStart**. Date and time corresponding to when the lock timer was instantiated.

**ThreadId**. Managed thread id.

**ThreadName**. Name of the thread, with any comma or quote removed (for simplified CSV safety). 

**LockName**. Name passed into the lock timer instance as the name of the lock. Can be anything that helps you with logging and analysis.

**LockHash**. Hash code of the object being locked on. 

**LockTaken**. 1 or 0 if the lock was taken. If an error is generated acquiring the lock, this will be zero. In practice I've never actually seen it be zero.

**EnterTotal**. Milliseconds spent waiting to enter the lock.

**InsideTotal**. Milliseconds spent inside the lock.

**GrandTotal**. Total milliseconds spent acquiring, inside, and exiting the lock.

**PreEnter**. Timer for right before we enter the lock. Milliseconds precision but the absolute value is meaningless--it's useful in relation to other values within an individual run of the application (log file).

**PostEnter**. Timer for right after we enter the lock. EnterTotal is PostEnter - PreEnter.

**PreExit**. Timer for right before we leave the lock. InsideTotal is PreExit - PostEnter.

**PostExit**. Timer for right after we leave the lock.

# Analyzing the logs

I find it easiest to analyze logs in SQL. To import the log file into a SQLite database, use the following:

```
create table L(
			TimeStart text,
			ThreadId int,
			ThreadName text,
			LockName text,
			LockHash text,
			LockTaken int,
			EnterTotal int,
			InsideTotal int,
			GrandTotal int,
			PreEnter int,
			PostEnter int,
			PreExit int,
			PostExit int);
.separator ","
.import locks.log L
delete from L where TimeStart = 'TimeStart';
```

Then you can run a query, such as this one to find the highest contention lock names:

```
SELECT LockName, SUM(EnterTotal) SumEnterTotal
FROM L
GROUP BY LockName
ORDER BY SumEnterTotal DESC
LIMIT 5;
```

Results in demo:

```
LockName    SumEnterTotal
----------  -------------
Index  1    92036
Index  2    45462
Index  0    31151
Index  3    25447
Index  4    0
```

Now I can use this data and look at the lock names `Index  1`, find that in my code, and I know that my application spent 92 seconds waiting to enter that lock. Is there work being done inside the lock that doesn't need to be? Are multiple things locking on the same object when they can actually use something finer grained? Could we come up with a lock-free solution entirely? This one lock can possibly save 92 seconds of waiting.

# LockName vs LockHash

It's important to understand the difference between `LockName` and `LockHash`. 

`LockName` is the string name that was passed into `LockTimer`. In my own usage I've generally associated this with the class and method where the lock is acquired. This can primarily be used to identify where in the code each lock is used.

`LockHash` is the hash code of the object we're actually locking. If we have a lock on a single object and only lock that object in one place, then `LockHash` and `LockName` will refer to the same concept. Usually though a single object is the target of a lock in multiple places within a class (and sometimes across multiple classes, but that's generally a really bad thing to do). 

For full analysis look at both `LockName` and `LockHash` independently and how they correspond to each other in your application. For example, if you find that a specific place in code has a lot of lock contention but the actual operation inside the lock can lock something finer grained, then you can create a collection of lock objects, grab the one that corresponds to the current operation, and lock on that less-used object instead. This reduces contention and for logging it will result in a single `LockName` corresponding to multiple `LockHash`es (whereas the reverse is more common).

