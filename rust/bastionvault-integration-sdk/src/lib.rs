pub fn sdk_name() -> &'static str {
    "BastionVault Integration SDK"
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn exposes_expected_sdk_name() {
        assert_eq!(sdk_name(), "BastionVault Integration SDK");
    }
}
