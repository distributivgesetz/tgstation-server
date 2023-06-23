﻿using System;
using System.Runtime.InteropServices;
using System.Text;

using Mono.Unix.Native;

namespace Tgstation.Server.Host.System
{
	/// <summary>
	/// Native Windows methods used by the code.
	/// </summary>
#pragma warning disable SA1602
#pragma warning disable SA1611
#pragma warning disable SA1615
	static class NativeMethods
	{
		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winbase/nf-winbase-createsymboliclinka#parameters.
		/// </summary>
		[Flags]
		public enum CreateSymbolicLinkFlags : int
		{
			None = 0x0,
			Directory = 0x1,
			AllowUnprivilegedCreate = 0x2,
		}

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms686769(v=vs.85).aspx.
		/// </summary>
		public enum ThreadAccess : int
		{
			SuspendResume = 0x0002,
		}

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/win32/api/minidumpapiset/ne-minidumpapiset-minidump_type.
		/// </summary>
		[Flags]
		public enum MiniDumpType : uint
		{
			WithDataSegs = 0x00000001,
			WithFullMemory = 0x00000002,
			WithHandleData = 0x00000004,
			WithUnloadedModules = 0x00000020,
			WithThreadInfo = 0x00001000,
		}

		/// <summary>
		/// See https://linux.die.net/man/3/clock_gettime.
		/// </summary>
		public enum ClockId : int
		{
			CLOCK_MONOTONIC = 1, // from linux/time.h. God forbid this breaks uncannily
		}

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowthreadprocessid.
		/// </summary>
		[DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-findwindoww.
		/// </summary>
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-sendmessage.
		/// </summary>
		[DllImport("user32.dll")]
		public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/ms633493(v=VS.85).aspx.
		/// </summary>
		public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-enumchildwindows.
		/// </summary>
		[DllImport("user32.dll")]
		public static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr lParam);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowtextw.
		/// </summary>
		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx.
		/// </summary>
		[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winbase/nf-winbase-createsymboliclinkw.
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, CreateSymbolicLinkFlags dwFlags);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms684335(v=vs.85).aspx.
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms724211(v=vs.85).aspx.
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool CloseHandle(IntPtr hObject);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms686345(v=vs.85).aspx.
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern uint SuspendThread(IntPtr hThread);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms685086(v=vs.85).aspx.
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern uint ResumeThread(IntPtr hThread);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump.
		/// </summary>
		[DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool MiniDumpWriteDump(
			IntPtr hProcess,
			uint processId,
			SafeHandle hFile,
			MiniDumpType dumpType,
			IntPtr expParam,
			IntPtr userStreamParam,
			IntPtr callbackParam);

		/// <summary>
		/// See https://www.freedesktop.org/software/systemd/man/sd_notify.html.
		/// </summary>
#pragma warning disable IDE0079
#pragma warning disable CA2101 // https://github.com/dotnet/roslyn-analyzers/issues/5479#issuecomment-1603665900
		[DllImport("libsystemd.so.0", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
		public static extern int sd_notify(int unset_environment, [MarshalAs(UnmanagedType.LPUTF8Str)] string state);
#pragma warning restore CA2101
#pragma warning restore IDE0079

		/// <summary>
		/// See https://linux.die.net/man/3/clock_gettime.
		/// </summary>
		/// <remarks>We're relying on the portablility of <see cref="Timeval"/> for this to work. Untested on x86, but I don't think that's supported anymore?</remarks>
		[DllImport("libc", SetLastError = true)]
		public static extern int clock_gettime(ClockId clk_id, out Timeval tp);
	}
}
