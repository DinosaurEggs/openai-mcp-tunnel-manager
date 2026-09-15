from __future__ import annotations

import ctypes
import os
import threading
from typing import Any, Callable

ERROR_ALREADY_EXISTS = 183
WAIT_OBJECT_0 = 0x00000000
WAIT_TIMEOUT = 0x00000102
WAIT_FAILED = 0xFFFFFFFF

DEFAULT_MUTEX_NAME = r"Local\OpenAITunnelManager.SingleInstance"
DEFAULT_EVENT_NAME = r"Local\OpenAITunnelManager.Activate"


def _load_kernel32() -> Any:
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateMutexW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.LPCWSTR]
    kernel32.CreateMutexW.restype = wintypes.HANDLE
    kernel32.CreateEventW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
    kernel32.CreateEventW.restype = wintypes.HANDLE
    kernel32.SetEvent.argtypes = [wintypes.HANDLE]
    kernel32.SetEvent.restype = wintypes.BOOL
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.WaitForSingleObject.restype = wintypes.DWORD
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    return kernel32


class SingleInstanceGuard:
    """Windows single-instance guard with a lightweight activation signal.

    The named mutex guarantees one application instance per Windows logon
    session. A named auto-reset event lets later launches ask the existing
    instance to restore and focus its window without opening a socket or
    requiring pywin32.
    """

    def __init__(
        self,
        mutex_name: str = DEFAULT_MUTEX_NAME,
        event_name: str = DEFAULT_EVENT_NAME,
    ) -> None:
        self.mutex_name = mutex_name
        self.event_name = event_name
        self._enabled = os.name == "nt"
        self._kernel32: Any | None = _load_kernel32() if self._enabled else None
        self._mutex_handle: Any | None = None
        self._event_handle: Any | None = None
        self._primary = False
        self._stop = threading.Event()
        self._listener: threading.Thread | None = None

    @property
    def is_primary(self) -> bool:
        return self._primary

    def acquire(self) -> bool:
        if not self._enabled:
            self._primary = True
            return True
        if self._primary:
            return True

        assert self._kernel32 is not None
        ctypes.set_last_error(0)
        mutex = self._kernel32.CreateMutexW(None, False, self.mutex_name)
        if not mutex:
            raise OSError(ctypes.get_last_error(), "无法创建单实例 Mutex")
        already_exists = ctypes.get_last_error() == ERROR_ALREADY_EXISTS
        if already_exists:
            self._kernel32.CloseHandle(mutex)
            return False

        event = self._kernel32.CreateEventW(None, False, False, self.event_name)
        if not event:
            error = ctypes.get_last_error()
            self._kernel32.CloseHandle(mutex)
            raise OSError(error, "无法创建单实例激活事件")

        self._mutex_handle = mutex
        self._event_handle = event
        self._primary = True
        return True

    def activate_existing(self) -> bool:
        """Signal the already-running instance to restore its main window."""
        if not self._enabled:
            return False
        assert self._kernel32 is not None

        # CreateEventW opens the existing named event when it already exists.
        # If the primary process has created its mutex but has not yet created
        # the event, this creates the event first; the primary then opens the
        # same signaled object, so startup races are not lost.
        event = self._kernel32.CreateEventW(None, False, False, self.event_name)
        if not event:
            return False
        try:
            return bool(self._kernel32.SetEvent(event))
        finally:
            self._kernel32.CloseHandle(event)

    def start_activation_listener(self, callback: Callable[[], None]) -> None:
        if not self._enabled or not self._primary or self._event_handle is None:
            return
        if self._listener is not None and self._listener.is_alive():
            return

        self._stop.clear()

        def listen() -> None:
            assert self._kernel32 is not None
            while not self._stop.is_set():
                result = int(self._kernel32.WaitForSingleObject(self._event_handle, 250))
                if result == WAIT_OBJECT_0:
                    if self._stop.is_set():
                        break
                    try:
                        callback()
                    except Exception:
                        # Activation must never terminate the background waiter.
                        pass
                elif result == WAIT_TIMEOUT:
                    continue
                elif result == WAIT_FAILED:
                    break
                else:
                    break

        self._listener = threading.Thread(
            target=listen,
            name="openai-tunnel-manager-single-instance",
            daemon=True,
        )
        self._listener.start()

    def close(self) -> None:
        self._stop.set()
        if self._enabled and self._kernel32 is not None and self._event_handle is not None:
            # Wake the waiter so shutdown does not wait for the polling timeout.
            self._kernel32.SetEvent(self._event_handle)
        if self._listener is not None and self._listener.is_alive():
            self._listener.join(timeout=1.0)
        self._listener = None

        if self._enabled and self._kernel32 is not None:
            if self._event_handle is not None:
                self._kernel32.CloseHandle(self._event_handle)
            if self._mutex_handle is not None:
                self._kernel32.CloseHandle(self._mutex_handle)
        self._event_handle = None
        self._mutex_handle = None
        self._primary = False
