using System.Runtime.InteropServices;

namespace RadioPassthrough.Core.Native;

public sealed class SqliteException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;
}

// Minimal SQLite access through winsqlite3.dll, which ships with Windows 10 and 11. Using the OS copy
// means the app carries no native library of its own.
public sealed partial class SqliteDatabase : IDisposable
{
    private const string Lib = "winsqlite3";
    private const int Ok = 0, Row = 100, Done = 101, Null = 5;
    private const int OpenReadOnly = 0x1, OpenReadWrite = 0x2;
    private static readonly IntPtr Transient = new(-1);

    private IntPtr _db;

    private SqliteDatabase(IntPtr db) => _db = db;

    public static SqliteDatabase Open(string path, bool readOnly = false)
    {
        int rc = sqlite3_open_v2(path, out IntPtr db, readOnly ? OpenReadOnly : OpenReadWrite, IntPtr.Zero);
        if (rc != Ok)
        {
            string message = db != IntPtr.Zero ? ErrorMessage(db) : $"code {rc}";
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new SqliteException($"Couldn't open {Path.GetFileName(path)}: {message}", rc);
        }
        sqlite3_busy_timeout(db, 3000);
        return new SqliteDatabase(db);
    }

    public static SqliteDatabase Create(string path)
    {
        const int create = 0x4;
        int rc = sqlite3_open_v2(path, out IntPtr db, OpenReadWrite | create, IntPtr.Zero);
        if (rc != Ok) throw new SqliteException($"Couldn't create {path}", rc);
        return new SqliteDatabase(db);
    }

    public void Execute(string sql, params object?[] parameters)
    {
        IntPtr stmt = Prepare(sql, parameters);
        try
        {
            int rc = sqlite3_step(stmt);
            if (rc != Done && rc != Row) Throw(rc);
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    public List<string?[]> Query(string sql, params object?[] parameters)
    {
        IntPtr stmt = Prepare(sql, parameters);
        var rows = new List<string?[]>();
        try
        {
            int columns = sqlite3_column_count(stmt);
            while (true)
            {
                int rc = sqlite3_step(stmt);
                if (rc == Done) break;
                if (rc != Row) Throw(rc);
                var row = new string?[columns];
                for (int c = 0; c < columns; c++)
                    row[c] = sqlite3_column_type(stmt, c) == Null ? null : Marshal.PtrToStringUni(sqlite3_column_text16(stmt, c));
                rows.Add(row);
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
        return rows;
    }

    public void InTransaction(Action body)
    {
        Execute("BEGIN IMMEDIATE");
        try
        {
            body();
            Execute("COMMIT");
        }
        catch
        {
            try { Execute("ROLLBACK"); } catch (SqliteException) { }
            throw;
        }
    }

    private IntPtr Prepare(string sql, object?[] parameters)
    {
        ObjectDisposedException.ThrowIf(_db == IntPtr.Zero, this);
        int rc = sqlite3_prepare16_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero);
        if (rc != Ok) Throw(rc);
        for (int i = 0; i < parameters.Length; i++)
        {
            rc = parameters[i] switch
            {
                null => sqlite3_bind_null(stmt, i + 1),
                long l => sqlite3_bind_int64(stmt, i + 1, l),
                int n => sqlite3_bind_int64(stmt, i + 1, n),
                string s => sqlite3_bind_text16(stmt, i + 1, s, -1, Transient),
                var other => sqlite3_bind_text16(stmt, i + 1, other.ToString() ?? "", -1, Transient),
            };
            if (rc != Ok)
            {
                sqlite3_finalize(stmt);
                Throw(rc);
            }
        }
        return stmt;
    }

    private void Throw(int rc) => throw new SqliteException(ErrorMessage(_db), rc);

    private static string ErrorMessage(IntPtr db) => Marshal.PtrToStringUni(sqlite3_errmsg16(db)) ?? "unknown SQLite error";

    public void Dispose()
    {
        if (_db == IntPtr.Zero) return;
        sqlite3_close_v2(_db);
        _db = IntPtr.Zero;
    }

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sqlite3_open_v2(string filename, out IntPtr db, int flags, IntPtr vfs);

    [LibraryImport(Lib)]
    private static partial int sqlite3_close_v2(IntPtr db);

    [LibraryImport(Lib)]
    private static partial int sqlite3_busy_timeout(IntPtr db, int ms);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int sqlite3_prepare16_v2(IntPtr db, string sql, int nByte, out IntPtr stmt, IntPtr tail);

    [LibraryImport(Lib)]
    private static partial int sqlite3_step(IntPtr stmt);

    [LibraryImport(Lib)]
    private static partial int sqlite3_finalize(IntPtr stmt);

    [LibraryImport(Lib)]
    private static partial int sqlite3_column_count(IntPtr stmt);

    [LibraryImport(Lib)]
    private static partial int sqlite3_column_type(IntPtr stmt, int column);

    [LibraryImport(Lib)]
    private static partial IntPtr sqlite3_column_text16(IntPtr stmt, int column);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int sqlite3_bind_text16(IntPtr stmt, int index, string value, int nBytes, IntPtr destructor);

    [LibraryImport(Lib)]
    private static partial int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [LibraryImport(Lib)]
    private static partial int sqlite3_bind_null(IntPtr stmt, int index);

    [LibraryImport(Lib)]
    private static partial IntPtr sqlite3_errmsg16(IntPtr db);
}
