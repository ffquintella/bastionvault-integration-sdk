use std::collections::VecDeque;

use serde_json::Value;

use super::fixture::{Fixture, ResponseSpec};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TransportFailure {
    ConnectionRefused,
    Timeout,
    TlsVerify,
    TlsHandshake,
    Reset,
    Dns,
    ScriptExhausted,
}

impl TransportFailure {
    pub fn parse(mode: &str) -> Result<Self, String> {
        match mode {
            "connection_refused" => Ok(Self::ConnectionRefused),
            "timeout" => Ok(Self::Timeout),
            "tls_verify" => Ok(Self::TlsVerify),
            "tls_handshake" => Ok(Self::TlsHandshake),
            "reset" => Ok(Self::Reset),
            "dns" => Ok(Self::Dns),
            other => Err(format!("unknown fake transport failure mode {other}")),
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct TransportResponse {
    pub status: u16,
    pub headers: Vec<(String, String)>,
    pub body: Option<Value>,
    pub raw_body: Option<String>,
}

impl TransportResponse {
    fn from_spec(spec: &ResponseSpec) -> Self {
        Self {
            status: spec.status,
            headers: spec
                .headers
                .iter()
                .map(|(name, value)| (name.clone(), value.as_str().unwrap_or_default().to_owned()))
                .collect(),
            body: spec.body.clone(),
            raw_body: spec.raw_body.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub enum ScriptedOutcome {
    Respond(TransportResponse),
    Fail(TransportFailure),
}

impl ScriptedOutcome {
    pub fn status(&self) -> Option<u16> {
        match self {
            Self::Respond(response) => Some(response.status),
            Self::Fail(_) => None,
        }
    }
}

#[derive(Debug, Clone)]
pub struct FakeTransport {
    exchanges: VecDeque<ScriptedOutcome>,
}

impl FakeTransport {
    pub fn from_fixture(fixture: &Fixture) -> Result<Self, String> {
        let exchanges = fixture
            .exchanges
            .iter()
            .map(|exchange| match (&exchange.respond, &exchange.fail) {
                (Some(response), None) => Ok(ScriptedOutcome::Respond(
                    TransportResponse::from_spec(response),
                )),
                (None, Some(failure)) => {
                    Ok(ScriptedOutcome::Fail(TransportFailure::parse(failure)?))
                }
                (Some(_), Some(_)) => {
                    Err(format!("fixture {} has both respond and fail", fixture.id))
                }
                (None, None) => Err(format!(
                    "fixture {} has neither respond nor fail",
                    fixture.id
                )),
            })
            .collect::<Result<VecDeque<_>, _>>()?;
        Ok(Self { exchanges })
    }

    pub fn next(&mut self) -> Result<ScriptedOutcome, TransportFailure> {
        self.exchanges
            .pop_front()
            .ok_or(TransportFailure::ScriptExhausted)
    }
}
