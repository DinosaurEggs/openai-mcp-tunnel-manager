from __future__ import annotations

import os
import threading
import unittest
import uuid

from openai_tunnel_manager.single_instance import SingleInstanceGuard


class SingleInstanceTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt", "Windows named-object behavior")
    def test_second_instance_signals_primary(self) -> None:
        token = uuid.uuid4().hex
        mutex_name = rf"Local\OpenAITunnelManager.Test.{token}"
        event_name = rf"Local\OpenAITunnelManager.Test.Activate.{token}"
        primary = SingleInstanceGuard(mutex_name, event_name)
        secondary = SingleInstanceGuard(mutex_name, event_name)
        activated = threading.Event()

        try:
            self.assertTrue(primary.acquire())
            primary.start_activation_listener(activated.set)

            self.assertFalse(secondary.acquire())
            self.assertTrue(secondary.activate_existing())
            self.assertTrue(activated.wait(2.0), "primary instance did not receive activation signal")
        finally:
            secondary.close()
            primary.close()

    def test_non_windows_guard_is_noop_primary(self) -> None:
        if os.name == "nt":
            self.skipTest("non-Windows behavior")
        guard = SingleInstanceGuard()
        try:
            self.assertTrue(guard.acquire())
            self.assertTrue(guard.is_primary)
            self.assertFalse(guard.activate_existing())
        finally:
            guard.close()


if __name__ == "__main__":
    unittest.main()
