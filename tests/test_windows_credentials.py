import os, unittest, uuid
from openai_tunnel_manager.credentials import WindowsCredentialStore

@unittest.skipUnless(os.name == "nt", "Windows-only Credential Manager integration test")
class WindowsCredentialIntegrationTests(unittest.TestCase):
    def test_native_roundtrip(self):
        store = WindowsCredentialStore()
        key = "test-" + uuid.uuid4().hex
        secret = "sk-test-" + uuid.uuid4().hex
        try:
            store.set(key, secret)
            self.assertEqual(store.get(key), secret)
        finally:
            store.delete(key)
        self.assertIsNone(store.get(key))

if __name__ == "__main__": unittest.main()
