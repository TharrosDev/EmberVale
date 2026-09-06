"""Process ownership. Windows jobs kill descendants even after the immediate parent exits."""
from __future__ import annotations

import os
import signal
import subprocess


class ProcessTree:
    def __init__(self):
        self.handle = None
        if os.name == "nt":
            import ctypes as c
            from ctypes import wintypes as w
            self.kernel = c.WinDLL("kernel32", use_last_error=True)
            self.kernel.CreateJobObjectW.argtypes = [c.c_void_p, w.LPCWSTR]
            self.kernel.CreateJobObjectW.restype = w.HANDLE
            self.kernel.SetInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD]
            self.kernel.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
            self.kernel.CloseHandle.argtypes = [w.HANDLE]
            class Basic(c.Structure):
                _fields_ = [("process_time", c.c_int64), ("job_time", c.c_int64), ("flags", w.DWORD),
                            ("minimum", c.c_size_t), ("maximum", c.c_size_t), ("active", w.DWORD),
                            ("affinity", c.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
            class Io(c.Structure):
                _fields_ = [(name, c.c_uint64) for name in ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]
            class Extended(c.Structure):
                _fields_ = [("basic", Basic), ("io", Io), ("process_memory", c.c_size_t),
                            ("job_memory", c.c_size_t), ("peak_process", c.c_size_t), ("peak_job", c.c_size_t)]
            self.handle = self.kernel.CreateJobObjectW(None, None)
            if not self.handle:
                raise OSError(c.get_last_error(), "CreateJobObject failed")
            limits = Extended()
            limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if not self.kernel.SetInformationJobObject(self.handle, 9, c.byref(limits), c.sizeof(limits)):
                self.close()
                raise OSError(c.get_last_error(), "SetInformationJobObject failed")

    def assign(self, process):
        if self.handle and not self.kernel.AssignProcessToJobObject(self.handle, int(process._handle)):
            import ctypes
            raise OSError(ctypes.get_last_error(), "Cannot own child process tree")

    def kill(self, process):
        if self.handle:
            self.close()
        elif os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, timeout=10)
        else:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None
