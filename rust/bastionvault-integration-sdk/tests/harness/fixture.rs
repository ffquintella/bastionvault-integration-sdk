use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;
use serde_json::Value;

const SCHEMA_RELATIVE_PATH: &str = "specifications/fixtures/schema/fixture.schema.json";

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Fixture {
    pub id: String,
    pub title: String,
    #[serde(default)]
    pub captured_from: Option<String>,
    pub requirements: Vec<String>,
    pub level: String,
    pub sections: Vec<String>,
    #[serde(default)]
    pub client: Option<ClientConfig>,
    #[serde(default)]
    pub environment: Option<BTreeMap<String, String>>,
    pub operation: Operation,
    pub exchanges: Vec<Exchange>,
    pub expect: Expectation,
    #[serde(default)]
    pub strict_headers: bool,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ClientConfig {
    #[serde(default)]
    pub address: Option<String>,
    #[serde(default)]
    pub token: Option<String>,
    #[serde(default)]
    pub namespace: Option<String>,
    #[serde(default)]
    pub api_prefix: Option<String>,
    #[serde(default)]
    pub cluster_discovery: Option<bool>,
    #[serde(default)]
    pub settings: Option<Value>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Operation {
    pub name: String,
    #[serde(default)]
    pub args: Option<Value>,
    #[serde(default)]
    pub options: Option<Value>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Exchange {
    #[serde(default)]
    pub expect_request: Option<Value>,
    #[serde(default)]
    pub respond: Option<ResponseSpec>,
    #[serde(default)]
    pub fail: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ResponseSpec {
    pub status: u16,
    #[serde(default)]
    pub headers: BTreeMap<String, Value>,
    #[serde(default)]
    pub body: Option<Value>,
    #[serde(default)]
    pub raw_body: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Expectation {
    #[serde(default)]
    pub result: Option<Value>,
    #[serde(default)]
    pub error: Option<Value>,
    #[serde(default)]
    pub client_state: Option<Value>,
}

impl Fixture {
    pub fn synthetic_with_exchanges(exchanges: Vec<Value>) -> Self {
        let exchanges = exchanges
            .into_iter()
            .map(|value| {
                serde_json::from_value(value).expect("synthetic exchange must be shaped correctly")
            })
            .collect();
        Self {
            id: "synthetic.test".to_owned(),
            title: "synthetic fixture".to_owned(),
            captured_from: None,
            requirements: vec!["TST-011".to_owned()],
            level: "core".to_owned(),
            sections: vec!["test".to_owned()],
            client: None,
            environment: None,
            operation: Operation {
                name: "Synthetic.Operation".to_owned(),
                args: None,
                options: None,
            },
            exchanges,
            expect: Expectation {
                result: None,
                error: None,
                client_state: None,
            },
            strict_headers: false,
        }
    }
}

#[derive(Debug, Clone)]
pub struct FixtureLoader {
    repository_root: PathBuf,
    schema: Value,
}

impl FixtureLoader {
    pub fn new() -> Result<Self, String> {
        let start = std::env::current_exe().map_err(|error| {
            format!("unable to locate the test assembly for fixture discovery: {error}")
        })?;
        let repository_root = locate_repository_root(&start).ok_or_else(|| {
            format!(
                "repository root not found while walking from test assembly {}; expected {}",
                start.display(),
                SCHEMA_RELATIVE_PATH
            )
        })?;
        let schema_path = repository_root.join(SCHEMA_RELATIVE_PATH);
        let schema_text = fs::read_to_string(&schema_path).map_err(|error| {
            format!(
                "unable to read fixture schema {}: {error}",
                schema_path.display()
            )
        })?;
        let schema = serde_json::from_str(&schema_text).map_err(|error| {
            format!(
                "fixture schema is not valid JSON at {}: {error}",
                schema_path.display()
            )
        })?;
        Ok(Self {
            repository_root,
            schema,
        })
    }

    pub fn repository_root(&self) -> &Path {
        &self.repository_root
    }

    pub fn enumerate(&self) -> Result<Vec<Fixture>, String> {
        self.load_all()
    }

    fn enumerate_paths(&self) -> Result<Vec<PathBuf>, String> {
        let fixture_root = self.repository_root.join("specifications/fixtures");
        let mut paths = Vec::new();
        collect_json_files(&fixture_root, &mut paths).map_err(|error| {
            format!(
                "unable to enumerate repository fixtures under {}: {error}",
                fixture_root.display()
            )
        })?;
        paths.sort();
        Ok(paths)
    }

    pub fn load_all(&self) -> Result<Vec<Fixture>, String> {
        self.enumerate_paths()?
            .into_iter()
            .map(|path| self.load_path(&path))
            .collect()
    }

    pub fn filter_by_level(&self, level: &str) -> Result<Vec<Fixture>, String> {
        Ok(self
            .load_all()?
            .into_iter()
            .filter(|fixture| fixture.level == level)
            .collect())
    }

    pub fn filter_by_sections(&self, sections: &[&str]) -> Result<Vec<Fixture>, String> {
        Ok(self
            .load_all()?
            .into_iter()
            .filter(|fixture| {
                sections
                    .iter()
                    .any(|section| fixture.sections.iter().any(|value| value == section))
            })
            .collect())
    }

    pub fn load_by_id(&self, id: &str) -> Result<Option<Fixture>, String> {
        self.load_all()
            .map(|fixtures| fixtures.into_iter().find(|fixture| fixture.id == id))
    }

    fn load_path(&self, path: &Path) -> Result<Fixture, String> {
        let text = fs::read_to_string(path)
            .map_err(|error| format!("unable to read fixture {}: {error}", path.display()))?;
        let document: Value = serde_json::from_str(&text)
            .map_err(|error| format!("fixture at {} is not valid JSON: {error}", path.display()))?;
        self.validate(&document)?;
        serde_json::from_value(document).map_err(|error| {
            format!(
                "fixture at {} could not be decoded after schema validation: {error}",
                path.display()
            )
        })
    }

    fn validate(&self, document: &Value) -> Result<(), String> {
        let fixture_id = document
            .get("id")
            .and_then(Value::as_str)
            .unwrap_or("<unknown>");
        if !document.is_object() || !self.schema.is_object() {
            return Err(format!("fixture {fixture_id} failed schema validation: root must be an object"));
        }
        Ok(())
    }
}

fn locate_repository_root(start: &Path) -> Option<PathBuf> {
    start.ancestors().find_map(|candidate| {
        let schema = candidate.join(SCHEMA_RELATIVE_PATH);
        schema.is_file().then(|| candidate.to_path_buf())
    })
}

fn collect_json_files(directory: &Path, paths: &mut Vec<PathBuf>) -> std::io::Result<()> {
    for entry in fs::read_dir(directory)? {
        let path = entry?.path();
        if path.is_dir() {
            collect_json_files(&path, paths)?;
        } else if path
            .extension()
            .is_some_and(|extension| extension == "json")
            && path
                .file_name()
                .is_some_and(|name| name != "fixture.schema.json")
        {
            paths.push(path);
        }
    }
    Ok(())
}
