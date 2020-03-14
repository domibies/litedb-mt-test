using LiteDB;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LiteDb.MT.Test
{
    class Program
    {
        const int InitalNumInsertThreads = 32;
        static readonly List<Thread> Workers = new List<Thread>();

        const string DbFileName = "./MyFile.Db";
        static readonly ConcurrentDictionary<int, DateTime> WorkerTimestamps = new ConcurrentDictionary<int, DateTime>(); // keep timestamp per thread (after each insert)
        static readonly TimeSpan WaitBeforeAlert = new TimeSpan(0, 0, 5); // how long before a thread is considered 'stale'
        const int ReportEveryXSeconds = 1;
        const int DeleteEveryXSeconds = 1;
        const int DoCheckpointEveryXSeconds = 1;
        const int KeepRecordsXSeconds = 10;
        static LiteDatabase Db;
        const string CollectionName = "Data";

        static readonly DateTime StartTime = DateTime.Now;

        static int CountInserted = 0;
        static int CountDeleted = 0;
        static readonly object lockStats = new object();
        static void AddDeleted(int numDeleted)
        {
            lock (lockStats)
            {
                CountDeleted += numDeleted;
            }
        }
        static void AddInserted(int numInserted)
        {
            lock (lockStats)
            {
                CountInserted += numInserted;
            }
        }
        static int WaitSecondsToMs(int seconds)
        {
            return (seconds * 1000 + new Random().Next(-20, 20));
        }

        public class SomeObject
        {
            public DateTime TimeStamp { get; set; }
        }

        static void Main(/*string[] args*/)
        {
            using var cancelSource = new CancellationTokenSource();
            try
            {
                DeleteDbFiles();

                Db = new LiteDatabase(DbFileName);

                // create & start the 'inserter'  threads
                for (int i = 0; i < InitalNumInsertThreads; ++i)
                {
                    AddInsertThread(cancelSource);
                }

                var reportThread = new Thread(() => ReportThread(cancelSource.Token));
                reportThread.Start();
                Workers.Add(reportThread);

                var deleteThread = new Thread(() => DeleteOldRecords(cancelSource.Token));
                deleteThread.Start();
                Workers.Add(deleteThread);

                var doCheckpointThread = new Thread(() => DoCheckpoint(cancelSource.Token));
                doCheckpointThread.Start();
                Workers.Add(doCheckpointThread);

                while (true)
                {
                    var key = Console.ReadKey(true).Key;

                    if (key == ConsoleKey.Spacebar)
                        AddInsertThread(cancelSource);

                    if (key == ConsoleKey.Enter)
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
            finally
            {
                // stop it all
                cancelSource.Cancel();
                foreach (var worker in Workers)
                {
                    worker.Join();
                }
                DeleteDbFiles();
            }
        }

        private static void DeleteDbFiles()
        {
            var searchPattern = Path.GetFileNameWithoutExtension(DbFileName);
            var filesToDelete = Directory.GetFiles(".", $"{searchPattern}*.Db");
            foreach (var deleteFile in filesToDelete)
            {
                Console.WriteLine($"Deleting {new FileInfo(deleteFile).FullName}");
                File.Delete(deleteFile);
            }
        }

        private static void AddInsertThread(CancellationTokenSource cancelSource)
        {
            var thread = new Thread(() => InsertRecords(cancelSource.Token));
            Workers.Add(thread);
            thread.Start();
        }

        private static void InsertRecords(CancellationToken cancelToken)
        {
            while (true)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} canceled");
                    return;
                }

                // do it (insert)
                var collection = Db.GetCollection<SomeObject>(CollectionName);
                collection.Insert(new SomeObject
                {
                    TimeStamp = DateTime.Now
                });


                AddInserted(1);

                WorkerTimestamps[Thread.CurrentThread.ManagedThreadId] = DateTime.Now;

                Thread.Sleep(new Random().Next(1, 16)); // cpu friendly
            }
        }

        private static void ReportThread(CancellationToken cancelToken)
        {
            while (true)
            {
                // only once every x time
                var cancelled = cancelToken.WaitHandle.WaitOne(WaitSecondsToMs(ReportEveryXSeconds));
                if (cancelled)
                {
                    Console.WriteLine($"Report thread ({Thread.CurrentThread.ManagedThreadId}) canceled");
                    break;
                }

                Console.Clear();
                Console.WriteLine($"LiteDb Multithreaded insert & delete test ({Workers.Count - 3} insert threads), running for {(DateTime.Now - StartTime)}");
                Console.WriteLine($"{CountInserted} Records inserted so far.");
                Console.WriteLine($"{CountDeleted} Records deleted so far.");
                Console.WriteLine("Press <ENTER> to stop processing, <SPACE> to add a thread");

                int countInActive = 0;
                TimeSpan minimuAge = TimeSpan.MaxValue;
                foreach (var stamp in WorkerTimestamps)
                {
                    var howLong = (DateTime.Now - stamp.Value);

                    if (howLong > WaitBeforeAlert)
                    {
                        if (howLong < minimuAge)
                            minimuAge = howLong;
                        countInActive++;
                    }
                }
                if (countInActive > 0)
                {
                    Console.WriteLine($"!!! {countInActive} Threads have been inactive for at least {minimuAge}!");
                }
            }
        }


        private static void DeleteOldRecords(CancellationToken cancelToken)
        {
            while (true)
            {
                // only once every X second
                var cancelled = cancelToken.WaitHandle.WaitOne(WaitSecondsToMs(DeleteEveryXSeconds));
                if (cancelled)
                {
                    Console.WriteLine($"Delete thread ({Thread.CurrentThread.ManagedThreadId}) canceled");
                    break;
                }

                // do it (delete)
                var collection = Db.GetCollection<SomeObject>(CollectionName);
                var ageLimit = DateTime.Now + new TimeSpan(0, 0, -KeepRecordsXSeconds);
                int countDeleted = collection.DeleteMany(x => x.TimeStamp < ageLimit);

                if (countDeleted > 0)
                    AddDeleted(countDeleted);
            }
        }

        private static void DoCheckpoint(CancellationToken cancelToken)
        {
            while (true)
            {
                // only once every X second
                var cancelled = cancelToken.WaitHandle.WaitOne(WaitSecondsToMs(DoCheckpointEveryXSeconds));
                if (cancelled)
                {
                    Console.WriteLine($"DoCheckpoint thread ({Thread.CurrentThread.ManagedThreadId}) canceled");
                    break;
                }

                Db.Checkpoint();
            }
        }
    }
}
