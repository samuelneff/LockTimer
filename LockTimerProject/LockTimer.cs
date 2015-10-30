using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LockTimerProject
{
    public class LockTimer : IDisposable
    {
        internal static string LogFilePath;
        internal static volatile bool KeepWriting = true; // only meant for unit tests

        private readonly string _name;
        private object _sync;
        private readonly bool _lockTaken;
        private readonly int _threadId;
        private readonly string _threadName;
        private DateTime _start;
        private long _preEnter;
        private long _postEnter;
        private long _preExit;
        private long _postExit;
        private int _syncHash;
        private static ILockTimerWriter _writer = new LockTimerDisabled();
        private static string _logPath;
        private static int _cacheMilliseconds = 10000;

        public static bool EnableLogging
        {
            get
            {
                return _writer is LockTimerEnabled;
            }
            set
            {
                _writer = value
                          ? (ILockTimerWriter)new LockTimerEnabled()
                          : new LockTimerDisabled();
            }            
        }

        public static string LogPath
        {
            get
            {
                return _logPath ?? (_logPath = Path.GetTempPath());
            }
            set
            {
                _logPath = value;
            }
        }

        public static int CacheMilliseconds
        {
            get
            {
                return _cacheMilliseconds;
            }
            set
            {
                _cacheMilliseconds = value;
            }
        }

        public LockTimer(string name, object sync)
        {
            _name = name;
            _sync = sync;

            if (sync == null)
            {
                return;
            }

            _threadId = Thread.CurrentThread.ManagedThreadId;
            _threadName = Thread.CurrentThread.Name ?? _threadId.ToString(CultureInfo.InvariantCulture);
            _start = DateTime.Now;
            _preEnter = Stopwatch.GetTimestamp();
            Monitor.Enter(_sync, ref _lockTaken);
            _postEnter = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            _preExit = Stopwatch.GetTimestamp();
            
            if (_lockTaken)
            {
                Monitor.Exit(_sync);
            }
            
            _postExit = Stopwatch.GetTimestamp();

            if (_postExit - _preEnter > Stopwatch.Frequency / 1000)
            {
                _writer.Write(this);
            }
        }

        private interface ILockTimerWriter
        {
            void Write(LockTimer lockTimer);
        }

        private class LockTimerDisabled : ILockTimerWriter
        {
            public void Write(LockTimer lockTimer)
            {
                // nothing, disabled
            }
        }

        private class LockTimerEnabled : ILockTimerWriter
        {
            private readonly ConcurrentQueue<LockTimer> _pendingWrites = new ConcurrentQueue<LockTimer>();

            public LockTimerEnabled()
            {
                LogFilePath = Path.Combine(LogPath,
                                            String.Format("LockTimer-Verbose-{0:yyyy-MM-dd--T--HH-mm-ss}.log",
                                                          DateTime.Now));

                new Thread(ThreadEnter) {
                                        IsBackground = true,
                                        Name = "LockWriter"
                                        }
                .Start();
            }

            public void Write(LockTimer lockTimer)
            {
                lockTimer._syncHash = lockTimer._sync == null ? 0 : lockTimer._sync.GetHashCode();
                lockTimer._sync = null;
                _pendingWrites.Enqueue(lockTimer);
            }

            private void ThreadEnter()
            {
                File.WriteAllText(LogFilePath, "TimeStart,ThreadId,ThreadName,LockName,LockHash,LockTaken,EnterTotal,InsideTotal,GrandTotal,PreEnter,PostEnter,PreExit,PostExit" + Environment.NewLine);

                while (KeepWriting)
                {
                    Thread.Sleep(CacheMilliseconds);
                    WriteLockLogs();
                }

                // ReSharper disable once FunctionNeverReturns
            }

            private void WriteLockLogs()
            {
                if (_pendingWrites.IsEmpty)
                {
                    return;
                }

                using (var fileWriter = File.Open(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    using (var streamWriter = new StreamWriter(fileWriter, Encoding.UTF8))
                    {
                        LockTimer timer;

                        while (_pendingWrites.TryDequeue(out timer))
                        {
                            var frequencyMilliseconds = Stopwatch.Frequency / 1000;

                            timer._preEnter /= frequencyMilliseconds;
                            timer._postEnter /= frequencyMilliseconds;
                            timer._preExit /= frequencyMilliseconds;
                            timer._postExit /= frequencyMilliseconds;

                            var enterTotal = timer._postEnter - timer._preEnter;
                            var insideTotal = timer._preExit - timer._postEnter;
                            var grandTotal = timer._postExit - timer._preEnter;

                            var line = String.Join(",",
                                                   timer._start.ToString("yyyy-MM-dd HH:mm:ss.ffff"),
                                                   timer._threadId,
                                                   timer._threadName.Replace(",", "").Replace("\"", ""),
                                                   timer._name.Replace(",", "").Replace("\"", ""),
                                                   timer._syncHash.ToString("x"),
                                                   timer._lockTaken ? 1 : 0,
                                                   enterTotal,
                                                   insideTotal,
                                                   grandTotal,
                                                   timer._preEnter,
                                                   timer._postEnter,
                                                   timer._preExit,
                                                   timer._postExit);

                            streamWriter.WriteLine(line);

                        }
                    }
                }
            }
        }

    }
}
