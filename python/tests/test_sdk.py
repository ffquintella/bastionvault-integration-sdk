import unittest

from bastionvault_integration_sdk import sdk_name


class SdkTests(unittest.TestCase):
    def test_exposes_expected_sdk_name(self) -> None:
        self.assertEqual("BastionVault Integration SDK", sdk_name())


if __name__ == "__main__":
    unittest.main()
