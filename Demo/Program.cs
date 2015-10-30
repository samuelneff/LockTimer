using System;
using System.Threading;
using LockTimerProject;

namespace Demo
{
    class Program
    {
        private static readonly object[] _syncs = {new object(), new object(), new object(), new object(), new object()};

        static void Main()
        {

            LockTimer.EnableLogging = true;
            LockTimer.CacheMilliseconds = 500;
            LockTimer.LogPath = Environment.CurrentDirectory;

            Console.WriteLine("LockTimer Demo");
            Console.WriteLine();
            Console.WriteLine("We'll spin up 20 threads all locking on 5 objects and log the timing.");
            Console.WriteLine();
            Console.WriteLine("Hit ENTER to finish.");

            for (var i = 0; i < 20; i++)
            {
                ThreadPool.QueueUserWorkItem(LockLoop, i);
            }

            LockTimer.Contention += (sender, args) => 
                Console.WriteLine("{0,8} {1,8} : {2,5} to enter, {3,5} total",
                                                                        args.ThreadName,
                                                                        args.LockName,
                                                                        args.EnterTime,
                                                                        args.TotalTime);

            Console.ReadLine();

            Console.WriteLine();
            Console.WriteLine("Logs written to current directory: " + LockTimer.LogPath);
            Console.WriteLine();
        }

        private static void LockLoop(object state)
        {
            var r = new Random();
            Thread.CurrentThread.Name = String.Format("Thread {0,2}",  state);

            for (;;)
            {
                // first we sleep for a random second
                Thread.Sleep(r.Next(1000));

                // now lets lock something
                // making the last sync less likely to lock on..
                int index = r.NextDouble() < 0.05 ? _syncs.Length - 1 : r.Next(_syncs.Length - 1);
                object sync = _syncs[index];

                using (new LockTimer(String.Format("Index {0,2}", index), sync))
                {
                    // now lets sleep a bit to simulate work.. for lowest indexes lets sleep a lot,
                    // and higher ones just a little
                    Thread.Sleep(r.Next(2000) + (index < 2 ? 1500 : 0));
                }
            }
        }
    }
}
