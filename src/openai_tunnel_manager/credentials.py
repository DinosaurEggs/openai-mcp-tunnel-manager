from __future__ import annotations

import ctypes
import os
from ctypes import wintypes
from typing import Protocol

SERVICE_PREFIX = "OpenAITunnelManager/"


class CredentialError(RuntimeError):
    pass


class CredentialStore(Protocol):
    def set(self, credential_id: str, secret: str) -> None: ...
    def get(self, credential_id: str) -> str | None: ...
    def delete(self, credential_id: str) -> None: ...


class WindowsCredentialStore:
    """Generic credentials stored by the native Windows 凭据管理器 API."""

    CRED_TYPE_GENERIC = 1
    CRED_PERSIST_LOCAL_MACHINE = 2
    ERROR_NOT_FOUND = 1168

    def __init__(self) -> None:
        if os.name != "nt":
            raise CredentialError("Windows 凭据管理器 仅可在 Windows 上使用")
        self._advapi32 = ctypes.WinDLL("Advapi32.dll", use_last_error=True)

        class CREDENTIALW(ctypes.Structure):
            _fields_ = [
                ("Flags", wintypes.DWORD),
                ("Type", wintypes.DWORD),
                ("TargetName", wintypes.LPWSTR),
                ("Comment", wintypes.LPWSTR),
                ("LastWritten", wintypes.FILETIME),
                ("CredentialBlobSize", wintypes.DWORD),
                ("CredentialBlob", ctypes.POINTER(ctypes.c_ubyte)),
                ("Persist", wintypes.DWORD),
                ("AttributeCount", wintypes.DWORD),
                ("Attributes", ctypes.c_void_p),
                ("TargetAlias", wintypes.LPWSTR),
                ("UserName", wintypes.LPWSTR),
            ]

        self._CREDENTIALW = CREDENTIALW
        self._PCREDENTIALW = ctypes.POINTER(CREDENTIALW)
        self._advapi32.CredWriteW.argtypes = [self._PCREDENTIALW, wintypes.DWORD]
        self._advapi32.CredWriteW.restype = wintypes.BOOL
        self._advapi32.CredReadW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.POINTER(self._PCREDENTIALW),
        ]
        self._advapi32.CredReadW.restype = wintypes.BOOL
        self._advapi32.CredDeleteW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD]
        self._advapi32.CredDeleteW.restype = wintypes.BOOL
        self._advapi32.CredFree.argtypes = [ctypes.c_void_p]
        self._advapi32.CredFree.restype = None

    @staticmethod
    def _target(credential_id: str) -> str:
        return SERVICE_PREFIX + credential_id.strip()

    def set(self, credential_id: str, secret: str) -> None:
        if not credential_id.strip():
            raise CredentialError("凭据 ID 不能为空")
        if not secret:
            raise CredentialError("API Key 不能为空")
        blob_bytes = secret.encode("utf-16-le")
        if len(blob_bytes) > 2560:
            raise CredentialError("API Key 超过 Windows 通用凭据大小限制")
        blob = (ctypes.c_ubyte * len(blob_bytes)).from_buffer_copy(blob_bytes)
        cred = self._CREDENTIALW()
        cred.Type = self.CRED_TYPE_GENERIC
        cred.TargetName = self._target(credential_id)
        cred.CredentialBlobSize = len(blob_bytes)
        cred.CredentialBlob = ctypes.cast(blob, ctypes.POINTER(ctypes.c_ubyte))
        cred.Persist = self.CRED_PERSIST_LOCAL_MACHINE
        cred.UserName = "OpenAI tunnel-client runtime key"
        if not self._advapi32.CredWriteW(ctypes.byref(cred), 0):
            raise CredentialError(f"写入 Windows 凭据管理器 失败: {ctypes.get_last_error()}")

    def get(self, credential_id: str) -> str | None:
        pcred = self._PCREDENTIALW()
        if not self._advapi32.CredReadW(
            self._target(credential_id), self.CRED_TYPE_GENERIC, 0, ctypes.byref(pcred)
        ):
            err = ctypes.get_last_error()
            if err == self.ERROR_NOT_FOUND:
                return None
            raise CredentialError(f"读取 Windows 凭据管理器 失败: {err}")
        try:
            cred = pcred.contents
            if cred.CredentialBlobSize == 0:
                return ""
            raw = ctypes.string_at(cred.CredentialBlob, cred.CredentialBlobSize)
            return raw.decode("utf-16-le")
        finally:
            self._advapi32.CredFree(pcred)

    def delete(self, credential_id: str) -> None:
        if not self._advapi32.CredDeleteW(self._target(credential_id), self.CRED_TYPE_GENERIC, 0):
            err = ctypes.get_last_error()
            if err != self.ERROR_NOT_FOUND:
                raise CredentialError(f"删除 Windows 凭据管理器 凭据失败: {err}")


class MemoryCredentialStore:
    """Test-only/injected store; never selected automatically by the production app."""
    def __init__(self) -> None:
        self.data: dict[str, str] = {}

    def set(self, credential_id: str, secret: str) -> None:
        self.data[credential_id] = secret

    def get(self, credential_id: str) -> str | None:
        return self.data.get(credential_id)

    def delete(self, credential_id: str) -> None:
        self.data.pop(credential_id, None)
