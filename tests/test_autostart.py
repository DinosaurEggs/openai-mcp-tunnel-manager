import unittest
from pathlib import Path
from openai_tunnel_manager.autostart import startup_command

class AutoStartTests(unittest.TestCase):
    def test_source_startup_command_points_to_launcher(self):
        command = startup_command()
        if "launcher.py" in command:
            self.assertTrue(Path(command.split('"')[3]).is_file())

if __name__ == "__main__": unittest.main()
