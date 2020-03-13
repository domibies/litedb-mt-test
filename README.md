# litedb-mt-test

[LiteDB](https://github.com/mbdavid/LiteDB) multithreaded stress test application

Accompanies LiteDB [issue 1573](https://github.com/mbdavid/LiteDB/issues/1537)

Edit 13/03/2020 :
- Seems to be stable now with 5.0.4. Added a thread specifically for calling 'Checkpoint()'.
- Stuff below not true anymore, leaving this for reference


Try to play a little bit with the parameters. Increase **DeleteEveryXSeconds** e.g. to 10, and you get different behaviour on Windows (locking instead of null reference exception), and a longer running test on MacOS.

Behaviour seems to be either a null reference exception

    System.NullReferenceException: Object reference not set to an instance of an object.
    at LiteDB.Engine.TransactionMonitor.b__15_0(UInt32 id)
    at LiteDB.Engine.TransactionService.Commit()
    at LiteDB.Engine.LiteEngine.AutoTransaction[T](Func2 fn) at LiteDB.LiteCollection1.Insert(T entity)

or a database lock timeout exception

    LiteDB.LiteException
    HResult=0x80131500
    Message=Database lock timeout when entering in transaction mode after 00:01:00
    Source=LiteDB
    StackTrace:
    at LiteDB.Engine.LockService.EnterTransaction()
    at LiteDB.Engine.TransactionMonitor.GetTransaction(Boolean create, Boolean& isNew)
    at LiteDB.Engine.LiteEngine.AutoTransaction[T](Func2 fn) at LiteDB.LiteCollection1.Insert(T entity)
