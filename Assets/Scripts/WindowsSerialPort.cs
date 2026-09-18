using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

sealed class WindowsSerialPort : IDisposable
{
    const uint GenericRead = 0x80000000;
    const uint GenericWrite = 0x40000000;
    const uint OpenExisting = 3;
    const uint FileAttributeNormal = 0x80;
    const uint PurgeAll = 0x000F;
    const uint SetDtr = 5;
    const uint SetRts = 3;
    const int ErrorOperationAborted = 995;

    static readonly IntPtr InvalidHandle = new IntPtr(-1);

    IntPtr handle = InvalidHandle;
    readonly byte[] readChunk = new byte[256];
    readonly StringBuilder lineBuffer = new StringBuilder();

    public bool IsOpen => handle != IntPtr.Zero && handle != InvalidHandle;

    public static string[] GetPortNames()
    {
        List<string> ports = new List<string>();
        char[] buffer = new char[256];
        for (int i = 1; i <= 32; i++)
        {
            string name = "COM" + i;
            if (QueryDosDevice(name, buffer, (uint)buffer.Length) != 0)
            {
                ports.Add(name);
            }
        }

        return ports.ToArray();
    }

    public void Open(string portName, int baudRate)
    {
        if (IsOpen)
        {
            throw new InvalidOperationException("Serial port already open.");
        }

        string path = portName.StartsWith(@"\\.\", StringComparison.Ordinal) ? portName : @"\\.\" + portName;
        handle = CreateFile(path, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (!IsOpen)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 2)
            {
                throw new FileNotFoundException("Serial port not present: " + portName);
            }

            if (error == 5)
            {
                throw new UnauthorizedAccessException("Serial port in use: " + portName);
            }

            throw new IOException("Could not open " + portName + " (Win32 " + error + ")");
        }

        SetupComm(handle, 4096, 4096);

        DCB dcb = new DCB();
        dcb.DCBlength = Marshal.SizeOf<DCB>();
        if (!GetCommState(handle, ref dcb))
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new IOException("GetCommState failed (Win32 " + error + ")");
        }

        dcb.BaudRate = (uint)baudRate;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        if (!SetCommState(handle, ref dcb))
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new IOException("SetCommState failed (Win32 " + error + ")");
        }

        CommTimeouts timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = 200,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 200
        };
        SetCommTimeouts(handle, ref timeouts);
        EscapeCommFunction(handle, SetDtr);
        EscapeCommFunction(handle, SetRts);
        PurgeComm(handle, PurgeAll);
    }

    public string ReadLine()
    {
        while (true)
        {
            string pending = TryExtractLine();
            if (pending != null)
            {
                return pending;
            }

            if (!IsOpen)
            {
                throw new IOException("Serial port closed.");
            }

            uint bytesRead;
            if (!ReadFile(handle, readChunk, (uint)readChunk.Length, out bytesRead, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorOperationAborted)
                {
                    throw new IOException("Serial read aborted.");
                }

                throw new IOException("Serial read failed (Win32 " + error + ")");
            }

            if (bytesRead == 0)
            {
                throw new TimeoutException();
            }

            for (uint i = 0; i < bytesRead; i++)
            {
                char c = (char)readChunk[i];
                if (c == '\0')
                {
                    continue;
                }

                lineBuffer.Append(c);
                if (lineBuffer.Length > 256)
                {
                    lineBuffer.Length = 0;
                }
            }
        }
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (!IsOpen)
        {
            return;
        }

        IntPtr current = handle;
        handle = InvalidHandle;
        CancelIoEx(current, IntPtr.Zero);
        CloseHandle(current);
        lineBuffer.Length = 0;
    }

    string TryExtractLine()
    {
        for (int i = 0; i < lineBuffer.Length; i++)
        {
            if (lineBuffer[i] != '\n')
            {
                continue;
            }

            string line = lineBuffer.ToString(0, i).TrimEnd('\r');
            lineBuffer.Remove(0, i + 1);
            return line;
        }

        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetCommState(IntPtr hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetCommTimeouts(IntPtr hFile, ref CommTimeouts lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetupComm(IntPtr hFile, uint dwInQueue, uint dwOutQueue);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool EscapeCommFunction(IntPtr hFile, uint dwFunc);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PurgeComm(IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint QueryDosDevice(string lpDeviceName, [Out] char[] lpTargetPath, uint ucchMax);

    [StructLayout(LayoutKind.Sequential)]
    struct DCB
    {
        public int DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }
}
