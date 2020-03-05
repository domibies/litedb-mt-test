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
        const int InitalNumInsertThreads = 16;
        static readonly List<Thread> Workers = new List<Thread>();

        const string DbFileName = ".\\MyFile.Db";
        static readonly ConcurrentDictionary<int, DateTime> WorkerTimestamps = new ConcurrentDictionary<int, DateTime>(); // keep timestamp per thread (after each insert)
        static readonly TimeSpan WaitBeforeAlert = new TimeSpan(0, 0, 5); // how long before a thread is considered 'stale'
        const int ReportEveryXSeconds = 1;
        const int DeleteEveryXSeconds = 3;
        const int KeepRecordsXSeconds = 5;
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


        public class SomeObject
        {
            public DateTime TimeStamp { get; set; }
        }

        static void Main(/*string[] args*/)
        {
            try
            {
                var searchPattern = Path.GetFileNameWithoutExtension(DbFileName);
                var filesToDelete = Directory.GetFiles(".", $"{searchPattern}*.Db");
                foreach (var deleteFile in filesToDelete)
                {
                    Console.WriteLine($"Deleting {deleteFile}");
                    File.Delete(deleteFile);
                }

                Db = new LiteDatabase(DbFileName);

                using var cancelSource = new CancellationTokenSource();
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

                while (true)
                {
                    var key = Console.ReadKey(true).Key;

                    if (key == ConsoleKey.Spacebar)
                        AddInsertThread(cancelSource);

                    if (key == ConsoleKey.Enter)
                        break;
                }

                // stop it all
                cancelSource.Cancel();
                foreach (var worker in Workers)
                {
                    worker.Join();
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
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

                Thread.Sleep(new Random().Next(1, 5)); // cpu friendly
            }
        }

        private static void ReportThread(CancellationToken cancelToken)
        {
            while (true)
            {
                // only once every x time
                var cancelled = cancelToken.WaitHandle.WaitOne(1000 * ReportEveryXSeconds);
                if (cancelled)
                {
                    Console.WriteLine($"Report thread ({Thread.CurrentThread.ManagedThreadId}) canceled");
                    break;
                }

                Console.Clear();
                Console.WriteLine($"LiteDb Multithreaded insert & delete hammering ({Workers.Count - 2} insert threads), running for {(DateTime.Now - StartTime)}");
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
                var cancelled = cancelToken.WaitHandle.WaitOne(1000 * DeleteEveryXSeconds);
                if (cancelled)
                {
                    Console.WriteLine($"Delete thread ({Thread.CurrentThread.ManagedThreadId}) canceled");
                    break;
                }

                //Db.Checkpoint();

                // do it (delete)
                var collection = Db.GetCollection<SomeObject>(CollectionName);
                var ageLimit = DateTime.Now + new TimeSpan(0, 0, -KeepRecordsXSeconds);
                int countDeleted = collection.DeleteMany(x => x.TimeStamp < ageLimit);

                if (countDeleted > 0)
                    AddDeleted(countDeleted);
            }
        }
    }
}
