import unittest
from openai_tunnel_manager.credentials import MemoryCredentialStore

class CredentialTests(unittest.TestCase):
    def test_contract(self):
        s=MemoryCredentialStore(); self.assertIsNone(s.get("a")); s.set("a","secret"); self.assertEqual(s.get("a"),"secret"); s.delete("a"); self.assertIsNone(s.get("a"))

if __name__ == "__main__": unittest.main()
